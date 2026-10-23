import SwiftUI
import AppKit

// MARK: shake detection (port of Lemon.Core/Wheel/ShakeDetector.cs)

struct ShakeDetector {
    static let minStroke = 40.0, reversals = 4, window = 0.9, jitter = 10.0

    struct Axis {
        var times: [Double] = [], strokeStart = 0.0, extreme = 0.0, dir = 0.0
        mutating func begin(_ p: Double) { times = []; strokeStart = p; extreme = p; dir = 0 }
        mutating func add(_ p: Double, _ t: Double) -> Bool {
            if dir == 0 {
                if abs(p - strokeStart) >= ShakeDetector.jitter { dir = p > strokeStart ? 1 : -1; extreme = p }
                return false
            }
            if (p - extreme) * dir > 0 { extreme = p; return false }
            if abs(extreme - p) < ShakeDetector.jitter { return false }
            // Direction changed. Only long swings count; a short one means the hand wasn't shaking.
            if abs(extreme - strokeStart) >= ShakeDetector.minStroke {
                times.append(t); if times.count > ShakeDetector.reversals { times.removeFirst() }
            } else { times = [] }
            strokeStart = extreme; extreme = p; dir = -dir
            return times.count == ShakeDetector.reversals && t - times[0] <= ShakeDetector.window
        }
    }

    var x = Axis(), y = Axis(), started = false
    mutating func reset() { started = false }
    mutating func add(_ p: NSPoint, _ t: Double) -> Bool {
        if !started { x.begin(p.x); y.begin(p.y); started = true; return false }
        let a = x.add(p.x, t), b = y.add(p.y, t)   // evaluate both so each axis stays current
        return a || b
    }
}

// MARK: which presets go on the wheel (port of ConversionWheel.cs)

private let favorites: [Kind: [String]] = [
    .image: ["PNG", "JPEG – High quality", "WebP – Web optimized", "PDF", "AVIF – Small", "Icon (256 px)", "GIF", "TIFF", "BMP"],
    .audio: ["MP3 – 320 kbps", "AAC (M4A) – 256 kbps", "FLAC – Lossless", "WAV – Uncompressed", "Opus – 128 kbps", "Ogg Vorbis – 192 kbps", "MP3 – 192 kbps", "AIFF – Uncompressed"],
    .video: ["MP4 (H.264)", "MP4 – 720p small", "WebM (VP9)", "Animated GIF", "MP3 – 320 kbps", "QuickTime MOV", "MKV (H.264)", "AAC (M4A) – 256 kbps"],
    .document: ["PDF", "Word (DOCX)", "HTML page", "Plain text", "Markdown", "OpenDocument (ODT)", "Rich Text (RTF)", "EPUB e-book"],
]

/// The wheel's rings. Tab cycles them, 1/2/3 jump straight to one.
enum WheelMode: Int, CaseIterable {
    case convert, optimize, tools
    var title: String { ["Convert", "Optimize", "Cut out"][rawValue] }
    func holds(_ p: Preset) -> Bool {
        switch self {
        case .convert: return p.strength == nil && p.removeBackground == nil
        case .optimize: return p.strength != nil
        case .tools: return p.removeBackground != nil
        }
    }
}

/// Up to 8 presets that work for every dragged file; a mixed drag falls back to what fits the main kind.
func wheelChoices(_ urls: [URL], _ mode: WheelMode) -> [Preset] {
    let optimize = mode == .optimize
    // Presets depend only on the extension, so a folder of 5,000 photos is one lookup, not 5,000.
    var fileCount: [String: Int] = [:]
    for u in urls where kindOf[ext(of: u)] != nil { fileCount[ext(of: u), default: 0] += 1 }
    let per = fileCount.keys.sorted().map { e in (kindOf[e], presets(for: URL(fileURLWithPath: "x." + e)).filter(mode.holds)) }
    guard !per.isEmpty else { return [] }
    // The main kind is the one with the most files, not the most extensions.
    var filesByKind: [Kind: Int] = [:]
    for (e, n) in fileCount { filesByKind[kindOf[e]!, default: 0] += n }
    let main = filesByKind.max { $0.value < $1.value }!.key
    func rank(_ p: Preset) -> Int { mode == .tools ? [BGEngine.vision, .lite, .full].firstIndex(of: p.removeBackground!)! : optimize ? [Strength.lossless, .balanced, .smaller].firstIndex(of: p.strength!)! : favorites[main]!.firstIndex(of: p.name) ?? 99 }
    func pick(_ lists: [[Preset]]) -> [Preset] {
        lists[0].filter { p in lists.dropFirst().allSatisfy { $0.contains(p) } }.sorted { rank($0) < rank($1) }
    }
    var out = pick(per.map(\.1))
    for p in pick(per.filter { $0.0 == main }.map(\.1)) where !out.contains(p) { out.append(p) }
    return Array(out.prefix(8))
}

