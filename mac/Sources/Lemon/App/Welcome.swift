import SwiftUI
import Combine
import UniformTypeIdentifiers

/// A kind of file Lemon converts with a Homebrew tool, as the welcome guide offers it.
private struct Converter: Identifiable {
    let title: String, detail: String, symbol: String
    let tool: String, formula: String   // what Tools.find looks for, and what `brew install` takes
    var id: String { tool }
    static let all = [
        Converter(title: "Pictures", detail: "JPG, PNG, WebP, HEIC and more", symbol: "photo", tool: "magick", formula: "imagemagick"),
        Converter(title: "Videos and music", detail: "MP4, MOV, MP3, WAV and more", symbol: "film", tool: "ffmpeg", formula: "ffmpeg"),
        Converter(title: "Documents and ebooks", detail: "Markdown, EPUB and more", symbol: "book", tool: "pandoc", formula: "pandoc"),
        Converter(title: "Making PDFs", detail: "Word and other documents to PDF", symbol: "doc.richtext", tool: "soffice", formula: "--cask libreoffice"),
        Converter(title: "Shrinking PDFs", detail: "Make PDFs smaller", symbol: "arrow.down.right.and.arrow.up.left", tool: "gs", formula: "ghostscript"),
    ]
}

/// The first-run guide: which kinds of files to set up (each one installs a converter with Homebrew, in Terminal), then
/// the wheel. Shown once; Settings › About brings it back.
struct WelcomeView: View {
    @ObservedObject private var store = PrefStore.shared
    @Environment(\.openSettings) private var openSettings
    @State private var step = 0
    @State private var ready: Set<String> = []
    @State private var installing: Set<String> = []
    private let tick = Timer.publish(every: 2, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(spacing: 0) {
          ScrollView {
           VStack(spacing: 0) {
            Text(store.p.theme.center.isEmpty ? "🍋" : store.p.theme.center).font(.system(size: 56))
            Text(step == 0 ? "Welcome to Lemon" : "Convert without opening Lemon").font(.largeTitle.bold()).padding(.top, 4)
            Text(step == 0 ? "Simple documents work right away. What else do you want to convert?"
                           : "Lemon pops up a wheel of formats while you drag files, anywhere on your Mac.")
                .foregroundStyle(.secondary).padding(.top, 2)
            Group { if step == 0 { converters } else { WheelDemo() } }.padding(.vertical, 18)
           }
           .frame(maxWidth: .infinity).padding(.top, 8)
          }
            HStack {
                if step == 1 { Button("Back") { withAnimation(.snappy) { step = 0 } }.glassButton() }
                Spacer()
                HStack(spacing: 6) { ForEach(0..<2) { Circle().fill(.primary.opacity($0 == step ? 0.8 : 0.2)).frame(width: 7, height: 7) } }
                Spacer()
                if step == 0 {
                    Button("Next") { withAnimation(.snappy) { step = 1 } }.glassButton(prominent: true).keyboardShortcut(.defaultAction)
                } else {
                    Button("Customize the wheel") { done(); NSApp.setActivationPolicy(.regular); openSettings() }.glassButton()
                    Button("Get started", action: done).glassButton(prominent: true).keyboardShortcut(.defaultAction)
                }
            }
            .controlSize(.large).padding(20)
        }
        // No more than the main screen's minimum: asking for more height than the window has hid its title bar.
        // A short window scrolls the guide instead.
        .frame(minWidth: 620, minHeight: 360)
        .onAppear(perform: refresh)
        .onReceive(tick) { _ in refresh() }
        // Back from Terminal: whatever finished installing shows as ready, and the rest can be tried again.
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in installing = []; refresh() }
    }

    private var converters: some View {
        VStack(spacing: 8) {
            ForEach(Converter.all) { c in
                HStack(spacing: 14) {
                    Image(systemName: c.symbol).font(.title2).frame(width: 34).foregroundStyle(.tint)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(c.title).font(.headline)
                        Text(c.detail).font(.callout).foregroundStyle(.secondary)
                    }
                    Spacer()
                    if ready.contains(c.tool) { Label("Ready", systemImage: "checkmark.circle.fill").foregroundStyle(.green) }
                    else if installing.contains(c.tool) { ProgressView().controlSize(.small) }
                    else { Button(Tools.find("brew") == nil ? "Get it…" : "Add") { install(c) }.glassButton() }
                }
                .padding(.horizontal, 16).padding(.vertical, 10)
                .background(.background.opacity(0.35), in: .rect(cornerRadius: 14, style: .continuous))
            }
        }
        .frame(maxWidth: 520)
    }

    private func refresh() {
        let now = Set(Converter.all.map(\.tool).filter { Tools.find($0) != nil })
        if now != ready { withAnimation(.snappy) { ready = now; installing.subtract(now) } }
    }

    /// Runs `brew install` in a Terminal window, which stays open on an error. Without Homebrew, opens brew.sh.
    /// A .command file rather than scripting Terminal, which would need Automation permission.
    private func install(_ c: Converter) {
        guard let brew = Tools.find("brew") else { NSWorkspace.shared.open(URL(string: "https://brew.sh")!); return }
        let script = FileManager.default.temporaryDirectory.appendingPathComponent("Lemon - add \(c.tool).command")
        let text = """
            #!/bin/sh
            echo 'Adding \(c.title.lowercased()) to Lemon…'
            '\(brew)' install \(c.formula) && exit
            echo; echo 'Something went wrong. Press Return to close.'; read _
            """
        do {
            try text.write(to: script, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)
            NSWorkspace.shared.open(script)
            installing.insert(c.tool)
        } catch { NSWorkspace.shared.open(URL(string: "https://brew.sh")!) }
    }

    private func done() { withAnimation(.spring(duration: 0.4)) { store.p.welcomeShown = true } }
}

