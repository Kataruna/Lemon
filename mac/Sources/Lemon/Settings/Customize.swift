import SwiftUI
import AppKit

// MARK: saved preferences

/// An sRGB color that can be saved.
struct RGBA: Codable, Hashable {
    var r, g, b: Double
    init(_ r: Double, _ g: Double, _ b: Double) { self.r = r; self.g = g; self.b = b }
    init(_ c: Color) {
        let n = NSColor(c).usingColorSpace(.sRGB) ?? .orange
        self.init(n.redComponent, n.greenComponent, n.blueComponent)
    }
    var color: Color { Color(.sRGB, red: r, green: g, blue: b) }
}

struct WheelTheme: Codable, Hashable {
    var name: String
    var accent: RGBA, deep: RGBA   // the highlight runs from accent (top) to deep (bottom)
    var tint = 0.12                // how strongly the glass takes the accent
    var center = "🍋"
    var label: RGBA? = nil         // nil follows light / dark mode
    var system: Bool? = nil        // follow the accent color in System Settings instead of `accent`

    /// The accent color, or the Mac's own; the darker end of the gradient follows it.
    var accentColor: Color { system == true ? Color(nsColor: .controlAccentColor) : accent.color }
    var deepColor: Color {
        guard system == true else { return deep.color }
        return Color(nsColor: NSColor.controlAccentColor.blended(withFraction: 0.25, of: .black) ?? .controlAccentColor)
    }
    var gradient: LinearGradient { LinearGradient(colors: [accentColor, deepColor], startPoint: .top, endPoint: .bottom) }
    var text: Color { label?.color ?? .primary }

    static let builtIn = [
        WheelTheme(name: "Tangerine", accent: RGBA(1, 0.55, 0), deep: RGBA(1, 0.36, 0)),
        WheelTheme(name: "Lime", accent: RGBA(0.55, 0.85, 0.1), deep: RGBA(0.3, 0.66, 0.05), center: "🍋‍🟩"),
        WheelTheme(name: "Berry", accent: RGBA(0.93, 0.22, 0.5), deep: RGBA(0.72, 0.1, 0.45), center: "🍓"),
        WheelTheme(name: "Ocean", accent: RGBA(0.15, 0.6, 1), deep: RGBA(0.05, 0.38, 0.9), center: "🫐"),
        WheelTheme(name: "Graphite", accent: RGBA(0.55, 0.56, 0.6), deep: RGBA(0.33, 0.34, 0.38)),
        WheelTheme(name: "macOS Accent", accent: RGBA(0, 0.48, 1), deep: RGBA(0, 0.36, 0.75), system: true),
    ]
}

/// How the wheel's glass looks. macOS has no blur-radius setting for Liquid Glass or materials, so frost (saved as
/// `blur`) is a choice of four looks, from clear to heavy.
struct GlassStyle: Codable, Equatable {
    var blur = 2          // 0 clear glass, 1 clear glass + light frost, 2 regular glass, 3 regular glass + heavy frost
    var opacity = 1.0     // of the glass layer; lower shows more of what's behind
    var liquid: Bool? = nil   // false: the glass drawn flat, without lensing (Optional so older prefs decode)
    static let blurNames = ["Clear", "Light", "Standard", "Heavy"]
}

struct Prefs: Codable, Equatable {
    var theme = WheelTheme.builtIn[0]
    var slots: [Kind: [String]] = [:]   // the Convert ring per kind, by preset name; missing = `favorites`
    var custom: [Preset] = []
    // Optional so prefs saved before these existed still decode.
    var hidden: Set<String>? = nil      // built-in rings switched off, by WheelMode id
    var rings: [CustomRing]? = nil
    var commands: [WheelCommand]? = nil
    var hub: Bool? = nil                // every file opens on the hub
    var backPill: Bool? = nil           // back to the hub from a pill under the ring, instead of the center
    var start: [Kind: String]? = nil    // without the hub, the ring files of a kind open on; missing = the first ring
    var glass: GlassStyle? = nil
    var wheelOnShake: Bool? = nil       // nil = on
    var wheelOnKey: Bool? = nil         // nil = on
    var wheelKey: WheelKey? = nil       // nil = Shift
    var welcomeShown: Bool? = nil
    var checkUpdates: Bool? = nil       // nil = on