/// "MP4" over "720p small": the format over what makes the preset different.
func wheelLabel(_ p: Preset) -> (String, String?) {
    if let e = p.removeBackground { return [BGEngine.vision: ("Quick", "Apple Vision"), .lite: ("Lite", "BiRefNet"), .full: ("Full", "Best quality")][e]! }
    if let s = p.strength { return (s.rawValue, [Strength.lossless: "Same pixels", .balanced: "Looks the same", .smaller: "Slightly lower quality"][s]) }
    let title = p.ext.uppercased()
    if let r = p.name.range(of: " – ") { return (title, String(p.name[r.upperBound...])) }
    if let a = p.name.firstIndex(of: "("), let b = p.name.firstIndex(of: ")"), a < b, !p.name.hasPrefix(title) { return (title, String(p.name[p.name.index(after: a)..<b])) }
    return (title, nil)
}

// MARK: the wheel

let tangerine = Color(red: 1.0, green: 0.55, blue: 0.0)
let tangerineDeep = Color(red: 1.0, green: 0.36, blue: 0.0)

@MainActor final class WheelModel: ObservableObject {
    static let size: CGFloat = 380, inner: CGFloat = 60, outer: CGFloat = 152
    @Published var mode = WheelMode.convert
    @Published var hovered: Preset?
    @Published var lemonHot = false
    @Published var switchHot = false
    @Published var shown = false
    private var byMode: [WheelMode: [Preset]] = [:]
    var choices: [Preset] { byMode[mode] ?? [] }
    /// Rings that have something on them for these files.
    var modes: [WheelMode] { WheelMode.allCases.filter { !(byMode[$0] ?? []).isEmpty } }
    var canSwitch: Bool { modes.count > 1 }
    private var flipTask: Task<Void, Never>?

    func load(_ urls: [URL]) {
        shown = false; hovered = nil; lemonHot = false; switchHot = false
        byMode = Dictionary(uniqueKeysWithValues: WheelMode.allCases.map { ($0, wheelChoices(urls, $0)) })
        mode = modes.first ?? .convert
    }
    func go(_ m: WheelMode) { if m != mode, modes.contains(m) { mode = m; hovered = nil } }
    func cycle(_ step: Int) {
        let ms = modes
        guard ms.count > 1, let i = ms.firstIndex(of: mode) else { return }
        go(ms[(i + step + ms.count) % ms.count])
    }

    /// Wedge i spans this many radians around its center angle; the first wedge points straight up.
    func angle(_ i: Int) -> Double { Double(i) / Double(choices.count) * 2 * .pi - .pi / 2 }

    /// What is under the pointer. p is in wheel coordinates, y down, origin top-left.
    func hover(at p: CGPoint) {
        let dx = p.x - Self.size / 2, dy = p.y - Self.size / 2, d = hypot(dx, dy)
        // draggingUpdated fires nonstop; assigning a @Published even to the same value redraws the whole wheel.
        if lemonHot != (d < Self.inner) { lemonHot = d < Self.inner }
        let onSwitch = canSwitch && abs(dx) < WheelView.barWidth(modes.count) / 2 && dy > Self.outer + 4 && dy < Self.outer + 36
        var best: Preset?
        if d >= Self.inner, !onSwitch, !choices.isEmpty {
            let step = 2 * Double.pi / Double(choices.count)
            var t = (atan2(dy, dx) + .pi / 2) / step
            t = t.rounded()
            best = choices[((Int(t) % choices.count) + choices.count) % choices.count]
        }
        if hovered != best { hovered = best }
        if onSwitch != switchHot {
            switchHot = onSwitch
            flipTask?.cancel()
            // Rest the files on the mode bar for a moment to move to the next ring.
            if onSwitch { flipTask = Task { try? await Task.sleep(nanoseconds: 450_000_000); if !Task.isCancelled { switchHot = false; cycle(1) } } }
        }
    }
    func clearHover() { hovered = nil; lemonHot = false; switchHot = false; flipTask?.cancel() }
}