/// Six seconds acted out on a loop with the real wheel: a photo is dragged, shaken (or the wheel's key pops up), the
/// wheel opens on the Convert ring for pictures, the photo is dropped on JPG, and the JPG appears under the original.
/// With Reduce motion, a still of the open wheel and the result.
private struct WheelDemo: View {
    @ObservedObject private var store = PrefStore.shared
    @Environment(\.accessibilityReduceMotion) private var still
    /// The wheel as it is, and with the target wedge lit: two models so nothing is changed while the scene draws.
    @StateObject private var idle = WheelModel()
    @StateObject private var hot = WheelModel()
    private static let size = CGSize(width: 440, height: 270), origin = CGPoint(x: 60, y: 70), center = CGPoint(x: 300, y: 135)
    private static let scale = 0.66

    /// The Convert ring for pictures as Settings has it, and the wedge the photo is dropped on (JPG when it's there).
    private var ring: (presets: [Preset], target: Int) {
        let all = builtIn + store.p.custom
        let presets = (store.p.slots[.image] ?? favorites[.image]!).compactMap { n in all.first { $0.name == n && $0.kind == .image } }
        let ps = Array(presets.filter { $0.ext != "heic" }.prefix(8))
        return (ps, ps.firstIndex { $0.ext == "jpg" } ?? 0)
    }