    /// Both off can't be picked in Settings; if saved that way anyway, shaking counts as on.
    var onShake: Bool { (wheelOnShake ?? true) || !(wheelOnKey ?? true) }
    var onKey: Bool { wheelOnKey ?? true }
    var key: WheelKey { wheelKey ?? .shift }
    /// "shake or hold Shift", "press Space", …: how the wheel opens right now, for hints.
    var summon: String {
        let k = key == .shift ? "hold Shift" : "press \(key.name)"
        return onShake && onKey ? "shake or \(k)" : onShake ? "shake" : k
    }
}

/// The key that opens the wheel mid-drag. Shift is held (read off the mouse events, no permission needed); any other
/// key is pressed, which needs the key tap and so Accessibility permission.
struct WheelKey: Codable, Hashable {
    var code: Int64, name: String
    static let shift = WheelKey(code: 56, name: "Shift")
    /// Esc closes the wheel, Tab and 0–9 switch rings, and ⌘ ⌥ ⌃ change what the drag does.
    static let taken: Set<Int64> = [53, 48, 29, 18, 19, 20, 21, 23, 22, 26, 28, 25, 55, 54, 58, 61, 59, 62, 63, 57]
    private static let names: [Int64: String] = [49: "Space", 36: "Return", 51: "Delete", 117: "Forward Delete", 115: "Home", 119: "End",
        116: "Page Up", 121: "Page Down", 123: "←", 124: "→", 125: "↓", 126: "↑", 122: "F1", 120: "F2", 99: "F3", 118: "F4",
        96: "F5", 97: "F6", 98: "F7", 100: "F8", 101: "F9", 109: "F10", 103: "F11", 111: "F12"]
    /// The key in a keyDown or flagsChanged event, or nil if it can't open the wheel.
    init?(_ e: NSEvent) {
        let c = Int64(e.keyCode)
        if e.type == .flagsChanged { guard c == 56 || c == 60 else { return nil }; self = .shift; return }
        guard !Self.taken.contains(c) else { return nil }
        // Function keys and the like come through as private-use characters (U+F700 and up), so they get a number.
        let chars = e.charactersIgnoringModifiers?.uppercased() ?? ""
        let printable = chars.unicodeScalars.first.map { $0.value > 32 && $0.value < 0xF700 } ?? false
        self.init(code: c, name: Self.names[c] ?? (printable ? chars : "Key \(c)"))
    }
    init(code: Int64, name: String) { self.code = code; self.name = name }
}

/// A ring the user puts together from any presets: convert, optimize, remove background, their own.
struct CustomRing: Codable, Hashable, Identifiable {
    var id = UUID().uuidString
    var name = ""
    var symbol = "star"
    var presets: [String] = []   // by name, clockwise from the top; ones a file can't use are skipped
    static let symbols = ["star", "bolt", "heart", "flag", "bookmark", "wand.and.stars", "paintbrush", "camera", "film", "music.note", "doc", "briefcase"]
}

/// Every preset a custom ring can hold, grouped for a menu. "PDF" is in both Images and Documents: by name it's one entry.
func ringCandidates() -> [(String, [Preset])] {
    let custom = PrefStore.shared.p.custom
    return [("Optimize", optimizePresets), ("Resize images", resizePresets), ("Remove background", backgroundPresets)]
        + Kind.allCases.map { k in (k.rawValue, (builtIn + custom).filter { $0.kind == k }) }
}

/// Everything the user customized, saved to UserDefaults on every change.
final class PrefStore: ObservableObject {
    static let shared = PrefStore()
    // ponytail: one JSON blob; a field added later must be optional or the old blob fails to decode and resets.
    /// Saved a moment after the last change: a slider or color wheel changes it dozens of times a second.
    @Published var p: Prefs { didSet { pending?.cancel(); pending = DispatchWorkItem { [weak self] in self?.save() }; DispatchQueue.main.asyncAfter(deadline: .now() + 0.5, execute: pending!) } }
    private var pending: DispatchWorkItem?
    private init() {
        p = UserDefaults.standard.data(forKey: "prefs").flatMap { try? JSONDecoder().decode(Prefs.self, from: $0) } ?? Prefs()
        // Quitting inside that moment still saves.
        NotificationCenter.default.addObserver(forName: NSApplication.willTerminateNotification, object: nil, queue: nil) { [weak self] _ in self?.save() }
        // The "macOS Accent" theme redraws when the accent is changed in System Settings.
        NotificationCenter.default.addObserver(forName: NSColor.systemColorsDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            if self?.p.theme.system == true { self?.objectWillChange.send() }
        }
    }
    private func save() {
        pending?.cancel(); pending = nil
        if let d = try? JSONEncoder().encode(p) { UserDefaults.standard.set(d, forKey: "prefs") }
    }
}

