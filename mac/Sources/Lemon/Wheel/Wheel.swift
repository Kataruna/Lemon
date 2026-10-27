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

/// The wheel key over a selection once the wheel is up: a tap enters what's lit, a hold closes the wheel. Fed by a poll
/// of the key. The hold that opened the wheel is neither; nor is a press with another key in it (Control+W).
struct KeyTap {
    static let hold = 0.7
    enum Action { case enter, close }
    private var armed = false, downAt: Double?, spoiled = false
    mutating func update(held: Bool, now: Double) -> Action? {
        guard held else {
            defer { downAt = nil; spoiled = false; armed = true }
            return downAt != nil && !spoiled ? .enter : nil
        }
        guard armed else { return nil }
        if downAt == nil { downAt = now }
        if !spoiled, now - downAt! >= Self.hold { spoiled = true; return .close }
        return nil
    }
    /// Another key went down with the wheel key held.
    mutating func otherKey() { spoiled = true }
}

// MARK: which presets go on the wheel (port of ConversionWheel.cs)

/// The Convert ring until the user picks their own in Settings.
let favorites: [Kind: [String]] = [
    .image: ["PNG", "JPEG – High quality", "WebP – Web optimized", "PDF", "AVIF – Small", "Icon (256 px)", "GIF", "TIFF", "BMP"],
    .audio: ["MP3 – 320 kbps", "AAC (M4A) – 256 kbps", "FLAC – Lossless", "WAV – Uncompressed", "Opus – 128 kbps", "Ogg Vorbis – 192 kbps", "MP3 – 192 kbps", "AIFF – Uncompressed"],
    .video: ["MP4 (H.264)", "MP4 – 720p small", "WebM (VP9)", "Animated GIF", "MP3 – 320 kbps", "QuickTime MOV", "MKV (H.264)", "AAC (M4A) – 256 kbps"],
    .document: ["PDF", "Word (DOCX)", "HTML page", "Plain text", "Markdown", "OpenDocument (ODT)", "Rich Text (RTF)", "EPUB e-book"],
]

/// One of the wheel's rings: built in, or made by the user in Settings. Tab cycles them, 1–9 jump straight to one.
struct WheelMode: Hashable {
    let id: String   // "convert", "optimize", "tools", "commands", or a custom ring's id
    static let convert = WheelMode(id: "convert"), optimize = WheelMode(id: "optimize"), tools = WheelMode(id: "tools"), resize = WheelMode(id: "resize")
    static let builtIns = [convert, optimize, tools, resize]
    /// The user's commands for dragged folders and links. Not one of the built-ins Settings can hide: no commands, no ring.
    static let commands = WheelMode(id: "commands")
    /// Built-in rings first, then the user's, in Settings order, then Commands.
    static var all: [WheelMode] { builtIns + (PrefStore.shared.p.rings ?? []).map { WheelMode(id: $0.id) } + [commands] }
    var custom: CustomRing? { PrefStore.shared.p.rings?.first { $0.id == id } }
    var title: String { custom?.name ?? ["convert": "Convert", "optimize": "Optimize", "tools": "Cut out", "resize": "Resize", "commands": "Commands"][id] ?? "" }
    var symbol: String { custom?.symbol ?? ["convert": "arrow.triangle.2.circlepath", "optimize": "arrow.down.right.and.arrow.up.left", "tools": "scissors", "resize": "arrow.up.left.and.arrow.down.right", "commands": "terminal"][id] ?? "star" }
    var detail: String { custom != nil ? "Your ring" : ["convert": "Change format", "optimize": "Make smaller", "tools": "Remove background", "resize": "Shrink or upscale images", "commands": "Your commands"][id] ?? "" }
    func holds(_ p: Preset) -> Bool {
        if let c = custom { return c.presets.contains(p.name) }
        switch self {
        case .convert: return p.strength == nil && p.removeBackground == nil && !p.isResize
        case .resize: return p.isResize
        case .optimize: return p.strength != nil
        case .commands: return false   // filled from Prefs.commands in WheelModel.load
        default: return p.removeBackground != nil
        }
    }
}

/// The kind most of these files are: by file count, not extension count.
func mainKind(_ urls: [URL]) -> Kind? {
    var n: [Kind: Int] = [:]
    for u in urls { if let k = kindOf[ext(of: u)] { n[k, default: 0] += 1 } }
    return n.max { $0.value < $1.value }?.key
}

/// The presets for each extension among the dragged files. Presets depend only on the extension, so a folder of
/// 5,000 photos is one lookup, not 5,000; and every ring reads this one table.
func presetTable(_ urls: [URL]) -> [(kind: Kind, presets: [Preset])] {
    Set(urls.map { ext(of: $0) }).sorted().compactMap { e in kindOf[e].map { ($0, presets(for: URL(fileURLWithPath: "x." + e))) } }
}