/// The ring segment between two radii with rounded corners: an inset wedge fattened by a round-joined stroke.
struct Wedge: Shape {
    var from: Double, to: Double, inner: CGFloat, outer: CGFloat
    static let radius: CGFloat = 9, gap: CGFloat = 4
    func path(in r: CGRect) -> Path {
        let c = CGPoint(x: r.midX, y: r.midY), n = 24, k = Self.radius
        // Inset by a constant distance, not a constant angle, so neighbours stay parallel toward the center.
        let po = Double((k + Self.gap) / outer), pi = Double((k + Self.gap) / max(inner, 1))
        func pt(_ a: Double, _ rad: CGFloat) -> CGPoint { CGPoint(x: c.x + cos(a) * rad, y: c.y + sin(a) * rad) }
        var p = Path()
        p.move(to: pt(from + po, outer - k))
        for i in 1...n { p.addLine(to: pt(from + po + (to - from - 2 * po) * Double(i) / Double(n), outer - k)) }
        for i in 0...n { p.addLine(to: pt(to - pi - (to - from - 2 * pi) * Double(i) / Double(n), inner + k)) }
        p.closeSubpath()
        return p.union(p.strokedPath(StrokeStyle(lineWidth: k * 2, lineJoin: .round)))
    }
}

extension View {
    /// Liquid Glass on macOS 26+, a frosted material before that.
    @ViewBuilder func glass<S: Shape>(_ shape: S, tint: Color? = nil) -> some View {
        if #available(macOS 26, *) {
            background(shape.fill((tint ?? .clear).opacity(0.75))).glassEffect(.regular.tint(tint), in: shape)
        } else {
            background(shape.fill(.ultraThinMaterial)).background(shape.fill((tint ?? .clear).opacity(0.6)))
                .overlay(shape.stroke(.white.opacity(0.35), lineWidth: 1))
        }
    }
}

/// SF Symbol for a wheel segment.
func wheelSymbol(_ p: Preset) -> String {
    if let e = p.removeBackground { return [BGEngine.vision: "scissors", .lite: "wand.and.stars", .full: "sparkles"][e]! }
    switch p.strength {
    case .lossless: return "checkmark.seal"
    case .balanced: return "scalemass"
    case .smaller: return "arrow.down.right.and.arrow.up.left"
    case nil: break
    }
    switch p.ext {
    case "pdf": return "doc.richtext"
    case "ico": return "app"
    case "gif": return "square.stack"
    case "html": return "globe"
    case "md": return "number"
    case "txt": return "text.alignleft"
    case "epub": return "book"
    case "docx", "odt", "rtf": return "doc.text"
    case "mp4", "webm", "mkv", "mov": return "film"
    case "mp3", "m4a", "opus", "ogg", "flac", "wav", "aiff": return "waveform"
    default: return "photo"
    }
}

/// The glass ring: a circle with the middle cut out.
struct Annulus: Shape {
    var inner: CGFloat, outer: CGFloat
    func path(in r: CGRect) -> Path {
        let c = CGPoint(x: r.midX, y: r.midY)
        func disc(_ rad: CGFloat) -> Path { Path(ellipseIn: CGRect(x: c.x - rad, y: c.y - rad, width: rad * 2, height: rad * 2)) }
        return disc(outer).subtracting(disc(inner))
    }
}

struct WheelView: View {
    @ObservedObject var model: WheelModel
    static func barWidth(_ count: Int) -> CGFloat { CGFloat(count) * 86 + 8 }
    private let spring = Animation.spring(response: 0.22, dampingFraction: 0.75)
    func spot(_ a: Double, _ r: CGFloat) -> CGPoint { CGPoint(x: WheelModel.size / 2 + cos(a) * r, y: WheelModel.size / 2 + sin(a) * r) }