/// What can go on the Convert ring for files of this kind.
func wheelCandidates(_ k: Kind) -> [Preset] {
    let all = builtIn + PrefStore.shared.p.custom
    return all.filter { $0.kind == k } + (k == .video ? all.filter { $0.kind == .audio && audioFromVideo.contains($0.ext) } : [])
}

// MARK: settings

/// The theme's accent on everything inside (buttons, toggles, pickers, progress), following changes as they're made.
struct Themed: ViewModifier {
    @ObservedObject private var store = PrefStore.shared
    func body(content: Content) -> some View { content.tint(store.p.theme.accentColor) }
}

extension View {
    func settingsPage(height: CGFloat? = nil) -> some View {
        formStyle(.grouped).frame(width: 500, height: height).modifier(Themed())
            // Not seeThroughWindow(): on the Settings window that hides the close/minimise/zoom buttons.
            .scrollContentBackground(.hidden).background(DesktopBlur().ignoresSafeArea())
    }
}

struct SettingsView: View {
    var body: some View {
        TabView {
            WheelSettings().tabItem { Label("Wheel", systemImage: "circle.circle") }
            PresetSettings().tabItem { Label("Presets", systemImage: "slider.horizontal.3") }
            BackgroundSettings().tabItem { Label("Background", systemImage: "scissors") }
            AboutSettings().tabItem { Label("About", systemImage: "info.circle") }
        }
    }
}

/// Shake, a key, or both; never neither. Turning off the last one turns the other back on and says so.
struct WheelTriggers: View {
    @ObservedObject private var store = PrefStore.shared
    @State private var note: String?
    @State private var recording: Any?   // the key monitor while picking a key
    @State private var refused = false

    var body: some View {
        let p = store.p
        Toggle("Open it by shaking the mouse while dragging", isOn: Binding(get: { p.onShake }, set: { on in
            store.p.wheelOnShake = on
            if !on, !p.onKey { store.p.wheelOnKey = true; note = "pressing a key" } else { note = nil }
        }))
        Toggle("Open it by pressing a key while dragging", isOn: Binding(get: { p.onKey }, set: { on in
            store.p.wheelOnKey = on
            if !on, !p.onShake { store.p.wheelOnShake = true; note = "shaking the mouse" } else { note = nil }
        }))
        if let note {
            Text("You can't turn off both ways to open the wheel, so opening it by \(note) is back on.").font(.caption).foregroundStyle(.secondary)
        }
        if p.onKey {
            LabeledContent(p.key == .shift ? "Key (hold)" : "Key") {
                Button(recording != nil ? (refused ? "Pick another key…" : "Press a key…") : p.key.name) { record() }
                    .onDisappear(perform: stop)
            }
            if p.key != .shift, !AXIsProcessTrusted() {
                HStack {
                    Text("Keys other than Shift need Accessibility permission.").font(.caption).foregroundStyle(.secondary)
                    Spacer()
                    Button("Open Accessibility Settings", action: openAccessibilitySettings).controlSize(.small)
                }
            }
        }
    }

    /// The next key pressed becomes the wheel's key; Esc cancels. Keys the wheel already uses are turned down.
    private func record() {
        guard recording == nil else { return }
        refused = false
        recording = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { e in
            if e.type == .keyDown, e.keyCode == 53 { stop(); return nil }
            if e.type == .flagsChanged, e.modifierFlags.intersection(.deviceIndependentFlagsMask).isEmpty { return nil }   // a modifier let go
            guard let k = WheelKey(e) else { refused = true; return nil }
            store.p.wheelKey = k == .shift ? nil : k
            stop(); return nil
        }
    }
    private func stop() { if let r = recording { NSEvent.removeMonitor(r) }; recording = nil; refused = false }
}

func openAccessibilitySettings() {
    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
}

struct AboutSettings: View {
    @ObservedObject private var store = PrefStore.shared
    @ObservedObject private var updates = Updates.shared
    var body: some View {
        Form {
            Section {
                LabeledContent("Version", value: Updates.current ?? "Development build")
                Toggle("Check for updates when Lemon starts", isOn: Binding(get: { store.p.checkUpdates ?? true }, set: { store.p.checkUpdates = $0 }))
                if let u = updates.available { Link("Download Lemon \(u.version)", destination: u.url) }
            } footer: {
                Text("Lemon asks GitHub for its newest Mac release. Nothing is installed by itself.").font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Button("Show the welcome guide again") { store.p.welcomeShown = false; AppHooks.openWindow?() }
            }
        }
        .settingsPage(height: 260)
    }
}

