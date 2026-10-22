import SwiftUI
import UniformTypeIdentifiers

enum JobState: Equatable {
    case idle, running(Double?), done(String), note(String), failed(String)
    var finished: Bool { if case .idle = self { return false }; if case .running = self { return false }; return true }
    /// How far along this job is, for the overall progress.
    var fraction: Double { if case .running(let p) = self { return p ?? 0 }; return finished ? 1 : 0 }
}

@MainActor final class Job: ObservableObject, Identifiable {
    let url: URL
    let options: [Preset]
    @Published var preset: Preset?
    @Published var state = JobState.idle
    init(_ url: URL) { self.url = url; options = presets(for: url); preset = options.first { $0.strength == nil && $0.removeBackground == nil } ?? options.first }

    func run() async {
        guard let preset, !state.finished || state != .idle else { return }
        state = .running(nil)
        do {
            let r = try await convert(url, preset) { p in Task { @MainActor in if case .running = self.state { self.state = .running(p) } } }
            switch r {
            case .done(let u): state = .done(u.lastPathComponent)
            case .alreadyOptimized: state = .note("Already optimized")
            }
        } catch is CancellationError { state = .idle }
        catch let e as LemonError { state = .failed(e.message) }
        catch { state = .failed(error.localizedDescription) }
    }
}

@MainActor final class Queue: ObservableObject {
    @Published var jobs: [Job] = []
    @Published var running = false
    /// Overall progress of the batch that's converting (nil when idle), and how many of its files are done.
    @Published var overall: Double?
    @Published var counts = (done: 0, total: 0)
    private var batch: [Job] = []
    private var task: Task<Void, Never>?
    private var wheelJobs: [Job] = []

    @discardableResult
    func add(_ urls: [URL]) -> [Job] {
        var added: [Job] = []
        for u in urls {
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: u.path, isDirectory: &isDir) else { continue }
            if isDir.boolValue {
                let it = FileManager.default.enumerator(at: u, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])
                added += add((it?.allObjects as? [URL] ?? []).filter { kindOf[ext(of: $0)] != nil })
            } else if kindOf[ext(of: u)] != nil, !jobs.contains(where: { $0.url == u && !$0.state.finished }) {
                let j = Job(u); jobs.append(j); added.append(j)
            }
        }
        return added
    }

    /// Wheel drop: convert right away with the picked preset and report with a notification.
    func addAndRun(_ urls: [URL], _ p: Preset) {
        for j in add(urls) {
            if let o = j.options.first(where: { $0.name == p.name && $0.ext == p.ext }) { j.preset = o; wheelJobs.append(j) }
            else { jobs.removeAll { $0 === j } }
        }
        start()
    }

    func start() {
        let pending = jobs.filter { $0.state == .idle && $0.preset != nil }
        guard !pending.isEmpty, !running else { return }
        running = true
        batch = pending; refreshOverall()
        // Sample rather than observe every job: each file can report hundreds of updates.
        Task { while running { refreshOverall(); try? await Task.sleep(nanoseconds: 200_000_000) } }
        let width = max(2, ProcessInfo.processInfo.activeProcessorCount / 2)
        task = Task {
            await withTaskGroup(of: Void.self) { g in
                var it = pending.makeIterator()
                for _ in 0..<width { if let j = it.next() { g.addTask { await j.run() } } }
                for await _ in g { if let j = it.next() { g.addTask { await j.run() } } }
            }
            running = false
            overall = nil
            if !wheelJobs.isEmpty { notify(wheelJobs); wheelJobs = [] }
            start()   // jobs added while this batch ran
        }
    }

    private func refreshOverall() {
        guard !batch.isEmpty else { return }
        let p = batch.reduce(0) { $0 + $1.state.fraction } / Double(batch.count)
        if overall.map({ abs($0 - p) >= 0.005 }) ?? true { overall = p }
        let done = batch.filter(\.state.finished).count
        if counts.done != done || counts.total != batch.count { counts = (done, batch.count) }
    }

    func cancel() { task?.cancel() }
    func clear() { jobs.removeAll { $0.state != .idle || !running } }

    func setAll(_ kind: Kind, _ name: String) {
        for j in jobs where kindOf[ext(of: j.url)] == kind && j.state == .idle {
            if let p = j.options.first(where: { $0.name == name }) { j.preset = p }
        }
    }
}