    var body: some View {
        let items = model.choices, n = items.count, half = Double.pi / Double(max(n, 1))
        let inner = WheelModel.inner, outer = WheelModel.outer, mid = (inner + outer) / 2
        let pillY = WheelModel.size / 2 + outer + 20, lemon = inner * 2 - 14
        ZStack {
            // Glass draws above its siblings, so the glow and text live in layers on top of it.
            GlassBox {
                ZStack {
                    Color.clear.glass(Annulus(inner: inner, outer: outer), tint: tangerine.opacity(0.12))
                    Color.clear.frame(width: lemon, height: lemon)
                        .glass(Circle(), tint: model.lemonHot ? tangerine.opacity(0.9) : tangerine.opacity(0.12))
                        .scaleEffect(model.lemonHot ? 1.1 : 1).animation(spring, value: model.lemonHot)
                    if model.canSwitch {
                        Color.clear.frame(width: Self.barWidth(model.modes.count), height: 28)
                            .glass(Capsule(), tint: model.switchHot ? tangerine.opacity(0.35) : tangerine.opacity(0.12))
                            .position(x: WheelModel.size / 2, y: pillY)
                    }
                }
            }
            // Hairline dividers between segments.
            if n > 1 {
                ForEach(0..<n, id: \.self) { i in
                    Path { p in p.move(to: spot(model.angle(i) + half, inner + 10)); p.addLine(to: spot(model.angle(i) + half, outer - 10)) }
                        .stroke(Color.white.opacity(0.22), style: StrokeStyle(lineWidth: 1, lineCap: .round))
                }
            }
            // The hovered segment glows orange inside the ring.
            ForEach(Array(items.enumerated()), id: \.offset) { i, p in
                Wedge(from: model.angle(i) - half, to: model.angle(i) + half, inner: inner + 5, outer: outer - 5)
                    .fill(LinearGradient(colors: [tangerine, tangerineDeep], startPoint: .top, endPoint: .bottom))
                    .shadow(color: tangerine.opacity(0.8), radius: 12)
                    .opacity(model.hovered == p ? 1 : 0)
                    .animation(spring, value: model.hovered)
            }
            ForEach(Array(items.enumerated()), id: \.offset) { i, p in
                let hot = model.hovered == p, (title, detail) = wheelLabel(p)
                VStack(spacing: 2) {
                    Image(systemName: wheelSymbol(p)).font(.system(size: 15, weight: .semibold))
                    Text(title).font(.system(size: 13, weight: .bold, design: .rounded))
                    if let detail { Text(detail).font(.system(size: 9, weight: .medium, design: .rounded)).lineLimit(1).minimumScaleFactor(0.75).opacity(0.85) }
                }
                .frame(width: min(78, CGFloat(half) * mid * 1.7))
                .foregroundStyle(hot ? Color.white : Color.primary)
                .shadow(color: .black.opacity(0.25), radius: 1.5, y: 1)
                .scaleEffect(hot ? 1.08 : 1)
                .position(spot(model.angle(i), mid))
                .animation(spring, value: hot)
            }
            Text("🍋").font(.system(size: 34)).scaleEffect(model.lemonHot ? 1.15 : 1).animation(spring, value: model.lemonHot)
            if model.canSwitch {
                HStack(spacing: 0) {
                    ForEach(model.modes, id: \.self) { m in
                        let on = m == model.mode
                        HStack(spacing: 4) {
                            Text("\(m.rawValue + 1)").font(.system(size: 9, weight: .bold, design: .monospaced))
                                .frame(width: 14, height: 14).background(RoundedRectangle(cornerRadius: 4).strokeBorder(.primary.opacity(on ? 0 : 0.35)))
                            Text(m.title).font(.system(size: 11, weight: on ? .bold : .medium, design: .rounded))
                        }
                        .foregroundStyle(on ? Color.white : Color.primary.opacity(0.8))
                        .frame(width: 82, height: 22)
                        .background(Capsule().fill(on ? AnyShapeStyle(LinearGradient(colors: [tangerine, tangerineDeep], startPoint: .top, endPoint: .bottom)) : AnyShapeStyle(Color.clear)))
                        .padding(.horizontal, 2)
                    }
                }
                .position(x: WheelModel.size / 2, y: pillY)
            }
        }
        .frame(width: WheelModel.size, height: WheelModel.size)
        .scaleEffect(model.shown ? 1 : 0.82).opacity(model.shown ? 1 : 0)
        .animation(spring, value: model.mode)
        .allowsHitTesting(false)   // the hosting view below is the one drop target
    }
}

