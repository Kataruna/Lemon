import SwiftUI
import UniformTypeIdentifiers

enum JobState: Equatable {
    case idle, running(Double?), done(String), note(String), failed(String)
    var finished: Bool { if case .idle = self { return false }; if case .running = self { return false }; return true }
    /// How far along this job is, for the overall progress.
    var fraction: Double { if case .running(let p) = self { return p ?? 0 }; return finished ? 1 : 0 }
    /// Which case, ignoring the payload: what row animations key on, so progress ticks don't each start one.
    var phase: Int {
        switch self { case .idle: 0; case .running(let p): p == nil ? 1 : 2; case .done: 3; case .note: 4; case .failed: 5 }
    }
}

@MainActor final class Job: ObservableObject, Identifiable {
    let url: URL
    let options: [Preset]
    @Published var preset: Preset?
    @Published var state = JobState.idle
    /// Looked up once: rows redraw on every progress tick, and the icon lookup touches the disk.
    private(set) lazy var icon = NSWorkspace.shared.icon(forFile: url.path)
    init(_ url: URL, options: [Preset]) {
        self.url = url; self.options = options
        preset = options.first { $0.strength == nil && $0.removeBackground == nil && !$0.isResize } ?? options.first
    }

    func run() async {
        guard let preset, state == .idle else { return }
        state = .running(nil)
        do {
            let r = try await convert(url, preset) { p in Task { @MainActor in if case .running = self.state { self.state = .running(p) } } }
            switch r {
            case .done(let u): state = .done(u.lastPathComponent)
            case .alreadyOptimized: state = .note("Already optimized")
            case .alreadySmall: state = .note("Already small enough")
            }
        } catch is CancellationError { state = .idle }
        catch let e as LemonError { state = .failed(e.message) }
        catch { state = .failed(error.localizedDescription) }
    }
}

/// Overall progress of the batch that's converting (nil when idle) and how many of its files are done.
/// Kept apart from the job list so its frequent updates only redraw the progress views, not the whole list.
@MainActor final class BatchProgress: ObservableObject {
    @Published var overall: Double?
    @Published var counts = (done: 0, total: 0)
}

@MainActor final class Queue: ObservableObject {
    @Published var jobs: [Job] = []
    @Published var running = false
    let progress = BatchProgress()
    private var batch: [Job] = []
    private var task: Task<Void, Never>?
    private var poll: Task<Void, Never>?
    private var wheelJobs: [Job] = []
    /// Jobs asked to run that haven't started yet.
    private var waiting: [Job] = []
    /// Everything in `batch` and `late`, for a quick "already asked to run?" check (a big batch is thousands of jobs).
    private var queued = Set<ObjectIdentifier>()
    /// Asked to run after Cancel, while the cancelled batch winds down: they start once it has.
    private var late: [Job] = []
    private var cancelled = false