@MainActor let sharedQueue = Queue()

@MainActor func notify(_ jobs: [Job]) {
    let failed = jobs.filter { if case .failed = $0.state { return true }; return false }
    let ok = jobs.filter { if case .done = $0.state { return true }; return false }.count
    var msg = "\(ok) of \(jobs.count) done"
    if let f = failed.first, case .failed(let m) = f.state { msg += " – \(failed.count) failed: \(m)" }
    else if ok < jobs.count { msg += " (already optimized)" }
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
                VStack(spacing: 10) {
                    Text("🍋").font(.system(size: 64)).padding(18).background(Circle().fill(tangerine.opacity(0.18)))
                    Text("Drop files or folders here").font(.title3)
                    Button("Choose Files…") { picking = true }
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                List(queue.jobs) { JobRow(job: $0) }
            }
        }
        .frame(minWidth: 620, minHeight: 360)
        .overlay { if targeted { RoundedRectangle(cornerRadius: 12).strokeBorder(tangerine, lineWidth: 3).padding(6) } }
        .dropDestination(for: URL.self, action: { urls, _ in queue.add(urls); return true }, isTargeted: { targeted = $0 })
        .fileImporter(isPresented: $picking, allowedContentTypes: [.item, .folder], allowsMultipleSelection: true) { if case .success(let u) = $0 { queue.add(u) } }
        .onOpenURL { queue.add([$0]) }
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
            if let p = queue.overall {
                ToolbarItem {
                    HStack(spacing: 6) {
                        ProgressView(value: p).frame(width: 110)
                        Text("\(queue.counts.done)/\(queue.counts.total) · \(Int(p * 100))%").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                    }
                }
            }
            ToolbarItem { Button("Clear") { queue.clear() }.disabled(queue.jobs.isEmpty) }
            ToolbarItem {
                if queue.running { Button("Cancel") { queue.cancel() } }
                else { Button("Convert") { queue.start() }.buttonStyle(.borderedProminent).disabled(queue.jobs.isEmpty) }
            }
        }
    }
}

struct JobRow: View {
    @ObservedObject var job: Job
    var body: some View {
        HStack {
            Image(nsImage: NSWorkspace.shared.icon(forFile: job.url.path)).resizable().frame(width: 28, height: 28)
            Text(job.url.lastPathComponent).lineLimit(1).truncationMode(.middle)
            Spacer()
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
            case .done(let n): Label(n, systemImage: "checkmark.circle.fill").foregroundStyle(.green).lineLimit(1).truncationMode(.middle)
            case .note(let m): Label(m, systemImage: "equal.circle").foregroundStyle(.secondary)
            case .failed(let m): Label(m, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).lineLimit(2).help(m)
            }
        }
    }
}

final class Delegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ n: Notification) { WheelController.shared.start() }
    // Lemon keeps running (in the menu bar) so the wheel works with no window open.
    func applicationShouldTerminateAfterLastWindowClosed(_ app: NSApplication) -> Bool { false }
    func applicationShouldHandleReopen(_ app: NSApplication, hasVisibleWindows: Bool) -> Bool {
        if !hasVisibleWindows { AppHooks.openWindow?() }
        return true
    }
}

/// The menu bar icon: a lemon, or a progress ring around a small lemon while files convert.
/// It also hands the app a way to open the main window, since it's the one view that's always alive.
struct MenuBarIcon: View {
    @Environment(\.openWindow) private var open
    @ObservedObject private var queue = sharedQueue
    var body: some View {
        // Whole percents only, so the icon redraws at most 100 times per batch.
        Image(nsImage: menuBarImage(progress: queue.overall.map { ($0 * 100).rounded() / 100 }))
            .onAppear { AppHooks.openWindow = { open(id: "main"); NSApp.activate(ignoringOtherApps: true) } }
    }
}