/// Lets neighbouring glass shapes blend into each other on macOS 26+.
struct GlassBox<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        if #available(macOS 26, *) { GlassEffectContainer(spacing: 0) { content } } else { content }
    }
}

/// One AppKit drop target for the whole wheel: far more reliable than a SwiftUI target per button.
final class WheelHost: NSHostingView<WheelView> {
    var onPick: ((Preset, [URL]) -> Void)?, onQueue: (([URL]) -> Void)?
    private let model: WheelModel
    init(model: WheelModel) {
        self.model = model
        super.init(rootView: WheelView(model: model))
        registerForDraggedTypes([.fileURL])
    }
    @available(*, unavailable) required init(rootView: WheelView) { fatalError() }
    @available(*, unavailable) required init?(coder: NSCoder) { fatalError() }

    private func point(_ s: NSDraggingInfo) -> CGPoint {
        let p = convert(s.draggingLocation, from: nil)
        return CGPoint(x: p.x, y: isFlipped ? p.y : bounds.height - p.y)
    }
    override func draggingEntered(_ s: NSDraggingInfo) -> NSDragOperation { model.hover(at: point(s)); return .copy }
    override func draggingUpdated(_ s: NSDraggingInfo) -> NSDragOperation { model.hover(at: point(s)); return .copy }
    override func draggingExited(_ s: NSDraggingInfo?) { MainActor.assumeIsolated { model.clearHover() } }
    override func performDragOperation(_ s: NSDraggingInfo) -> Bool {
        let urls = s.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        model.hover(at: point(s))
        let target = model.hovered, queue = model.lemonHot
        model.clearHover()
        if let target { onPick?(target, urls); return true }
        if queue { onQueue?(urls); return true }
        return false
    }
}

/// Watches the whole system for a file drag that gets shaken, and shows the wheel under the pointer.
@MainActor final class WheelController {
    static let shared = WheelController()
    let model = WheelModel()
    private var panel: NSPanel?
    private var shake = ShakeDetector()
    private var dragCount = 0, fired = false, down = false