    /// Files (folders are searched) Lemon can convert, in order, without duplicates.
    static func files(in urls: [URL]) -> [URL] {
        var out: [URL] = [], seen = Set<URL>()
        for u in urls {
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: u.path, isDirectory: &isDir) else { continue }
            let found = isDir.boolValue
                ? (FileManager.default.enumerator(at: u, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])?.allObjects as? [URL] ?? [])
                : [u]
            for f in found where kindOf[ext(of: f)] != nil && seen.insert(f.standardizedFileURL).inserted { out.append(f) }
        }
        return out
    }

    /// Adds files and folders. Returns the jobs for them: new ones, plus (with `reuseWaiting`) ones already waiting.
    @discardableResult
    func add(_ urls: [URL], reuseWaiting: Bool = false) -> [Job] {
        var active: [URL: Job] = [:]
        for j in jobs where !j.state.finished { active[j.url.standardizedFileURL] = j }
        var optionsByExt: [String: [Preset]] = [:]   // presets depend only on the extension
        var result: [Job] = [], added: [Job] = []
        for u in Self.files(in: urls) {
            if let existing = active[u.standardizedFileURL] {
                if reuseWaiting, existing.state == .idle { result.append(existing) }
                continue
            }
            let e = ext(of: u)
            let options = optionsByExt[e] ?? presets(for: u)
            optionsByExt[e] = options
            let j = Job(u, options: options)
            added.append(j); result.append(j)
        }
        withAnimation(added.count <= 200 ? .snappy : nil) { jobs += added }   // one change notification, not one per file
        return result
    }

    /// Wheel drop: convert these files right away with the picked preset, and report with a notification.
    /// Files already waiting in the queue are included. In a mixed drag, files the preset can't handle stay queued.
    func addAndRun(_ urls: [URL], _ p: Preset) {
        var picked: [Job] = []
        for j in add(urls, reuseWaiting: true) {
            guard let o = j.options.first(where: { $0.name == p.name && $0.ext == p.ext }) else { continue }
            j.preset = o; picked.append(j)
        }
        wheelJobs += picked
        run(picked)
    }

    /// The Convert button: everything waiting in the list.
    func start() { run(jobs) }

    /// Converts these jobs (the ones still waiting). While a batch runs, they join it.
    private func run(_ candidates: [Job]) {
        let new = candidates.filter { j in j.state == .idle && j.preset != nil && !queued.contains(ObjectIdentifier(j)) }
        guard !new.isEmpty else { return }
        queued.formUnion(new.map(ObjectIdentifier.init))
        if running {
            // The running loop picks them up as slots free; after Cancel it won't, so they wait for the next batch.
            if cancelled { late += new } else { waiting += new; batch += new }
            return
        }
        running = true; cancelled = false
        waiting = new; batch = new; refreshOverall()
        // Sample rather than observe every job: each file can report hundreds of updates.
        poll?.cancel()
        poll = Task { while !Task.isCancelled { refreshOverall(); try? await Task.sleep(nanoseconds: 200_000_000) } }
        let width = max(2, ProcessInfo.processInfo.activeProcessorCount / 2)
        task = Task {
            await withTaskGroup(of: Void.self) { g in
                var active = 0
                while true {
                    while active < width, !cancelled, !waiting.isEmpty {
                        let j = waiting.removeFirst()
                        active += 1; g.addTask { await j.run() }
                    }
                    if active == 0 { break }
                    await g.next(); active -= 1
                }
            }
            waiting.removeAll()   // after Cancel, the rest stay in the list, waiting
            poll?.cancel()
            refreshOverall()
            running = false; batch = []; queued = []
            progress.overall = nil
            // Jobs that finished or were cancelled; ones dropped on the wheel after Cancel are reported with their own batch.
            let lateIDs = Set(late.map(ObjectIdentifier.init))
            let report = wheelJobs.filter { !lateIDs.contains(ObjectIdentifier($0)) }
            wheelJobs.removeAll { !lateIDs.contains(ObjectIdentifier($0)) }
            if !report.isEmpty { notify(report) }
            let next = late; late = []
            run(next)
        }
    }

    private func refreshOverall() {
        guard !batch.isEmpty else { return }
        let p = batch.reduce(0) { $0 + $1.state.fraction } / Double(batch.count)
        if progress.overall.map({ abs($0 - p) >= 0.005 }) ?? true { progress.overall = p }
        let done = batch.filter(\.state.finished).count
        if progress.counts.done != done || progress.counts.total != batch.count { progress.counts = (done, batch.count) }
    }

    func cancel() {
        cancelled = true; task?.cancel()
        // Not started, and now won't be: free them so dropping them on the wheel again runs them after the wind-down.
        for j in waiting + late { queued.remove(ObjectIdentifier(j)) }
        waiting.removeAll(); late.removeAll()
    }
    /// While converting, only finished rows go; when idle, the whole list does.
    func clear() { withAnimation(.snappy) { jobs.removeAll { running ? $0.state.finished : true } } }

    func setAll(_ kind: Kind, _ name: String) {
        for j in jobs where kindOf[ext(of: j.url)] == kind && j.state == .idle {
            if let p = j.options.first(where: { $0.name == name }) { j.preset = p }
        }
    }
}

@MainActor let sharedQueue = Queue()

@MainActor func notify(_ jobs: [Job]) {
    var ok = 0, same = 0, failed: [String] = []
    for j in jobs {
        switch j.state {
        case .done: ok += 1
        case .note: same += 1
        case .failed(let m): failed.append(m)
        default: break   // cancelled
        }
    }
    var parts = ["\(ok) of \(jobs.count) done"]
    if same > 0 { parts.append("\(same) already optimized") }
    if let m = failed.first { parts.append("\(failed.count) failed: \(m)") }
    let msg = parts.joined(separator: ", ")
    let p = Process(); p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
    p.arguments = ["-e", "on run argv\ndisplay notification (item 1 of argv) with title \"Lemon\"\nend run", msg]
    try? p.run()
}