/// A file of each kind, for the preview wheel.
private let sample: [Kind: String] = [.image: "png", .audio: "wav", .video: "mov", .document: "docx"]

struct WheelSettings: View {
    @ObservedObject private var store = PrefStore.shared
    @StateObject private var preview = WheelModel()
    @State private var kind = Kind.image
    @State private var editingRing: CustomRing?
    @State private var editingCommand: WheelCommand?

    var body: some View {
        let t = store.p.theme
        Form {
            Section {
                WheelView(model: preview).scaleEffect(0.7).frame(width: 266, height: 266).frame(maxWidth: .infinity)
            }
            Section("Look") {
                Picker("Theme", selection: Binding(get: { t.name }, set: { n in
                    if let b = WheelTheme.builtIn.first(where: { $0.name == n }) { store.p.theme = b }
                })) {
                    ForEach(WheelTheme.builtIn, id: \.name) { Text($0.name).tag($0.name) }
                    if t.name == "Custom" { Text("Custom").tag("Custom") }
                }
                ColorPicker("Accent", selection: Binding(get: { t.accentColor }, set: { c in
                    let n = NSColor(c)
                    var theme = t   // one change, not three: each one redraws every wheel
                    theme.system = nil
                    theme.accent = RGBA(c)
                    theme.deep = RGBA(Color(nsColor: n.blended(withFraction: 0.25, of: .black) ?? n))
                    theme.name = "Custom"
                    store.p.theme = theme
                }), supportsOpacity: false)
                Slider(value: bind(\.tint), in: 0...0.5) { Text("Glass tint") }
                // The last character typed wins, so a new emoji replaces the old one.
                TextField("Center", text: Binding(get: { t.center }, set: { v in edit { $0.center = String(v.suffix(1)) } }))
                Toggle("Custom text color", isOn: Binding(get: { t.label != nil }, set: { on in edit { $0.label = on ? RGBA(.white) : nil } }))
                if let l = t.label {
                    ColorPicker("Text color", selection: Binding(get: { l.color }, set: { c in edit { $0.label = RGBA(c) } }), supportsOpacity: false)
                }
            }
            Section {
                let g = store.p.glass ?? GlassStyle()
                if #available(macOS 26, *) {
                    Toggle("Liquid Glass", isOn: Binding(get: { g.liquid ?? true }, set: { v in var x = g; x.liquid = v; store.p.glass = x }))
                }
                Picker("Frost", selection: Binding(get: { g.blur }, set: { v in var x = g; x.blur = v; store.p.glass = x })) {
                    ForEach(GlassStyle.blurNames.indices, id: \.self) { Text(GlassStyle.blurNames[$0]).tag($0) }
                }.pickerStyle(.segmented)
                Slider(value: Binding(get: { g.opacity }, set: { v in var x = g; x.opacity = v; store.p.glass = x }), in: 0.3...1) { Text("Opacity") }
            } header: { Text("Glass") } footer: {
                Text("Clear is Liquid Glass's clear style; Light frosts it a little; Standard is the regular style; Heavy frosts it further. The edges always bend what's behind. With Liquid Glass off, the same glass is drawn flat, frosted without bending. Lower opacity shows more of what's behind the wheel.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                WheelTriggers()
            } header: { Text("Opening the wheel") } footer: {
                Text("Shift is held while dragging; any other key is pressed once. Esc closes the wheel and keeps the drag going.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                let hidden = store.p.hidden ?? []
                let visible = WheelMode.builtIns.filter { !hidden.contains($0.id) }
                ForEach(WheelMode.builtIns, id: \.self) { m in
                    Toggle(isOn: Binding(get: { visible.contains(m) }, set: { on in
                        var h = hidden; if on { h.remove(m.id) } else { h.insert(m.id) }; store.p.hidden = h
                    })) { Label(m.title, systemImage: m.symbol) }
                    // Keep at least one ring: the last built-in one stays on unless there's a ring of your own.
                    .disabled(visible == [m] && (store.p.rings ?? []).isEmpty)
                }
                ForEach(store.p.rings ?? []) { r in
                    HStack {
                        Label(r.name, systemImage: r.symbol)
                        Spacer()
                        Text("\(r.presets.count) tools").foregroundStyle(.secondary)
                        Button("Edit") { editingRing = r }
                        Button { deleteRing(r) } label: { Image(systemName: "trash") }.buttonStyle(.borderless)
                            .disabled(visible.isEmpty && store.p.rings?.count == 1)
                    }
                }
                Button("New Ring…") { editingRing = CustomRing() }.disabled((store.p.rings ?? []).count >= 5)
            } header: { Text("Rings") } footer: {
                Text("Make a ring of the tools you use together, like Optimize and Remove background. Rings you hide are skipped by the hub, Tab and the scroll wheel.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                let commands = store.p.commands ?? []
                ForEach(Array(commands.enumerated()), id: \.element.id) { i, c in
                    HStack {
                        Label(c.name, systemImage: c.symbol)
                        Spacer()
                        Text(c.on.rawValue).foregroundStyle(.secondary)
                        // Wheel order, clockwise from the top.
                        Button { store.p.commands?.swapAt(i, i - 1) } label: { Image(systemName: "chevron.up") }.buttonStyle(.borderless).disabled(i == 0)
                        Button { store.p.commands?.swapAt(i, i + 1) } label: { Image(systemName: "chevron.down") }.buttonStyle(.borderless).disabled(i == commands.count - 1)
                        Button("Edit") { editingCommand = c }
                        Button { store.p.commands?.removeAll { $0.id == c.id } } label: { Image(systemName: "trash") }.buttonStyle(.borderless)
                    }
                }
                Button("New Command…") { editingCommand = WheelCommand() }
            } header: { Text("Commands") } footer: {
                Text("Your own shell commands for dragged files, folders and links, on a Commands ring. A folder dragged on its own opens on it. Up to 8 of each.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Toggle("Open on the hub", isOn: Binding(get: { store.p.hub ?? false }, set: { store.p.hub = $0 }))
                Toggle("Back to the hub from a button under the ring", isOn: Binding(get: { store.p.backPill ?? false }, set: { store.p.backPill = $0 }))
                if !(store.p.hub ?? false) {
                    let rings = WheelMode.all.filter { !(store.p.hidden ?? []).contains($0.id) && $0 != .commands }
                    ForEach(Kind.allCases, id: \.self) { k in
                        Picker(k.rawValue, selection: Binding(get: { rings.first { $0.id == store.p.start?[k] }?.id ?? rings.first?.id ?? "" }, set: { v in
                            var s = store.p.start ?? [:]; s[k] = v; store.p.start = s
                        })) {
                            ForEach(rings, id: \.id) { m in Label(m.title, systemImage: m.symbol).tag(m.id) }
                        }
                    }
                }
            } header: { Text("Opens on") } footer: {
                Text("The hub shows the rings as wedges for every kind of file; rest the files on one to open it. Without it, each kind opens on its own ring. With more than four rings, the hub is how you switch. Inside a ring, rest on the center to go back to the hub, or turn on the button under the ring instead. If a ring has nothing for the files, the wheel opens on the hub or the first ring instead.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            slotsSection
        }
        .settingsPage(height: 640)
        .sheet(item: $editingRing) { RingEditor(ring: $0) }
        .sheet(item: $editingCommand) { CommandEditor(command: $0) }
        .onAppear(perform: reloadPreview)
        // Not on theme changes: the preview redraws with those by itself, and a slider sends dozens a second.
        .onChange(of: store.p.slots) { reloadPreview() }
        .onChange(of: store.p.custom) { reloadPreview() }
        .onChange(of: store.p.rings) { reloadPreview() }
        .onChange(of: store.p.hidden) { reloadPreview() }
        .onChange(of: store.p.hub) { reloadPreview() }
        .onChange(of: store.p.backPill) { reloadPreview() }
        .onChange(of: store.p.start) { reloadPreview() }
        .onChange(of: kind) { reloadPreview() }
    }

    private var slotsSection: some View {
        // Names whose preset is gone (deleted, or changed to another kind) are dropped.
        let candidates = wheelCandidates(kind), names = Set(candidates.map(\.name))
        let slots = (store.p.slots[kind] ?? favorites[kind]!).filter(names.contains)
        func set(_ s: [String]) { store.p.slots[kind] = s }
        return Section {
            Picker("Files", selection: $kind) { ForEach(Kind.allCases, id: \.self) { Text($0.rawValue).tag($0) } }.pickerStyle(.segmented)
            OrderedNames(names: slots, set: set)
            HStack {
                Menu("Add Format") {
                    ForEach(candidates.filter { !slots.contains($0.name) }) { p in Button(p.name) { set(slots + [p.name]) } }
                }.fixedSize().disabled(slots.count >= 8)
                Spacer()
                Button("Reset") { store.p.slots[kind] = nil }.disabled(store.p.slots[kind] == nil)
            }
        } header: { Text("Convert ring") } footer: {
            Text("Up to 8, clockwise from the top. Formats a file can't be converted to are skipped.").font(.caption).foregroundStyle(.secondary)
        }
    }

    private func deleteRing(_ r: CustomRing) {
        store.p.rings?.removeAll { $0.id == r.id }
        store.p.start = store.p.start?.filter { $0.value != r.id }
    }

    /// Any change to the look makes it a custom theme. A write of the same value isn't one: the Center field writes its
    /// text back when Settings opens, which used to turn a built-in theme into "Custom" untouched.
    private func edit(_ f: (inout WheelTheme) -> Void) {
        var t = store.p.theme; f(&t)
        guard t != store.p.theme else { return }
        t.name = "Custom"; store.p.theme = t
    }
    private func bind<T>(_ k: WritableKeyPath<WheelTheme, T>) -> Binding<T> {
        Binding(get: { store.p.theme[keyPath: k] }, set: { v in edit { $0[keyPath: k] = v } })
    }

    private func reloadPreview() {
        preview.load([URL(fileURLWithPath: "/preview." + sample[kind]!)])
        preview.hovered = preview.choices.first
        preview.shown = true
    }
}

struct PresetSettings: View {
    @ObservedObject private var store = PrefStore.shared
    @State private var editing: Preset?

    var body: some View {
        Form {
            Section {
                if store.p.custom.isEmpty { Text("No custom presets yet.").foregroundStyle(.secondary) }
                ForEach(store.p.custom) { p in
                    HStack {
                        VStack(alignment: .leading) {
                            Text(p.name)
                            Text(summary(p)).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("Edit") { editing = p }
                        Button { delete(p) } label: { Image(systemName: "trash") }.buttonStyle(.borderless)
                    }
                }
                Button("New Preset…") { editing = Preset(name: "", ext: "png", kind: .image) }
            } header: { Text("Custom presets") } footer: {
                Text("They show up in the main window's menus and can be put on the wheel in the Wheel tab. Files already in the list keep the presets they had.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .settingsPage(height: 420)
        .sheet(item: $editing) { PresetEditor(original: $0) }
    }

    /// Also takes it off the wheel and out of any ring, so it doesn't linger as a dead name there.
    private func delete(_ p: Preset) {
        store.p.custom.removeAll { $0.name == p.name }
        store.p.slots = store.p.slots.mapValues { $0.filter { $0 != p.name } }
        store.p.rings = store.p.rings?.map { var r = $0; r.presets.removeAll { $0 == p.name }; return r }
    }

    private func summary(_ p: Preset) -> String {
        var s = ["\(p.kind.rawValue) → \(p.ext.uppercased())"]
        if let q = p.quality { s.append("quality \(q)") }
        if let w = p.maxWidth, let h = p.maxHeight, w == h, p.kind == .image { s.append("≤ \(w) px") }
        else {
            if let w = p.maxWidth { s.append("≤ \(w) px wide") }
            if let h = p.maxHeight { s.append("≤ \(h) px tall") }
        }
        if let k = p.kbps { s.append("\(k) kbps") }
        if let c = p.crf { s.append("CRF \(c)") }
        if let f = p.fps { s.append("\(f) fps") }
        if p.strip { s.append("no metadata") }
        return s.joined(separator: " · ")
    }
}

struct PresetEditor: View {
    let original: Preset
    @State private var p: Preset
    @ObservedObject private var store = PrefStore.shared
    @Environment(\.dismiss) private var dismiss
    init(original: Preset) { self.original = original; _p = State(initialValue: original) }

    /// Every output format, once per kind.
    private static let formats: [(kind: Kind, ext: String)] = {
        var seen = Set<String>()
        return builtIn.compactMap { seen.insert("\($0.kind)/\($0.ext)").inserted ? ($0.kind, $0.ext) : nil }
    }()

    private var name: String { p.name.trimmingCharacters(in: .whitespaces) }
    private var taken: Bool {
        (builtIn + optimizePresets + resizePresets + backgroundPresets + store.p.custom).contains { $0.name == name && $0.name != original.name }
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    TextField("Name", text: $p.name)
                    Picker("Format", selection: Binding(get: { "\(p.kind)/\(p.ext)" }, set: { tag in
                        // A new format starts from scratch: settings for the old one may not apply.
                        if let f = Self.formats.first(where: { "\($0.kind)/\($0.ext)" == tag }) { p = Preset(name: p.name, ext: f.ext, kind: f.kind) }
                    })) {
                        ForEach(Kind.allCases, id: \.self) { k in
                            Section(k.rawValue) {
                                ForEach(Self.formats.filter { $0.kind == k }, id: \.ext) { f in
                                    Text(k == .video && f.ext == "gif" ? "GIF (animated)" : f.ext.uppercased()).tag("\(f.kind)/\(f.ext)")
                                }
                            }
                        }
                    }
                } footer: {
                    if taken { Text("Another preset already has this name.").font(.caption).foregroundStyle(.red) }
                }
                Section {
                    knobs
                } footer: { Text("Leave a box empty to use the default.").font(.caption).foregroundStyle(.secondary) }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save() }.keyboardShortcut(.defaultAction).disabled(name.isEmpty || taken)
            }.padding()
        }
        .frame(width: 420, height: 440)
    }

    @ViewBuilder private var knobs: some View {
        switch p.kind {
        case .image:
            if ["jpg", "webp", "avif"].contains(p.ext) { number("Quality (1–100)", $p.quality) }
            // magick only resizes when both are set, so images get one "longest side" box.
            number("Longest side (px)", Binding(get: { p.maxWidth }, set: { p.maxWidth = $0; p.maxHeight = $0 }))
            Toggle("Strip metadata", isOn: $p.strip)
        case .audio:
            if ["mp3", "m4a", "opus", "ogg"].contains(p.ext) { number("Bitrate (kbps)", $p.kbps) }
            Toggle("Strip metadata", isOn: $p.strip)
        case .video:
            if p.ext == "gif" {
                number("Frame rate (fps)", $p.fps)
                number("Max width (px)", $p.maxWidth)
            } else {
                number(p.ext == "webm" ? "Quality, CRF (0–63, lower is better)" : "Quality, CRF (0–51, lower is better)", $p.crf)
                number("Max width (px)", $p.maxWidth)
                number("Max height (px)", $p.maxHeight)
                number("Frame rate (fps)", $p.fps)
            }
            Toggle("Strip metadata", isOn: $p.strip)
        case .document:
            Text("Documents have no settings beyond the format.").foregroundStyle(.secondary)
        }
    }

    private func number(_ label: String, _ v: Binding<Int?>) -> some View {
        TextField(label, value: v, format: .number.grouping(.never))
    }

    private func save() {
        func clamp(_ v: Int?, _ r: ClosedRange<Int>) -> Int? { v.map { min(max($0, r.lowerBound), r.upperBound) } }
        var q = p
        q.name = name
        q.quality = clamp(q.quality, 1...100)
        q.kbps = clamp(q.kbps, 8...512)
        q.crf = clamp(q.crf, 0...(q.ext == "webm" ? 63 : 51))
        q.fps = clamp(q.fps, 1...120)
        let side = q.ext == "ico" ? 16...256 : 16...16384
        q.maxWidth = clamp(q.maxWidth, side); q.maxHeight = clamp(q.maxHeight, side)
        if let i = store.p.custom.firstIndex(where: { $0.name == original.name }), !original.name.isEmpty { store.p.custom[i] = q }
        else { store.p.custom.append(q) }
        // Keep it on the wheel under its new name.
        if !original.name.isEmpty, original.name != q.name {
            func rename(_ s: [String]) -> [String] { s.map { $0 == original.name ? q.name : $0 } }
            for (k, s) in store.p.slots { store.p.slots[k] = rename(s) }
            store.p.rings = store.p.rings?.map { var r = $0; r.presets = rename(r.presets); return r }
        }
        dismiss()
    }
}

struct RingEditor: View {
    @State var ring: CustomRing
    @ObservedObject private var store = PrefStore.shared
    @Environment(\.dismiss) private var dismiss

    private var name: String { ring.name.trimmingCharacters(in: .whitespaces) }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    TextField("Name", text: $ring.name)
                    Picker("Icon", selection: $ring.symbol) {
                        ForEach(CustomRing.symbols, id: \.self) { Image(systemName: $0).tag($0) }
                    }
                }
                Section {
                    if ring.presets.isEmpty { Text("Add the tools you want on this ring.").foregroundStyle(.secondary) }
                    OrderedNames(names: ring.presets) { ring.presets = $0 }
                    Menu("Add Tool") {
                        ForEach(ringCandidates(), id: \.0) { group, ps in
                            Section(group) {
                                ForEach(ps.filter { !ring.presets.contains($0.name) }) { p in Button(p.name) { ring.presets.append(p.name) } }
                            }
                        }
                    }.fixedSize().disabled(ring.presets.count >= 8)
                } header: { Text("Tools") } footer: {
                    Text("Up to 8, clockwise from the top. Mix formats for different kinds of files: each file only sees the ones that work for it.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save() }.keyboardShortcut(.defaultAction).disabled(name.isEmpty || ring.presets.isEmpty)
            }.padding()
        }
        .frame(width: 420, height: 480)
    }

    private func save() {
        var r = ring; r.name = name
        var rings = store.p.rings ?? []
        if let i = rings.firstIndex(where: { $0.id == r.id }) { rings[i] = r } else { rings.append(r) }
        store.p.rings = rings
        dismiss()
    }
}

struct CommandEditor: View {
    @State var command: WheelCommand
    @ObservedObject private var store = PrefStore.shared
    @Environment(\.dismiss) private var dismiss
    private let field = ChipFieldControl()

    private func trimmed(_ s: String) -> String { s.trimmingCharacters(in: .whitespacesAndNewlines) }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    TextField("Name", text: $command.name)
                    Picker("Icon", selection: $command.symbol) {
                        ForEach(WheelCommand.symbols, id: \.self) { Image(systemName: $0).tag($0) }
                    }
                    Picker("Runs on", selection: Binding(get: { command.on }, set: { command.runsOn = $0; command.forLinks = $0 == .links })) {
                        ForEach(RunsOn.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                    }.pickerStyle(.segmented)
                    if command.on == .files {
                        TextField("File types", text: Binding(get: { command.types ?? "" }, set: { command.types = $0 }), prompt: Text("pdf, png (empty: any file)"))
                    }
                }
                Section {
                    ChipField(text: $command.line, control: field)
                        .frame(height: 72)
                    HStack(spacing: 6) {
                        Text("Insert").foregroundStyle(.secondary)
                        ForEach(Token.offered(command.on), id: \.self) { t in
                            Button { field.insert(t) } label: {
                                HStack(spacing: 3) { Image(systemName: "plus"); Text(t.label) }.font(.system(size: 11, weight: .semibold)).foregroundStyle(.white)
                                    .padding(.horizontal, 8).padding(.vertical, 3).background(Capsule().fill(Color(nsColor: t.color)))
                            }.buttonStyle(.plain)
                        }
                    }
                } header: { Text("Command") } footer: {
                    Text(footer).font(.caption).foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save() }.keyboardShortcut(.defaultAction).disabled(trimmed(command.name).isEmpty || trimmed(command.line).isEmpty)
            }.padding()
        }
        .frame(width: 480, height: 440)
    }