    func start() {
        let pb = NSPasteboard(name: .drag)
        NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged, .leftMouseUp, .scrollWheel]) { [weak self] e in
            MainActor.assumeIsolated { self?.handle(e, pb) }
        }
        NSEvent.addLocalMonitorForEvents(matching: .leftMouseUp) { [weak self] e in
            Task { @MainActor in try? await Task.sleep(nanoseconds: 200_000_000); self?.hide() }; return e
        }
        // Keys go to whatever app the drag started in, so reading (and swallowing) Tab/1/2/3 needs an event tap,
        // which needs Accessibility permission. Ask once; the wheel works without it, just without keys.
        AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary)
        installKeyTap()
    }

    private var tap: CFMachPort?

    private func installKeyTap() {
        guard tap == nil, AXIsProcessTrusted() else { return }
        tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                eventsOfInterest: CGEventMask(1 << CGEventType.keyDown.rawValue),
                                callback: { _, type, event, _ in
            MainActor.assumeIsolated {
                let me = WheelController.shared
                if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                    if let t = me.tap, me.panel?.isVisible == true { CGEvent.tapEnable(tap: t, enable: true) }
                    return Unmanaged.passUnretained(event)
                }
                return me.key(event) ? nil : Unmanaged.passUnretained(event)
            }
        }, userInfo: nil)
        guard let tap else { return }
        CFRunLoopAddSource(CFRunLoopGetMain(), CFMachPortCreateRunLoopSource(nil, tap, 0), .commonModes)
        // Every keystroke on the Mac waits on an enabled tap, so it's only on while the wheel is up.
        CGEvent.tapEnable(tap: tap, enable: panel?.isVisible == true)
    }

    /// Tab / Shift+Tab cycle the rings, 1 2 3 jump to one. Returns true to swallow the key.
    /// Only while the wheel is up mid-drag, so these keys work normally everywhere else.
    private func key(_ e: CGEvent) -> Bool {
        guard panel?.isVisible == true, NSEvent.pressedMouseButtons & 1 != 0 else { return false }
        let repeating = e.getIntegerValueField(.keyboardEventAutorepeat) != 0
        switch e.getIntegerValueField(.keyboardEventKeycode) {
        case 48: if !repeating { model.cycle(e.flags.contains(.maskShift) ? -1 : 1) }   // Tab
        case 18: model.go(.convert)    // 1
        case 19: model.go(.optimize)   // 2
        case 20: model.go(.tools)      // 3
        default: return false
        }
        return true
    }

    private func handle(_ e: NSEvent, _ pb: NSPasteboard) {
        switch e.type {
        case .leftMouseDown: down = true; fired = false; shake.reset(); dragCount = pb.changeCount
        case .leftMouseUp: down = false
            Task { try? await Task.sleep(nanoseconds: 200_000_000); hide() }
        case .scrollWheel:
            // One step per flick: ignore the momentum tail and tiny deltas.
            if panel?.isVisible == true, down, e.momentumPhase == [], abs(e.scrollingDeltaY) > 2 { model.cycle(e.scrollingDeltaY < 0 ? 1 : -1) }
        case .leftMouseDragged:
            // Only a drag that put files on the drag pasteboard counts, and only once the cheap shake check passes.
            // Shake (or hold Shift, like Tangerine) opens the wheel.
            let shaken = shake.add(NSEvent.mouseLocation, e.timestamp) || e.modifierFlags.contains(.shift)
            guard down, !fired, shaken, pb.changeCount != dragCount,
                  let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] else { return }
            fired = true   // once per drag, so an unsupported drag doesn't re-read the pasteboard on every move
            let flat = urls.filter { kindOf[ext(of: $0)] != nil || $0.hasDirectoryPath }
            if !flat.isEmpty { show(flat) }
        default: break
        }
    }

    private func show(_ urls: [URL]) {
        // Folders: look inside so the right formats are offered. This runs mid-drag on the main thread, so only a
        // sample is read; the formats on offer come from the extensions found.
        var files: [URL] = []
        for u in urls {
            guard u.hasDirectoryPath else { files.append(u); continue }
            guard let it = FileManager.default.enumerator(at: u, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) else { continue }
            var scanned = 0
            // ponytail: first 5,000 entries; a rare type deep in a huge folder may be missed (the drop still adds all).
            while scanned < 5_000, let f = it.nextObject() as? URL { scanned += 1; if kindOf[ext(of: f)] != nil { files.append(f) } }
        }
        model.load(files)
        guard !model.choices.isEmpty else { return }
        installKeyTap()   // in case permission was granted since launch
        if panel == nil {
            let p = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
            p.isOpaque = false; p.backgroundColor = .clear; p.hasShadow = false; p.level = .popUpMenu
            p.hidesOnDeactivate = false; p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            let host = WheelHost(model: model)
            host.onPick = { [weak self] in self?.pick($0, $1) }; host.onQueue = { [weak self] in self?.queueIt($0) }
            p.contentView = host
            panel = p
        }
        let m = NSEvent.mouseLocation, s = WheelModel.size
        var o = NSPoint(x: m.x - s / 2, y: m.y - s / 2)
        if let f = (NSScreen.screens.first { NSMouseInRect(m, $0.frame, false) } ?? NSScreen.main)?.visibleFrame {
            o.x = min(max(o.x, f.minX), f.maxX - s); o.y = min(max(o.y, f.minY), f.maxY - s)
        }
        panel?.setFrame(NSRect(origin: o, size: NSSize(width: s, height: s)), display: true)
        panel?.orderFrontRegardless()
        if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
        withAnimation(.spring(response: 0.28, dampingFraction: 0.72)) { model.shown = true }
    }

    private func hide() {
        panel?.orderOut(nil)
        if let tap { CGEvent.tapEnable(tap: tap, enable: false) }
    }

    private func pick(_ p: Preset, _ urls: [URL]) {
        hide(); sharedQueue.addAndRun(urls, p)
    }

    private func queueIt(_ urls: [URL]) {
        hide(); sharedQueue.add(urls); AppHooks.openWindow?(); NSApp.activate(ignoringOtherApps: true)
    }
}

enum AppHooks { @MainActor static var openWindow: (() -> Void)? }