struct ContentView: View {
    @ObservedObject private var queue = sharedQueue
    @State private var picking = false
    @State private var targeted = false

    var body: some View {
        Group {
            if queue.jobs.isEmpty {
                VStack(spacing: 14) {
                    FloatingLemon(targeted: targeted)
                    Text("Drop files or folders here").font(.title3.weight(.medium))
                    Text("Or shake / Shift-drag files anywhere for the wheel").font(.callout).foregroundStyle(.secondary)
                    Button { picking = true } label: { Label("Choose Files…", systemImage: "plus") }
                        .glassButton().controlSize(.large).padding(.top, 4)
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
                .transition(.opacity.combined(with: .scale(scale: 0.94)))
            } else {
                // Rows are content, so they sit on the see-through window as plain cards; glass is kept for controls.
                List(queue.jobs) {
                    JobRow(job: $0)
                        .listRowBackground(Color.clear).listRowSeparator(.hidden)
                        .listRowInsets(EdgeInsets(top: 3, leading: 10, bottom: 3, trailing: 10))
                }
                .scrollContentBackground(.hidden)
                .safeAreaInset(edge: .bottom) { actionBar.padding(.bottom, 14) }
                .transition(.opacity)
            }
        }
        .frame(minWidth: 620, minHeight: 360)
        .overlay {
            if targeted {
                RoundedRectangle(cornerRadius: 18, style: .continuous).fill(tangerine.opacity(0.06))
                    .strokeBorder(tangerine, lineWidth: 2).padding(8).allowsHitTesting(false)
                    .transition(.opacity.combined(with: .scale(scale: 1.02)))
            }
        }
        .animation(.spring(duration: 0.3), value: targeted)
        .animation(.spring(duration: 0.4), value: queue.jobs.isEmpty)
        .seeThroughWindow()
        .dropDestination(for: URL.self, action: { urls, _ in queue.add(urls); return true }, isTargeted: { targeted = $0 })
        .fileImporter(isPresented: $picking, allowedContentTypes: [.item, .folder], allowsMultipleSelection: true) { if case .success(let u) = $0 { queue.add(u) } }
        .toolbar {
            ToolbarItem { Button { picking = true } label: { Label("Add", systemImage: "plus") } }
            ToolbarItem {
                Menu("Set all to") {
                    ForEach(Kind.allCases, id: \.self) { k in
                        let js = queue.jobs.filter { kindOf[ext(of: $0.url)] == k }
                        if let first = js.first {
                            Section(k.rawValue) {
                                ForEach(first.options) { p in Button(p.name) { queue.setAll(k, p.name) } }
                            }
                        }
                    }
                }.disabled(queue.jobs.isEmpty)
            }
            ToolbarItem { Button("Clear") { queue.clear() }.disabled(queue.jobs.isEmpty) }
        }
    }

    /// Floating glass bar over the list: batch progress and the main action. Lives in the content, not the toolbar,
    /// because the toolbar ignores button styles and would render Convert as a plain grey capsule.
    private var actionBar: some View {
        GlassBox {
            HStack(spacing: 12) {
                BatchStatus(progress: queue.progress)
                if queue.running {
                    Button("Cancel", role: .cancel) { queue.cancel() }.glassButton().controlSize(.large).tint(nil as Color?)
                        .transition(.scale(scale: 0.8).combined(with: .opacity))
                } else {
                    Button { queue.start() } label: { Label("Convert", systemImage: "arrow.triangle.2.circlepath") }
                        .glassButton(prominent: true).controlSize(.large)
                        .disabled(!queue.jobs.contains { $0.state == .idle && $0.preset != nil })
                        .transition(.scale(scale: 0.8).combined(with: .opacity))
                }
            }
        }
        .animation(.spring(duration: 0.35, bounce: 0.25), value: queue.running)
    }
}

/// "2/5 · 40%" with a bar, inside the action bar while a batch runs.
struct BatchStatus: View {
    @ObservedObject var progress: BatchProgress
    var body: some View {
        if let p = progress.overall {
            HStack(spacing: 8) {
                ProgressView(value: p).frame(width: 140)
                Text("\(progress.counts.done)/\(progress.counts.total) · \(Int(p * 100))%").font(.callout.monospacedDigit()).foregroundStyle(.secondary).fixedSize()
            }
            .padding(.horizontal, 14).frame(height: 36).glassCapsule()
            .transition(.move(edge: .trailing).combined(with: .opacity))
        }
    }
}

/// The empty screen's lemon: bobs gently, and leans in when files are dragged over the window.
struct FloatingLemon: View {
    var targeted: Bool
    var body: some View {
        Bobbing(content: Text("🍋").font(.system(size: 64)).padding(22)
            .background(Circle().fill(tangerine.opacity(targeted ? 0.3 : 0.14))))
            .fixedSize()
            .scaleEffect(targeted ? 1.12 : 1)
    }
}

/// Plays the bob as a Core Animation layer animation, which the window server runs on its own. A SwiftUI
/// phaseAnimator re-laid-out the whole window every frame instead: ~14% CPU with the window just sitting open.
struct Bobbing<Content: View>: NSViewRepresentable {
    let content: Content
    func makeNSView(context: Context) -> BobView<Content> { BobView(rootView: content) }
    func updateNSView(_ v: BobView<Content>, context: Context) { v.rootView = content }
}

final class BobView<Content: View>: NSHostingView<Content> {
    override func layout() {
        super.layout()
        wantsLayer = true
        guard let l = layer, bounds.width > 0, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        // Rebuilt only when the size changes: the turn is about the view's center.
        let key = "bob \(bounds.size)"
        guard l.animation(forKey: key) == nil else { return }
        l.removeAllAnimations()
        // Layer space is y-up with counterclockwise angles: 3 pt down and 3° clockwise, to 5 pt up and 4° the other way.
        let c = CGPoint(x: bounds.midX, y: bounds.midY)
        func pose(_ y: CGFloat, _ degrees: CGFloat) -> NSValue {
            var m = CATransform3DMakeTranslation(c.x, c.y + y, 0)
            m = CATransform3DRotate(m, degrees * .pi / 180, 0, 0, 1)
            return NSValue(caTransform3D: CATransform3DTranslate(m, -c.x, -c.y, 0))
        }
        let a = CABasicAnimation(keyPath: "transform")
        a.fromValue = pose(-3, -3); a.toValue = pose(5, 4)
        a.duration = 1.8; a.autoreverses = true; a.repeatCount = .infinity
        a.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        l.add(a, forKey: key)
    }
}

struct JobRow: View {
    @ObservedObject var job: Job
    var body: some View {
        HStack {
            Image(nsImage: job.icon).resizable().frame(width: 28, height: 28)
            Text(job.url.lastPathComponent).lineLimit(1).truncationMode(.middle)
            Spacer()
            Group { status }
                .transition(.asymmetric(insertion: .scale(scale: 0.6).combined(with: .opacity), removal: .opacity))
        }
        .padding(.horizontal, 10).padding(.vertical, 7)
        .background(.background.opacity(0.35), in: .rect(cornerRadius: 12, style: .continuous))
        .animation(.spring(duration: 0.35, bounce: 0.3), value: job.state.phase)
    }