    var body: some View {
        let p = store.p, shake = p.onShake, ring = ring
        let summon = shake ? "2. Shake" : p.key.held ? "2. Hold \(p.key.name)" : "2. Press \(p.key.name)"
        VStack(spacing: 14) {
            TimelineView(.animation(paused: still)) { ctx in
                scene(t: still ? nil : ctx.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 6), shake: shake, key: p.key.name,
                      ring: ring)
            }
            .frame(width: Self.size.width, height: Self.size.height)
            HStack(spacing: 24) {
                ForEach(Array(["1. Drag", summon, "3. Drop"].enumerated()), id: \.offset) { i, s in
                    TimelineView(.animation(minimumInterval: 0.1, paused: still)) { ctx in
                        let t = ctx.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 6)
                        let on = still || i == (t < 1.4 ? 0 : t < 2.4 ? 1 : 2)
                        Text(s).font(.title3.bold()).foregroundStyle(on ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary)).opacity(on ? 1 : 0.5)
                    }
                }
            }
            if !AXIsProcessTrusted() {
                HStack {
                    Text("Keys on the wheel (Tab, 1–9, Esc\(p.onKey && !p.key.held ? ", \(p.key.name)" : "")) need Lemon to be allowed in Accessibility.")
                        .font(.callout).foregroundStyle(.secondary)
                    Button("Open Settings", action: openAccessibilitySettings).controlSize(.small)
                }
            }
        }
        .onAppear(perform: load)
        .onChange(of: store.p.slots) { load() }
        .onChange(of: store.p.custom) { load() }
    }

    private func load() {
        let r = ring
        idle.load(demo: r.presets); hot.load(demo: r.presets)
        hot.hovered = r.presets.indices.contains(r.target) ? r.presets[r.target] : nil
    }

    /// `t` in 0..<6, or nil for the still.
    private func scene(t: Double?, shake: Bool, key: String, ring: (presets: [Preset], target: Int)) -> some View {
        let o = Self.origin, c = Self.center, k = Self.scale
        // The target wedge's middle, where the photo is dropped.
        let a = Double(ring.target) / Double(max(ring.presets.count, 1)) * 2 * .pi - .pi / 2, mid = (WheelModel.inner + WheelModel.outer) / 2 * k
        let target = CGPoint(x: c.x + cos(a) * mid, y: c.y + sin(a) * mid)
        let ext = ring.presets.indices.contains(ring.target) ? ring.presets[ring.target].ext : "jpg"
        func span(_ a: Double, _ b: Double) -> Double { guard let t else { return 1 }; let x = min(max((t - a) / (b - a), 0), 1); return x * x * (3 - 2 * x) }
        func mix(_ a: CGPoint, _ b: CGPoint, _ k: Double) -> CGPoint { CGPoint(x: a.x + (b.x - a.x) * k, y: a.y + (b.y - a.y) * k) }
        // Where the dragged copy is: to the middle, shaken, then onto the format.
        var ghost = mix(mix(o, c, span(0.3, 1.4)), target, span(2.4, 3.4))
        if shake, let t, t > 1.4, t < 2.3 { ghost.x += sin((t - 1.4) * 2 * .pi * 4.5) * 14 }
        let ghostOn = t == nil ? 0 : span(0, 0.3) * (1 - span(3.4, 3.7))
        let wheel = t == nil ? 1 : span(2.2, 2.5) * (1 - span(3.7, 4.0))
        let lit = t.map { $0 > 3.0 } ?? true
        let result = t == nil ? 1 : span(4.0, 4.4) * (1 - span(5.6, 6.0))
        let keycap = t == nil || shake ? 0 : span(1.4, 1.6) * (1 - span(2.4, 2.6))
        return ZStack {
            // The real wheel, opening with the same spring-like grow it has.
            WheelView(model: lit ? hot : idle)
                .scaleEffect(k * (0.82 + 0.18 * wheel)).opacity(wheel)
                .frame(width: WheelModel.size, height: WheelModel.size).position(c)
            file("Photo.heic").position(o)
            file("Photo.\(ext)").scaleEffect(0.7 + 0.3 * result).opacity(result).position(x: o.x, y: o.y + 110)
            // The dragged copy, see-through as macOS draws it, with the pointer on it.
            ZStack(alignment: .topLeading) {
                file("Photo.heic", label: false).opacity(0.7)
                Image(systemName: "cursorarrow").font(.system(size: 20)).foregroundStyle(.white, .black).offset(x: 34, y: 30)
            }
            .opacity(ghostOn).position(ghost)
            Text(key).font(.system(size: 14, weight: .semibold, design: .rounded)).padding(.horizontal, 12).padding(.vertical, 6)
                .background(.background, in: .rect(cornerRadius: 7)).overlay(RoundedRectangle(cornerRadius: 7).strokeBorder(.secondary.opacity(0.5)))
                .shadow(radius: 2, y: 1)
                .scaleEffect(0.6 + 0.4 * keycap).opacity(keycap).position(x: c.x + 70, y: c.y + 70)
        }
        .frame(width: Self.size.width, height: Self.size.height)
    }

    /// A file as Finder shows it: its icon, and the name under it.
    private func file(_ name: String, label: Bool = true) -> some View {
        let type = UTType(filenameExtension: (name as NSString).pathExtension) ?? .image
        return VStack(spacing: 2) {
            Image(nsImage: NSWorkspace.shared.icon(for: type)).resizable().frame(width: 56, height: 56)
            Text(name).font(.caption.weight(.medium)).opacity(label ? 1 : 0)
        }
    }
}