/// Up to 8 presets that work for every dragged file; a mixed drag falls back to what fits the main kind.
func wheelChoices(_ table: [(kind: Kind, presets: [Preset])], main: Kind, _ mode: WheelMode) -> [Preset] {
    let optimize = mode == .optimize
    let per = table.map { ($0.kind, $0.presets.filter(mode.holds)) }
    guard !per.isEmpty else { return [] }
    let mine = PrefStore.shared.p.slots[main], favs = mine ?? favorites[main]!
    func rank(_ p: Preset) -> Int {
        if let c = mode.custom { return c.presets.firstIndex(of: p.name) ?? 99 }
        // Upscale (4× Best, 4×, 2× Best, 2×), then the user's sizes, then the built-in ones from largest to smallest.
        if mode == .resize { return upscalePresets.firstIndex(of: p) ?? resizePresets.firstIndex(of: p).map { 20 + $0 } ?? 10 }
        return mode == .tools ? [BGEngine.vision, .lite, .full].firstIndex(of: p.removeBackground!)! : optimize ? [Strength.lossless, .balanced, .smaller].firstIndex(of: p.strength!)! : favs.firstIndex(of: p.name) ?? 99
    }
    func pick(_ lists: [[Preset]]) -> [Preset] {
        lists[0].filter { p in lists.dropFirst().allSatisfy { $0.contains(p) } }.sorted { rank($0) < rank($1) }
    }
    var out = pick(per.map(\.1))
    for p in pick(per.filter { $0.0 == main }.map(\.1)) where !out.contains(p) { out.append(p) }
    // Picked by hand: only those, not the built-in fill-ins.
    if mode == .convert, mine != nil { out = out.filter { favs.contains($0.name) } }
    return Array(out.prefix(8))
}

/// "MP4" over "720p small": the format over what makes the preset different.
func wheelLabel(_ p: Preset) -> (String, String?) {
    if p.command != nil { return (p.name, nil) }
    if let u = p.upscale { return ("\(p.scale ?? 4)×", u == .lite ? "AI upscale" : "Best quality") }
    if p.isResize, let side = p.maxWidth ?? p.maxHeight { return (String(p.name.split(separator: "–").last ?? "").trimmingCharacters(in: .whitespaces), "\(side) px") }
    if let e = p.removeBackground { return [BGEngine.vision: ("Quick", "Apple Vision"), .lite: ("Lite", "BiRefNet"), .full: ("Full", "Best quality")][e]! }
    if let s = p.strength { return (s.rawValue, [Strength.lossless: "Same pixels", .balanced: "Looks the same", .smaller: "Slightly lower quality"][s]) }
    let title = p.ext.uppercased()
    if let r = p.name.range(of: " – ") { return (title, String(p.name[r.upperBound...])) }
    if let a = p.name.firstIndex(of: "("), let b = p.name.firstIndex(of: ")"), a < b, !p.name.hasPrefix(title) { return (title, String(p.name[p.name.index(after: a)..<b])) }
    return (title, nil)
}

// MARK: the wheel

@MainActor final class WheelModel: ObservableObject {
    static let size: CGFloat = 380, inner: CGFloat = 60, outer: CGFloat = 152
    /// Drops count a little past the ring's edge, as far as WheelView's catch layer goes.
    static let reach = outer + 10
    @Published var mode = WheelMode.convert
    @Published var hovered: Preset?
    @Published var lemonHot = false
    @Published var switchHot = false
    @Published var shown = false
    /// On the hub: the wedges are the rings themselves, and resting on one opens it.
    @Published var inHub = false
    @Published var hoveredMode: WheelMode?
    private var byMode: [WheelMode: [Preset]] = [:]
    var choices: [Preset] { byMode[mode] ?? [] }
    /// Rings that are switched on and have something on them for these files. Worked out once per drag in load():
    /// hover() reads it on every mouse move.
    private(set) var modes: [WheelMode] = []
    var canSwitch: Bool { modes.count > 1 }
    /// What these files open on: "hub" or a ring id, set per kind in Settings.
    private var start: String?
    /// The hub is there when files open on it, or when there are too many rings for the bar (4 fit).
    var hubOn: Bool { canSwitch && (start == "hub" || modes.count > 4) }
    /// Inside a ring with the hub on, the center is the way back to it (unless Settings asks for the old pill).
    var centerBack: Bool { hubOn && !inHub && !(PrefStore.shared.p.backPill ?? false) }
    /// Pills on the mode bar: one per ring, or with the hub on, at most a single one back to it (none on the hub itself).
    var pills: Int { !canSwitch || inHub ? 0 : hubOn ? (centerBack ? 0 : 1) : modes.count }
    /// How many wedges the wheel has right now.
    var count: Int { inHub ? modes.count : choices.count }
    private var flipTask: Task<Void, Never>?
    private enum Stop: Equatable { case hub, ring(WheelMode) }
    private var switchTarget: Stop?
    /// The wheel opens centered on the pointer, so the center only leads back to the hub once the pointer has left it
    /// since this ring came up; otherwise a pause right after the shake would send the files to the hub on its own.
    private var centerArmed = false
    /// The dragged files (not what's inside folders), folders and links, for a drop on the Commands ring.
    private(set) var files: [URL] = [], folders: [URL] = [], links: [URL] = []

    /// The dragged items a command runs on.
    func targets(_ c: WheelCommand) -> [URL] {
        switch c.on { case .files: files.filter(c.takes); case .folders: folders; case .links: links }
    }