    @ViewBuilder private var status: some View {
        switch job.state {
        case .idle:
            if job.options.isEmpty { Text("No converter installed").foregroundStyle(.secondary) }
            else {
                Picker("", selection: $job.preset) { ForEach(job.options) { Text($0.name).tag(Optional($0)) } }
                    .labelsHidden().frame(width: 210)
            }
        case .running(let p):
            if let p {
                ProgressView(value: p).frame(width: 140)
                Text("\(Int(p * 100))%").font(.caption.monospacedDigit()).foregroundStyle(.secondary).frame(width: 34, alignment: .trailing)
            } else { ProgressView().controlSize(.small) }
        case .done(let n):
            Label(n, systemImage: "checkmark.circle.fill").foregroundStyle(.green).lineLimit(1).truncationMode(.middle)
        case .note(let m): Label(m, systemImage: "equal.circle").foregroundStyle(.secondary)
        case .failed(let m): Label(m, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).lineLimit(2).help(m)
        }
    }
}

/// The desktop behind the window, blurred (like Finder's sidebar), without changing the window itself.
struct DesktopBlur: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView(); v.blendingMode = .behindWindow; v.material = .underWindowBackground; v.state = .followsWindowActiveState
        return v
    }
    func updateNSView(_ v: NSVisualEffectView, context: Context) {}
}