/// Lemon silhouette: a body with pointed tips, a highlight cut out of it, and a separate leaf.
/// `scale` 1 fills an 18 pt icon. Built along the x axis, then tilted up to the right.
private func lemonPath(center c: CGPoint, scale s: CGFloat) -> NSBezierPath {
    let p = NSBezierPath()
    p.move(to: NSPoint(x: -8.2, y: 0))
    p.curve(to: NSPoint(x: 8.2, y: 0), controlPoint1: NSPoint(x: -5.4, y: 6.6), controlPoint2: NSPoint(x: 5.4, y: 6.6))
    p.curve(to: NSPoint(x: -8.2, y: 0), controlPoint1: NSPoint(x: 5.4, y: -6.6), controlPoint2: NSPoint(x: -5.4, y: -6.6))
    p.close()
    p.append(NSBezierPath(ovalIn: NSRect(x: -4.6, y: 1.0, width: 4.0, height: 1.4)))   // highlight (even-odd hole)
    // Leaf off the top tip, pointing up and away, with a gap so it reads as separate.
    let leaf = NSBezierPath()
    leaf.move(to: NSPoint(x: 0, y: 0))
    leaf.curve(to: NSPoint(x: 4.4, y: 0), controlPoint1: NSPoint(x: 1.4, y: 2.2), controlPoint2: NSPoint(x: 3.2, y: 1.8))
    leaf.curve(to: NSPoint(x: 0, y: 0), controlPoint1: NSPoint(x: 3.2, y: -1.2), controlPoint2: NSPoint(x: 1.4, y: -1.4))
    var lt = AffineTransform(); lt.translate(x: 5.6, y: 2.6); lt.rotate(byDegrees: 100); leaf.transform(using: lt)
    p.append(leaf)
    p.windingRule = .evenOdd
    var t = AffineTransform(); t.translate(x: c.x - 0.4 * s, y: c.y - 1.3 * s); t.scale(s * 0.95); t.rotate(byDegrees: 30); p.transform(using: t)
    return p
}

func menuBarImage(progress: Double?) -> NSImage {
    let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { r in
        let c = CGPoint(x: r.midX, y: r.midY)
        NSColor.black.setFill(); NSColor.black.setStroke()
        guard let progress else { lemonPath(center: c, scale: 1).fill(); return true }
        // Template images only use alpha: a faint track, a solid arc from 12 o'clock clockwise, a small lemon inside.
        let track = NSBezierPath(ovalIn: r.insetBy(dx: 1.5, dy: 1.5)); track.lineWidth = 2
        NSColor.black.withAlphaComponent(0.3).setStroke(); track.stroke()
        let arc = NSBezierPath()
        arc.appendArc(withCenter: c, radius: 7.5, startAngle: 90, endAngle: 90 - 360 * max(progress, 0.02), clockwise: true)
        arc.lineWidth = 2; arc.lineCapStyle = .round
        NSColor.black.setStroke(); arc.stroke()
        lemonPath(center: c, scale: 0.5).fill()
        return true
    }
    image.isTemplate = true
    return image
}

/// "Converting 3 of 7 · 42%" and a Cancel item at the top of the menu while a batch runs.
struct MenuStatus: View {
    @ObservedObject private var queue = sharedQueue
    var body: some View {
        if let p = queue.overall {
            Text("Converting \(queue.counts.done + 1 > queue.counts.total ? queue.counts.total : queue.counts.done + 1) of \(queue.counts.total) · \(Int(p * 100))%")
            Button("Cancel") { queue.cancel() }
            Divider()
        }
    }
}

struct SettingsView: View {
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
        .formStyle(.grouped).frame(width: 480).tint(tangerine)
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
            SettingsLink { Text("Settings…") }.keyboardShortcut(",")
            Text("Shake or Shift-drag files to open the wheel").font(.caption)
            Divider()
            Button("Quit Lemon") { NSApp.terminate(nil) }.keyboardShortcut("q")
        } label: { MenuBarIcon() }
    }
}