    func load(_ urls: [URL], files: [URL] = [], folders: [URL] = [], links: [URL] = []) {
        shown = false; hovered = nil; hoveredMode = nil; lemonHot = false; switchHot = false; switchTarget = nil; centerArmed = false
        self.files = files; self.folders = folders; self.links = links
        let main = mainKind(urls), table = presetTable(urls), hidden = PrefStore.shared.p.hidden ?? []
        byMode = main.map { main in Dictionary(uniqueKeysWithValues: WheelMode.all.map { ($0, wheelChoices(table, main: main, $0)) }) } ?? [:]
        byMode[.commands] = (PrefStore.shared.p.commands ?? []).filter { !targets($0).isEmpty }
            .prefix(8).map { Preset(name: $0.name, ext: "cmd", kind: .document, command: $0.id) }
        modes = WheelMode.all.filter { !hidden.contains($0.id) && !(byMode[$0] ?? []).isEmpty }
        start = PrefStore.shared.p.hub == true ? "hub" : main.flatMap { PrefStore.shared.p.start?[$0] }
        // The chosen ring if these files have anything on it, else the hub (when there is one) or the first ring.
        let ring = modes.first { $0.id == start }
        mode = ring ?? modes.first ?? .convert
        // No ring chosen means the first one, as Settings shows; the hub only when asked for, or the chosen ring is empty.
        inHub = hubOn && ring == nil && start != nil
    }
    /// Just these presets on one ring, as the welcome guide's demo shows it.
    func load(demo presets: [Preset]) {
        byMode = [.convert: presets]; modes = [.convert]; mode = .convert; start = nil
        inHub = false; hovered = nil; hoveredMode = nil; lemonHot = false; switchHot = false; shown = true
    }
    /// Number keys: the nth ring, or 0 for the hub.
    func go(number n: Int) {
        if n == 0 { if hubOn, !inHub { inHub = true; hovered = nil } }
        else if n <= modes.count { go(modes[n - 1]) }
    }
    func go(_ m: WheelMode) {
        guard modes.contains(m), inHub || m != mode else { return }
        mode = m; inHub = false; hovered = nil; hoveredMode = nil; centerArmed = false
    }
    func cycle(_ step: Int) {
        let ms = modes
        guard ms.count > 1, let i = ms.firstIndex(of: mode) else { return }
        go(inHub ? (step > 0 ? ms[0] : ms[ms.count - 1]) : ms[(i + step + ms.count) % ms.count])
    }

    /// Wedge i spans this many radians around its center angle; the first wedge points straight up.
    func angle(_ i: Int) -> Double { Double(i) / Double(count) * 2 * .pi - .pi / 2 }

    /// What is under the pointer. p is in wheel coordinates, y down, origin top-left.
    func hover(at p: CGPoint) {
        let dx = p.x - Self.size / 2, dy = p.y - Self.size / 2, d = hypot(dx, dy)
        // draggingUpdated fires nonstop; assigning a @Published even to the same value redraws the whole wheel.
        if lemonHot != (d < Self.inner) { lemonHot = d < Self.inner }
        if !lemonHot { centerArmed = true }
        let n = pills, ms = modes
        // The bar's strip never counts as a wedge, even on the hub where no pill is drawn: otherwise resting there opens the
        // bottom ring, whose "All modes" pill then sends it back to the hub, and round it goes.
        let onBar = canSwitch && abs(dx) < WheelView.barWidth(max(n, 1)) / 2 && dy > Self.outer + 4 && dy < Self.outer + 36
        let onSwitch = n > 0 && onBar
        var slot: Int?
        if d >= Self.inner, d <= Self.reach, !onBar, count > 0 {
            let t = ((atan2(dy, dx) + .pi / 2) / (2 * Double.pi / Double(count))).rounded()
            slot = ((Int(t) % count) + count) % count
        }
        let best = inHub ? nil : slot.map { choices[$0] }, bestMode = inHub ? slot.map { ms[$0] } : nil
        if hovered != best { hovered = best }
        if hoveredMode != bestMode { hoveredMode = bestMode }
        if onSwitch != switchHot { switchHot = onSwitch }
        // Where resting here leads: a hub wedge, or the pill under the pointer (86 pt per pill, centered).
        var target: Stop? = bestMode.map { .ring($0) }
        if onSwitch { target = hubOn ? .hub : .ring(ms[min(max(Int((dx + CGFloat(n) * 43) / 86), 0), n - 1)]) }
        if centerBack, lemonHot, centerArmed { target = .hub }
        if target != switchTarget {
            switchTarget = target
            flipTask?.cancel()
            // Rest the files for a moment to switch; passing over others on the way doesn't count.
            if let target, target != (inHub ? .hub : .ring(mode)) {
                flipTask = Task {
                    try? await Task.sleep(nanoseconds: inHub ? 250_000_000 : 450_000_000)
                    guard !Task.isCancelled else { return }
                    switch target {
                    case .hub: inHub = true; hovered = nil
                    case .ring(let m): go(m)
                    }
                    switchTarget = nil
                }
            }
        }
    }
    /// Arrow keys: the wedge after (1, clockwise) or before (-1) the lit one, starting from the top.
    func step(_ d: Int) {
        flipTask?.cancel(); switchTarget = nil; lemonHot = false
        func next<T: Equatable>(_ all: [T], _ lit: T?) -> T? {
            guard !all.isEmpty else { return nil }
            guard let i = lit.flatMap(all.firstIndex) else { return all[d > 0 ? 0 : all.count - 1] }
            return all[(i + d + all.count) % all.count]
        }
        if inHub { hoveredMode = next(modes, hoveredMode) } else { hovered = next(choices, hovered) }
    }
    func clearHover() { hovered = nil; hoveredMode = nil; lemonHot = false; switchHot = false; switchTarget = nil; flipTask?.cancel() }
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
    @ViewBuilder func glass<S: Shape>(_ shape: S, tint: Color? = nil, clear: Bool = false) -> some View {
        if #available(macOS 26, *) {
            background(shape.fill((tint ?? .clear).opacity(0.75))).glassEffect((clear ? Glass.clear : .regular).tint(tint), in: shape)
        } else {
            background(shape.fill(.ultraThinMaterial).opacity(clear ? 0.5 : 1)).background(shape.fill((tint ?? .clear).opacity(0.6)))
                .overlay(shape.stroke(.white.opacity(0.35), lineWidth: 1))
        }
    }

    /// The wheel's glass at a frost level. Liquid Glass is clear for the lighter two and regular for the heavier two;
    /// frost under the full shape would leave the glass nothing to bend, so it fades out short of the edges (a hard
    /// edge reads as a stroke inside the ring). With Liquid Glass off the same glass is drawn flat: see WheelPanel.
    @ViewBuilder func wheelGlass<S: InsettableShape>(_ shape: S, tint: Color, blur: Int) -> some View {
        // A plain color, not a material: a material re-samples what's behind the window and flickers where it fades out.
        // The glass underneath already blurs.
        let fade = shape.inset(by: 8).fill().blur(radius: 8)
        let frost = Color(nsColor: .windowBackgroundColor)
        switch blur {
        case 0: glass(shape, tint: tint, clear: true)
        case 1: background(Rectangle().fill(frost.opacity(0.3)).mask(fade)).glass(shape, tint: tint, clear: true)
        case 3: background(Rectangle().fill(frost.opacity(0.6)).mask(fade)).glass(shape, tint: tint)
        default: glass(shape, tint: tint)
        }
    }
}