extension View {
    /// The window shows the desktop through a blur instead of an opaque background (macOS 15+; plain window on 14).
    @ViewBuilder func seeThroughWindow() -> some View {
        if #available(macOS 15, *) {
            containerBackground(.ultraThinMaterial, for: .window).toolbarBackgroundVisibility(.hidden, for: .windowToolbar)
        } else { self }
    }

    /// A Liquid Glass capsule behind non-button controls (material before macOS 26).
    @ViewBuilder func glassCapsule() -> some View {
        if #available(macOS 26, *) { glassEffect(.regular, in: .capsule) } else { background(.regularMaterial, in: Capsule()) }
    }

    /// Liquid Glass buttons on macOS 26, bordered ones before. On 26 plain `.glass` already takes the tint and stays a
    /// capsule (`.glassProminent` ignores buttonBorderShape), so `prominent` only matters on older systems.
    @ViewBuilder func glassButton(prominent: Bool = false) -> some View {
        if #available(macOS 26, *) { buttonStyle(.glass) }
        else {
            if prominent { buttonStyle(.borderedProminent) } else { buttonStyle(.bordered) }
        }
    }
}

final class Delegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ n: Notification) {
        WheelController.shared.start()
        // In the Dock only while a window is open; closed, Lemon carries on from the menu bar (and the wheel).
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: nil, queue: .main) { n in
            let closing = n.object as? NSWindow
            DispatchQueue.main.async {
                let open = NSApp.windows.contains { $0 !== closing && $0.isVisible && $0.canBecomeMain && !($0 is NSPanel) }
                if !open { NSApp.setActivationPolicy(.accessory) }
            }
        }
        NotificationCenter.default.addObserver(forName: NSWindow.didBecomeKeyNotification, object: nil, queue: .main) { n in
            guard let w = n.object as? NSWindow, w.canBecomeMain, !(w is NSPanel), NSApp.activationPolicy() != .regular else { return }
            NSApp.setActivationPolicy(.regular)
        }
    }
    // Open With / drops on the Dock icon. SwiftUI's onOpenURL only delivers one of several files opened together
    // (measured: 1 of 3), so take the open-documents Apple Event directly.
    func applicationWillFinishLaunching(_ n: Notification) {
        NSAppleEventManager.shared().setEventHandler(self, andSelector: #selector(openDocuments(_:reply:)),
                                                     forEventClass: AEEventClass(kCoreEventClass), andEventID: AEEventID(kAEOpenDocuments))
    }
    @MainActor @objc func openDocuments(_ event: NSAppleEventDescriptor, reply: NSAppleEventDescriptor) {
        guard let list = event.paramDescriptor(forKeyword: keyDirectObject) else { return }
        sharedQueue.add((0..<list.numberOfItems).compactMap { list.atIndex($0 + 1)?.fileURLValue })
        AppHooks.openWindow?()
    }
    // Lemon keeps running (in the menu bar) so the wheel works with no window open.
    func applicationShouldTerminateAfterLastWindowClosed(_ app: NSApplication) -> Bool { false }
    func applicationShouldHandleReopen(_ app: NSApplication, hasVisibleWindows: Bool) -> Bool {
        if !hasVisibleWindows { AppHooks.openWindow?() }
        return true
    }
}

/// The menu bar icon: the app icon's lemon, or its slice filling up segment by segment while files convert.
/// It also hands the app a way to open the main window, since it's the one view that's always alive.
struct MenuBarIcon: View {
    @Environment(\.openWindow) private var open
    @ObservedObject private var progress = sharedQueue.progress
    var body: some View {
        // Every new image makes the status item redraw, and menu bar managers (Bartender) re-capture the item each time,
        // so the slice moves in 8 steps and each step's image is drawn once and reused.
        Image(nsImage: menuBarImage(step: progress.overall.map { Int(($0 * 8).rounded(.down)) }))
            .onAppear { AppHooks.openWindow = { NSApp.setActivationPolicy(.regular); open(id: "main"); NSApp.activate(ignoringOtherApps: true) } }
    }
}

/// A layer of the app icon (mac/Lemon.icon): 1 the slice, 2 the half lemon, 3 its shine. From the app bundle, or from
/// the source tree under `swift run`.
private func iconLayer(_ n: Int) -> NSImage {
    let name = "Lemon \(n)"
    let url = Bundle.main.url(forResource: name, withExtension: "svg")
        ?? URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("../../../Lemon.icon/Assets/\(name).svg")
    return NSImage(contentsOf: url) ?? NSImage()
}

@MainActor private var menuBarImages: [Int: NSImage] = [:]
/// Read and parsed once; every progress step draws from them.
@MainActor private let iconLayers = (iconLayer(1), iconLayer(2), iconLayer(3))