    private var footer: String {
        let what = switch command.on {
        case .files: "File is the dragged file; the command runs once for each, in the file's folder."
        case .folders: "Folder is the dragged folder, Files in Folder the files directly inside it; the command runs once for each folder, in it."
        case .links: "Link is the dragged link; the command runs once for each, in your home folder."
        }
        return what + " Chips are quoted for you: don't put quotes around them. Runs in zsh. If it fails, Lemon shows what it printed."
    }

    private func save() {
        var c = command; c.name = trimmed(c.name); c.line = trimmed(c.line)
        var all = store.p.commands ?? []
        if let i = all.firstIndex(where: { $0.id == c.id }) { all[i] = c } else { all.append(c) }
        store.p.commands = all
        dismiss()
    }
}

/// Preset names in wheel order, each with move up / move down / remove.
struct OrderedNames: View {
    let names: [String]
    let set: ([String]) -> Void
    var body: some View {
        ForEach(Array(names.enumerated()), id: \.element) { i, name in
            HStack {
                Text(name)
                Spacer()
                Button { var s = names; s.swapAt(i, i - 1); set(s) } label: { Image(systemName: "chevron.up") }.disabled(i == 0)
                Button { var s = names; s.swapAt(i, i + 1); set(s) } label: { Image(systemName: "chevron.down") }.disabled(i == names.count - 1)
                Button { set(names.filter { $0 != name }) } label: { Image(systemName: "minus.circle") }
            }.buttonStyle(.borderless)
        }
    }
}