/// SF Symbol for a wheel segment.
func wheelSymbol(_ p: Preset) -> String {
    if let id = p.command { return PrefStore.shared.p.commands?.first { $0.id == id }?.symbol ?? "terminal" }
    if let u = p.upscale { return u == .lite ? "arrow.up.left.and.arrow.down.right" : "sparkles" }
    if p.isResize { return "arrow.down.right.and.arrow.up.left" }
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
struct Annulus: InsettableShape {
    var inner: CGFloat, outer: CGFloat
    func path(in r: CGRect) -> Path {
        let c = CGPoint(x: r.midX, y: r.midY)
        func disc(_ rad: CGFloat) -> Path { Path(ellipseIn: CGRect(x: c.x - rad, y: c.y - rad, width: rad * 2, height: rad * 2)) }
        return disc(outer).subtracting(disc(inner))
    }
    func inset(by a: CGFloat) -> Annulus { Annulus(inner: inner + a, outer: outer - a) }
}

/// The wheel's glass on its own. Hovering a wedge redraws WheelView, and Liquid Glass rebuilt on every hover change
/// stutters under a moving drag; being Equatable, this redraws only when what it shows changes.
struct WheelGlass: View, Equatable {
    let accent: Color, tint: Double, style: GlassStyle, lemonHot: Bool, switchHot: Bool, pills: Int
    var body: some View {
        let inner = WheelModel.inner, outer = WheelModel.outer, lemon = inner * 2 - 14
        let spring = Animation.spring(response: 0.22, dampingFraction: 0.75)
        GlassBox {
            ZStack {
                Color.clear.wheelGlass(Annulus(inner: inner, outer: outer), tint: accent.opacity(tint), blur: style.blur)
                Color.clear.frame(width: lemon, height: lemon)
                    .wheelGlass(Circle(), tint: lemonHot ? accent.opacity(0.9) : accent.opacity(tint), blur: style.blur)
                    .scaleEffect(lemonHot ? 1.1 : 1).animation(spring, value: lemonHot)
                if pills > 0 {
                    Color.clear.frame(width: WheelView.barWidth(pills), height: 28)
                        .wheelGlass(Capsule(), tint: switchHot ? accent.opacity(tint + 0.23) : accent.opacity(tint), blur: style.blur)
                        .position(x: WheelModel.size / 2, y: WheelModel.size / 2 + outer + 20)
                }
            }
        }
        .opacity(style.opacity)
    }
}

struct WheelView: View {
    @ObservedObject var model: WheelModel
    @ObservedObject private var store = PrefStore.shared
    static func barWidth(_ count: Int) -> CGFloat { CGFloat(count) * 86 + 8 }
    private let spring = Animation.spring(response: 0.22, dampingFraction: 0.75)
    func spot(_ a: Double, _ r: CGFloat) -> CGPoint { CGPoint(x: WheelModel.size / 2 + cos(a) * r, y: WheelModel.size / 2 + sin(a) * r) }

    var body: some View {
        // On the hub the wedges are the rings; otherwise the presets.
        let slots: [(symbol: String, title: String, detail: String?, hot: Bool)] = model.inHub
            ? model.modes.map { ($0.symbol, $0.title, $0.detail, model.hoveredMode == $0) }
            : model.choices.map { p in let (title, detail) = wheelLabel(p); return (wheelSymbol(p), title, detail, model.hovered == p) }
        let n = slots.count, half = Double.pi / Double(max(n, 1))
        let inner = WheelModel.inner, outer = WheelModel.outer, mid = (inner + outer) / 2
        let pillY = WheelModel.size / 2 + outer + 20
        let t = store.p.theme, accent = t.accentColor, g = store.p.glass ?? GlassStyle()
        ZStack {
            // Nearly clear, not clear: the window server hands drags over fully transparent pixels to the window behind,
            // so without this the gap between the lemon and the ring (and the ring's soft edge) drop onto the desktop.
            Circle().fill(Color.black.opacity(0.01)).frame(width: WheelModel.reach * 2, height: WheelModel.reach * 2)
            // Glass draws above its siblings, so the glow and text live in layers on top of it.
            WheelGlass(accent: accent, tint: t.tint, style: g, lemonHot: model.lemonHot, switchHot: model.switchHot, pills: model.pills)
                .equatable()
            // Hairline dividers between segments.
            if n > 1 {
                ForEach(0..<n, id: \.self) { i in
                    Path { p in p.move(to: spot(model.angle(i) + half, inner + 10)); p.addLine(to: spot(model.angle(i) + half, outer - 10)) }
                        .stroke(Color.white.opacity(0.22), style: StrokeStyle(lineWidth: 1, lineCap: .round))
                }
            }
            // The hovered segment glows orange inside the ring.
            ForEach(Array(slots.enumerated()), id: \.offset) { i, s in
                Wedge(from: model.angle(i) - half, to: model.angle(i) + half, inner: inner + 5, outer: outer - 5)
                    .fill(t.gradient)
                    .shadow(color: accent.opacity(0.8), radius: 12)
                    .opacity(s.hot ? 1 : 0)
                    .animation(spring, value: s.hot)
            }
            ForEach(Array(slots.enumerated()), id: \.offset) { i, s in
                let hot = s.hot
                VStack(spacing: 2) {
                    Image(systemName: s.symbol).font(.system(size: 15, weight: .semibold))
                    Text(s.title).font(.system(size: 13, weight: .bold, design: .rounded))
                    if let detail = s.detail { Text(detail).font(.system(size: 9, weight: .medium, design: .rounded)).lineLimit(1).minimumScaleFactor(0.75).opacity(0.85) }
                }
                .frame(width: min(78, CGFloat(half) * mid * 1.7))
                .foregroundStyle(hot ? Color.white : t.text)
                .shadow(color: .black.opacity(0.25), radius: 1.5, y: 1)
                .scaleEffect(hot ? 1.08 : 1)
                .position(spot(model.angle(i), mid))
                .animation(spring, value: hot)
            }
            Group {
                if model.centerBack { Image(systemName: "chevron.left").font(.system(size: 26, weight: .bold)).foregroundStyle(model.lemonHot ? Color.white : t.text) }
                else { Text(t.center.isEmpty ? "🍋" : t.center).font(.system(size: 34)) }
            }.scaleEffect(model.lemonHot ? 1.15 : 1).animation(spring, value: model.lemonHot)
            if model.hubOn, model.pills > 0 {
                Label("All modes", systemImage: "chevron.left").font(.system(size: 11, weight: .bold, design: .rounded))
                    .foregroundStyle(t.text.opacity(0.8)).frame(width: 82, height: 22)
                    .position(x: WheelModel.size / 2, y: pillY)
            } else if model.pills > 0 {
                HStack(spacing: 0) {
                    ForEach(Array(model.modes.enumerated()), id: \.element) { i, m in
                        let on = m == model.mode
                        HStack(spacing: 4) {
                            Text("\(i + 1)").font(.system(size: 9, weight: .bold, design: .monospaced))
                                .frame(width: 14, height: 14).background(RoundedRectangle(cornerRadius: 4).strokeBorder(.primary.opacity(on ? 0 : 0.35)))
                            Text(m.title).font(.system(size: 11, weight: on ? .bold : .medium, design: .rounded)).lineLimit(1)
                        }
                        .foregroundStyle(on ? Color.white : t.text.opacity(0.8))
                        .frame(width: 82, height: 22)
                        .background(Capsule().fill(on ? AnyShapeStyle(t.gradient) : AnyShapeStyle(Color.clear)))
                        .padding(.horizontal, 2)
                    }
                }
                .position(x: WheelModel.size / 2, y: pillY)
            }
        }
        .frame(width: WheelModel.size, height: WheelModel.size)
        .scaleEffect(model.shown ? 1 : 0.82).opacity(model.shown ? 1 : 0)
        .animation(spring, value: model.mode)
        .animation(spring, value: model.inHub)
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
        registerForDraggedTypes([.fileURL, .URL])
    }
    @available(*, unavailable) required init(rootView: WheelView) { fatalError() }
    @available(*, unavailable) required init?(coder: NSCoder) { fatalError() }

    private func point(_ s: NSDraggingInfo) -> CGPoint {
        let p = convert(s.draggingLocation, from: nil)
        return CGPoint(x: p.x, y: isFlipped ? p.y : bounds.height - p.y)
    }
    override func draggingEntered(_ s: NSDraggingInfo) -> NSDragOperation { operation(s) }
    override func draggingUpdated(_ s: NSDraggingInfo) -> NSDragOperation { operation(s) }
    /// "+" only where a drop does something (a tool or the lemon), so the mode bar, hub wedges and back button don't look droppable.
    /// Takes whatever the source allows: insisting on .copy makes macOS refuse drops from apps that only offer move.
    private func operation(_ s: NSDraggingInfo) -> NSDragOperation {
        model.hover(at: point(s))
        // A link has nothing to queue, so the lemon takes no links.
        guard model.hovered != nil || (model.lemonHot && !model.centerBack && model.links.isEmpty) else { return [] }
        let m = s.draggingSourceOperationMask
        return [.copy, .generic, .move, .link].first { m.contains($0) } ?? .copy
    }
    override func draggingExited(_ s: NSDraggingInfo?) { MainActor.assumeIsolated { model.clearHover() } }
    override func performDragOperation(_ s: NSDraggingInfo) -> Bool {
        let urls = s.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        model.hover(at: point(s))
        let target = model.hovered, queue = model.lemonHot && !model.centerBack
        model.clearHover()
        if let target { onPick?(target, urls); return true }
        if queue, !urls.isEmpty { onQueue?(urls); return true }   // a link has nothing to queue
        return false
    }
}

/// Glass in a window that isn't key is drawn flat, without Liquid Glass's lensing and rims: that's the Liquid Glass
/// setting. Nonactivating, so it can be key while Lemon stays in the background and the drag's app stays frontmost.
private final class WheelPanel: NSPanel { override var canBecomeKey: Bool { PrefStore.shared.p.glass?.liquid ?? true } }

/// Watches the whole system for a file drag that gets shaken, and shows the wheel under the pointer.
@MainActor final class WheelController {
    static let shared = WheelController()
    let model = WheelModel()
    private var panel: NSPanel?
    private var shake = ShakeDetector()
    private var dragCount = 0, fired = false, down = false
    /// Esc went down on the wheel and was swallowed: its key-up is swallowed too.
    private var swallowingEscape = false
    private let pb = NSPasteboard(name: .drag)
    /// The wheel key (Control or Shift) held alone over Finder: the wait before its selection opens the wheel.
    private var hold: Task<Void, Never>?
    /// The Finder selection the wheel is up for (nil in a drag), the timer that follows the pointer and the key for it,
    /// and whether the pointer has left the lemon (letting go there only queues the files after that).
    private var selection: [URL]?
    private var follow: Timer?
    private var movedOff = false
    /// Where the pointer was at the last look: it only takes over from the arrow keys once it moves.
    private var lastMouse: NSPoint?
    /// The wheel stays up over a selection once the key is let go: a tap of it enters what's lit, a hold closes it.
    private var keyTap = KeyTap()

    func start() {
        NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged, .leftMouseUp, .scrollWheel]) { [weak self] e in
            MainActor.assumeIsolated { self?.handle(e) }
        }
        // Only modifier changes: a monitor of every key would wake Lemon for each keystroke in every app.
        NSEvent.addGlobalMonitorForEvents(matching: .flagsChanged) { [weak self] e in
            MainActor.assumeIsolated { self?.holdEvent(e) }
        }
        NSEvent.addLocalMonitorForEvents(matching: .leftMouseUp) { [weak self] e in
            // Not over a selection: that wheel stays up between clicks (entering a ring is a click).
            Task { @MainActor in try? await Task.sleep(nanoseconds: 200_000_000); if self?.down == false, self?.selection == nil { self?.hide() } }; return e
        }
        // A click on the wheel picks, as letting go of the key does.
        NSEvent.addLocalMonitorForEvents(matching: .leftMouseDown) { [weak self] e in
            let picked = MainActor.assumeIsolated { () -> Bool in
                guard let self, self.selection != nil, self.panel?.isVisible == true else { return false }
                self.track(force: true); self.activate(); return true
            }
            return picked ? nil : e
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
                                eventsOfInterest: CGEventMask(1 << CGEventType.keyDown.rawValue | 1 << CGEventType.keyUp.rawValue),
                                callback: { _, type, event, _ in
            MainActor.assumeIsolated {
                let me = WheelController.shared
                if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                    me.syncTap()
                    return Unmanaged.passUnretained(event)
                }
                return me.key(event, up: type == .keyUp) ? nil : Unmanaged.passUnretained(event)
            }
        }, userInfo: nil)
        guard let tap else { return }
        CFRunLoopAddSource(CFRunLoopGetMain(), CFMachPortCreateRunLoopSource(nil, tap, 0), .commonModes)
        syncTap()
    }

    /// Every keystroke on the Mac waits on an enabled tap, so it's only on while the wheel is up, or mid-press when a
    /// key other than Control or Shift opens the wheel.
    private func syncTap() {
        let p = PrefStore.shared.p, keyOpens = down && p.onKey && !p.key.held
        if keyOpens { installKeyTap() }   // in case permission was granted since launch
        if let tap { CGEvent.tapEnable(tap: tap, enable: panel?.isVisible == true || keyOpens || swallowingEscape) }
    }

    /// Tab / Shift+Tab cycle the rings, 1–9 jump to one, 0 to the hub, Esc closes the wheel but keeps the drag going.
    /// Without the wheel up, the wheel's key opens it. Returns true to swallow the key. Only mid-drag, so these keys
    /// work normally everywhere else.
    private func key(_ e: CGEvent, up: Bool) -> Bool {
        let code = e.getIntegerValueField(.keyboardEventKeycode)
        if up {
            guard code == 53, swallowingEscape else { return false }
            swallowingEscape = false; syncTap(); return true
        }
        guard NSEvent.pressedMouseButtons & 1 != 0 || selection != nil else { return false }
        // ⌘-Tab, ⌘W and the like belong to the system and the apps.
        guard !e.flags.contains(.maskCommand) else { return false }
        if let f = PrefStore.shared.p.key.flag, e.flags.contains(f == .control ? .maskControl : .maskShift) { keyTap.otherKey() }
        let repeating = e.getIntegerValueField(.keyboardEventAutorepeat) != 0
        guard panel?.isVisible == true else {
            let p = PrefStore.shared.p
            // Only a drag that carries something: otherwise the key is just typed, say while selecting text.
            guard p.onKey, code == p.key.code, pb.changeCount != dragCount else { return false }
            if !repeating { open() }
            return true
        }
        switch code {
        case 53:
            // Esc: the drag sees none of it, so it carries on and can be dropped anywhere, or the wheel opened again.
            // Over a selection, inside a ring it goes back to the hub; on the hub (or with none) it closes the wheel.
            swallowingEscape = true
            if repeating { break }
            if selection != nil, model.hubOn, !model.inHub { model.go(number: 0) } else { fired = false; shake.reset(); hide() }
        case 48: if !repeating { model.cycle(e.flags.contains(.maskShift) ? -1 : 1) }   // Tab
        // Over a selection, without the mouse: A W and D S light the slice (or the hub's ring) before or after; a tap of
        // the key or Return enters it. Not mid-drag: a drop goes where the pointer is.
        // WASD because Control + arrows switch Spaces before Lemon sees them; the arrows still work with Shift.
        case let k where selection != nil && [0, 13, 1, 2, 123, 126, 124, 125].contains(k): model.step([1, 2, 124, 125].contains(k) ? 1 : -1)
        case let k where selection != nil && (k == 36 || k == 76): if !repeating { activate() }
        case let k:
            // Key codes for 0...9 on the number row.
            guard let n = [29, 18, 19, 20, 21, 23, 22, 26, 28, 25].firstIndex(of: k) else { return false }
            model.go(number: n)
        }
        return true
    }

    private func handle(_ e: NSEvent) {
        switch e.type {
        case .leftMouseDown:
            cancelHold()
            if selection != nil { hide() }
            down = true; fired = false; swallowingEscape = false; shake.reset(); dragCount = pb.changeCount; syncTap()
        case .leftMouseUp: down = false; syncTap()
            // Not if a new drag has started meanwhile: that would close its wheel.
            Task { try? await Task.sleep(nanoseconds: 200_000_000); if !down { hide() } }
        case .scrollWheel:
            // One step per flick: ignore the momentum tail and tiny deltas.
            cancelHold()
            if panel?.isVisible == true, down || selection != nil, e.momentumPhase == [], abs(e.scrollingDeltaY) > 2 { model.cycle(e.scrollingDeltaY < 0 ? 1 : -1) }
        case .leftMouseDragged:
            // Shake, or hold Control / Shift, as Settings allows. A key that's pressed arrives through the key tap.
            let p = PrefStore.shared.p
            let shaken = (p.onShake && shake.add(NSEvent.mouseLocation, e.timestamp)) || (p.onKey && p.key.flag.map(e.modifierFlags.contains) == true)
            if shaken { open() }
        default: break
        }
    }

    /// Only a drag that put files or a web link on the drag pasteboard counts.
    private func open() {
        guard down, !fired, pb.changeCount != dragCount else { return }
        fired = true   // once per drag, so an unsupported drag (text, or no files) doesn't re-read the pasteboard on every move
        // A link counts as a link even when the browser adds a file for it, but only with a command for links: otherwise
        // a drag carrying both still converts its files.
        if (PrefStore.shared.p.commands ?? []).contains { $0.on == .links } {
            let links = (pb.readObjects(forClasses: [NSURL.self]) as? [URL] ?? []).filter { ["http", "https"].contains($0.scheme?.lowercased()) }
            if !links.isEmpty { show([], links: links); return }
        }
        guard let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] else { return }
        let flat = wheelItems(urls)
        if !flat.isEmpty { show(flat) }
    }

    /// What the wheel can do something with: convertible files, folders, and any file a command for files takes.
    private func wheelItems(_ urls: [URL]) -> [URL] {
        let fileCommands = (PrefStore.shared.p.commands ?? []).filter { $0.on == .files }
        return urls.filter { u in kindOf[ext(of: u)] != nil || u.hasDirectoryPath || fileCommands.contains { $0.takes(u) } }
    }

    // MARK: the wheel key held over files selected in Finder

    /// The key (Control or Shift) alone, with Finder in front and no button down, starts the hold; anything else cancels it.
    private func holdEvent(_ e: NSEvent) {
        let p = PrefStore.shared.p
        guard let flag = p.key.flag, e.type == .flagsChanged, e.modifierFlags.intersection([.shift, .control, .option, .command, .function]) == flag,
              p.onKey, !down, selection == nil, NSEvent.pressedMouseButtons == 0,
              NSWorkspace.shared.frontmostApplication?.bundleIdentifier == "com.apple.finder" else { cancelHold(); return }
        hold?.cancel()
        let start = Date()
        hold = Task {
            try? await Task.sleep(nanoseconds: 700_000_000)
            // A key, a click or a scroll meanwhile means the key was a modifier (Control-click, Shift+A), and typing a
            // capital in a rename or the search field isn't a hold either. Asked of the system, so no monitor is needed.
            let since = Date().timeIntervalSince(start)
            let busy = [CGEventType.keyDown, .leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel]
                .contains { CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: $0) < since }
            guard !Task.isCancelled, !busy, !typing() else { return }
            let urls = wheelItems(await finderSelection())
            guard !Task.isCancelled, !urls.isEmpty, NSEvent.modifierFlags.contains(flag), NSEvent.pressedMouseButtons == 0 else { return }
            hold = nil
            selection = urls; movedOff = false; lastMouse = nil; keyTap = KeyTap()
            show(urls)
            guard panel?.isVisible == true else { selection = nil; return }
            follow = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { _ in MainActor.assumeIsolated { WheelController.shared.track() } }
            follow?.tolerance = 0.005
        }
    }

    private func cancelHold() { hold?.cancel(); hold = nil }

    /// Whether a text field has the keyboard focus (needs Accessibility, which the wheel's keys already ask for).
    private func typing() -> Bool {
        var focused: CFTypeRef?, role: CFTypeRef?
        // On the main thread: a hung Finder mustn't hold Lemon up for AX's default 6 s.
        let system = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(system, 0.25)
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let focused, AXUIElementCopyAttributeValue(focused as! AXUIElement, kAXRoleAttribute as CFString, &role) == .success else { return false }
        return [kAXTextFieldRole, kAXTextAreaRole, kAXComboBoxRole].contains(role as? String ?? "")
    }

    /// Finder's selection (the desktop's too), asked through AppleScript. Empty if Lemon may not control Finder or
    /// Finder doesn't answer within a second.
    private func finderSelection() async -> [URL] {
        // Asked first and without a time limit: the first time, macOS asks the user, and a timed-out osascript would
        // take the question away before it's answered. osascript then runs on Lemon's permission.
        let allowed = await withCheckedContinuation { c in
            DispatchQueue.global(qos: .userInitiated).async {
                var finder = AEAddressDesc(), id = Array("com.apple.finder".utf8)
                guard AECreateDesc(typeApplicationBundleID, &id, id.count, &finder) == noErr else { c.resume(returning: false); return }
                defer { AEDisposeDesc(&finder) }
                c.resume(returning: AEDeterminePermissionToAutomateTarget(&finder, typeWildCard, typeWildCard, true) == noErr)
            }
        }
        guard allowed else { return [] }
        let script = """
            tell application "Finder" to set s to selection as alias list
            set o to ""
            repeat with a in s
            set o to o & POSIX path of a & linefeed
            end repeat
            o
            """
        let ask = Task { try await run("/usr/bin/osascript", ["-e", script]) }
        let timeout = Task { try await Task.sleep(nanoseconds: 1_000_000_000); ask.cancel() }
        defer { timeout.cancel() }
        let out = (try? await ask.value) ?? ""
        return out.split(separator: "\n").filter { $0.hasPrefix("/") }.map { URL(fileURLWithPath: String($0)) }
    }

    /// Follows the pointer and the key for the wheel over a selection: a tap enters what's lit, a hold closes the wheel.
    private func track(force: Bool = false) {
        // Another app in front (⌘-Tab, a click in the Dock): close, or the key tap would keep taking WASD there.
        guard selection != nil, let panel, panel.isVisible,
              NSWorkspace.shared.frontmostApplication?.bundleIdentifier == "com.apple.finder" else { hide(); return }
        let m = NSEvent.mouseLocation, f = panel.frame
        // A click (or a Control-click) outside the wheel closes it; ones on it are the local monitor's.
        if !force, NSEvent.pressedMouseButtons != 0, !f.contains(m) { hide(); return }
        if force || m != lastMouse {
            lastMouse = m
            model.hover(at: CGPoint(x: m.x - f.minX, y: f.maxY - m.y))
            if !model.lemonHot { movedOff = true }
        }
        guard let flag = PrefStore.shared.p.key.flag else { return }
        switch keyTap.update(held: NSEvent.modifierFlags.contains(flag), now: ProcessInfo.processInfo.systemUptime) {
        case .enter: activate()
        case .close: hide()
        case nil: break
        }
    }

    /// Enters what's lit: a ring on the hub, or runs a slice. The lemon (after moving off it) queues the files, or with
    /// the center as the way back, goes to the hub.
    private func activate() {
        guard let urls = selection else { return }
        if model.inHub { if let m = model.hoveredMode { model.go(m) }; return }
        if let target = model.hovered { model.clearHover(); pick(target, urls) }
        else if model.lemonHot, movedOff {
            if model.centerBack { model.go(number: 0) } else { model.clearHover(); queueIt(urls) }
        }
    }

    private func show(_ urls: [URL], links: [URL] = []) {
        // Folders: look inside so the right formats are offered. This runs mid-drag on the main thread, so only a
        // sample is read; the formats on offer come from the extensions found.
        var files: [URL] = [], loose: [URL] = [], folders: [URL] = []
        for u in urls {
            // A package (an app, a Photos library) is one item, not a folder to look inside.
            guard u.hasDirectoryPath, !NSWorkspace.shared.isFilePackage(atPath: u.path) else { files.append(u); loose.append(u); continue }
            folders.append(u)
            guard let it = FileManager.default.enumerator(at: u, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles, .skipsPackageDescendants]) else { continue }
            var scanned = 0
            // ponytail: first 5,000 entries; a rare type deep in a huge folder may be missed (the drop still adds all).
            while scanned < 5_000, let f = it.nextObject() as? URL { scanned += 1; if kindOf[ext(of: f)] != nil { files.append(f) } }
        }
        model.load(files, files: loose, folders: folders, links: links)
        guard !model.modes.isEmpty else { return }
        // Folders on their own open on Commands: a project folder nearly always has something convertible inside.
        if !folders.isEmpty, folders.count == urls.count { model.go(.commands) }
        installKeyTap()   // in case permission was granted since launch
        if panel == nil {
            let p = WheelPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
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
        panel?.orderFrontRegardless(); panel?.makeKey()
        syncTap()
        withAnimation(.spring(response: 0.28, dampingFraction: 0.72)) { model.shown = true }
    }

    private func hide() {
        selection = nil; follow?.invalidate(); follow = nil
        panel?.orderOut(nil)
        syncTap()
    }

    private func pick(_ p: Preset, _ urls: [URL]) {
        hide()
        if let id = p.command {
            if let c = PrefStore.shared.p.commands?.first(where: { $0.id == id }) { runCommand(c, on: model.targets(c)) }
        } else { sharedQueue.addAndRun(urls, p) }
    }

    private func queueIt(_ urls: [URL]) {
        hide(); sharedQueue.add(urls); AppHooks.openWindow?(); NSApp.activate(ignoringOtherApps: true)
    }
}

enum AppHooks { @MainActor static var openWindow: (() -> Void)? }