/// `step` 0...8 is how many of the slice's segments are lit; nil is the plain icon. Cached, so the same step is the same image.
@MainActor func menuBarImage(step: Int?) -> NSImage {
    let key = step ?? -1
    if let cached = menuBarImages[key] { return cached }
    let image = menuBarImage(drawing: step)
    menuBarImages[key] = image
    return image
}

/// Template images only use alpha. The layers are drawn on a 1024 canvas; the numbers below are where their art sits.
@MainActor private func menuBarImage(drawing step: Int?) -> NSImage {
    let (slice, half, shine) = iconLayers
    let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { r in
        guard let step else {
            // The half lemon over the slice, shine cut out. The art spans 235...776.
            let k = r.width / 551, art = NSRect(x: -230 * k, y: -243 * k, width: 1024 * k, height: 1024 * k)
            slice.draw(in: art); half.draw(in: art)
            shine.draw(in: art, from: .zero, operation: .destinationOut, fraction: 1)
            return true
        }
        // Just the slice (centered at 524, 500), faint, with its segments lighting up clockwise from 12 o'clock.
        let k = r.width / 490, art = NSRect(x: -279 * k, y: -255 * k, width: 1024 * k, height: 1024 * k)
        slice.draw(in: art, from: .zero, operation: .sourceOver, fraction: 0.3)
        guard step > 0 else { return true }
        let c = NSPoint(x: r.midX, y: r.midY), pie = NSBezierPath()
        pie.move(to: c)
        pie.appendArc(withCenter: c, radius: r.width, startAngle: 90, endAngle: 90 - 45 * CGFloat(step), clockwise: true)
        pie.close(); pie.addClip()
        slice.draw(in: art)
        return true
    }
    image.isTemplate = true
    return image
}

/// Settings… in the menu. Brings Lemon forward first: with its Dock icon hidden, a bare SettingsLink opens the
/// window behind whatever app is in front.
struct SettingsButton: View {
    @Environment(\.openSettings) private var openSettings
    var body: some View {
        Button("Settings…") {
            NSApp.setActivationPolicy(.regular); openSettings()
            DispatchQueue.main.async { NSApp.activate(ignoringOtherApps: true) }   // ignored if done in the same turn as the policy change
        }
            .keyboardShortcut(",")
    }
}

/// "Converting 3 of 7 · 42%" and a Cancel item at the top of the menu while a batch runs.
struct MenuStatus: View {
    @ObservedObject private var progress = sharedQueue.progress
    var body: some View {
        if let p = progress.overall {
            Text("Converting \(min(progress.counts.done + 1, progress.counts.total)) of \(progress.counts.total) · \(Int(p * 100))%")
            Button("Cancel") { sharedQueue.cancel() }
            Divider()
        }
    }
}

struct BackgroundSettings: View {
    @ObservedObject private var store = ModelStore.shared
    var body: some View {
        Form {
            Section {
                ForEach(BiRefNet.all, id: \.file) { m in
                    HStack {
                        VStack(alignment: .leading) {
                            Text(m.name)
                            Text(ByteCountFormatter.string(fromByteCount: m.bytes, countStyle: .file)).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        if let p = store.progress[m.file] {
                            ProgressView(value: p).frame(width: 120)
                            Button("Cancel") { store.cancel(m) }
                        } else if m.installed {
                            Label("Downloaded", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                            Button("Delete") { store.delete(m) }
                        } else {
                            Button("Download") { store.download(m) }
                        }
                    }
                }
                if let e = store.error { Text(e).foregroundStyle(.red).font(.caption) }
            } header: {
                Text("Background remover")
            } footer: {
                Text("Apple Vision is built in. BiRefNet runs on this Mac and handles large images and fine edges better. Models are checked against a pinned SHA-256 and stored in Application Support/Lemon/models.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .settingsPage()
    }
}

@main struct LemonApp: App {
    @NSApplicationDelegateAdaptor(Delegate.self) private var delegate
    var body: some Scene {
        Window("Lemon", id: "main") { ContentView().tint(tangerine) }
        Settings { SettingsView() }
        MenuBarExtra { 
            MenuStatus()
            Button("Open Lemon") { AppHooks.openWindow?() }
            SettingsButton()
            Text("Shake or Shift-drag files to open the wheel").font(.caption)
            Divider()
            Button("Quit Lemon") { NSApp.terminate(nil) }.keyboardShortcut("q")
        } label: { MenuBarIcon() }
    }
}
