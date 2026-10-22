import Foundation

enum Kind: String, CaseIterable { case image = "Images", audio = "Audio", video = "Video", document = "Documents" }
/// Background removal engines. Vision is built in; BiRefNet (Lite / Full, downloaded in Settings) copes with large images and fine edges.
enum BGEngine: Hashable { case vision, lite, full }
enum Strength: String { case lossless = "Lossless", balanced = "Balanced", smaller = "Smaller" }

let kindOf: [String: Kind] = {
    var m: [String: Kind] = [:]
    for e in "png jpg jpeg webp avif heic heif gif bmp tif tiff ico".split(separator: " ") { m[String(e)] = .image }
    for e in "mp3 wav flac m4a aac ogg opus wma aiff".split(separator: " ") { m[String(e)] = .audio }
    for e in "mp4 mkv webm mov avi wmv m4v flv".split(separator: " ") { m[String(e)] = .video }
    for e in "pdf docx doc odt rtf md txt html htm epub".split(separator: " ") { m[String(e)] = .document }
    return m
}()

struct Preset: Identifiable, Hashable {
    var id: String { name }
    let name: String
    let ext: String            // "" = Optimize (same format as the source)
    let kind: Kind
    var strength: Strength? = nil
    var quality: Int? = nil
    var maxWidth: Int? = nil
    var maxHeight: Int? = nil
    var kbps: Int? = nil
    var crf: Int? = nil
    var fps: Int? = nil
    var strip = false
    var removeBackground: BGEngine? = nil   // always writes a transparent PNG
}

private func p(_ name: String, _ ext: String, _ kind: Kind, _ f: (inout Preset) -> Void = { _ in }) -> Preset {
    var x = Preset(name: name, ext: ext, kind: kind); f(&x); return x
}

let builtIn: [Preset] = [
    p("PNG", "png", .image), p("JPEG – High quality", "jpg", .image) { $0.quality = 90 },
    p("WebP – Web optimized", "webp", .image) { $0.quality = 80; $0.maxWidth = 1920; $0.maxHeight = 1920; $0.strip = true },
    p("AVIF – Small", "avif", .image) { $0.quality = 60 }, p("GIF", "gif", .image), p("TIFF", "tiff", .image),
    p("BMP", "bmp", .image), p("Icon (256 px)", "ico", .image) { $0.maxWidth = 256; $0.maxHeight = 256 },
    p("PDF", "pdf", .image),

    p("MP3 – 320 kbps", "mp3", .audio) { $0.kbps = 320 }, p("MP3 – 192 kbps", "mp3", .audio) { $0.kbps = 192 },
    p("AAC (M4A) – 256 kbps", "m4a", .audio) { $0.kbps = 256 }, p("Opus – 128 kbps", "opus", .audio) { $0.kbps = 128 },
    p("Ogg Vorbis – 192 kbps", "ogg", .audio) { $0.kbps = 192 }, p("FLAC – Lossless", "flac", .audio),
    p("WAV – Uncompressed", "wav", .audio), p("AIFF – Uncompressed", "aiff", .audio),

    p("MP4 (H.264)", "mp4", .video) { $0.crf = 23 }, p("MP4 – 1080p", "mp4", .video) { $0.crf = 23; $0.maxHeight = 1080 },
    p("MP4 – 720p small", "mp4", .video) { $0.crf = 27; $0.maxHeight = 720 }, p("WebM (VP9)", "webm", .video) { $0.crf = 32 },
    p("MKV (H.264)", "mkv", .video) { $0.crf = 21 }, p("QuickTime MOV", "mov", .video) { $0.crf = 21 },
    p("Animated GIF", "gif", .video) { $0.fps = 12; $0.maxWidth = 480 },

    p("PDF", "pdf", .document), p("Word (DOCX)", "docx", .document), p("OpenDocument (ODT)", "odt", .document),
    p("Rich Text (RTF)", "rtf", .document), p("HTML page", "html", .document), p("Markdown", "md", .document),
    p("Plain text", "txt", .document), p("EPUB e-book", "epub", .document),
]

let optimizePresets: [Preset] = [Strength.lossless, .balanced, .smaller].map {
    Preset(name: "Optimize – \($0.rawValue)", ext: "", kind: .image, strength: $0)
}

let backgroundPresets = [
    Preset(name: "Remove background – Apple Vision", ext: "png", kind: .image, removeBackground: .vision),
    Preset(name: "Remove background – Lite", ext: "png", kind: .image, removeBackground: .lite),
    Preset(name: "Remove background – Full quality", ext: "png", kind: .image, removeBackground: .full),
]

let textutilIn: Set<String> = ["txt", "html", "htm", "rtf", "doc", "docx", "odt"]
let textutilOut: Set<String> = ["txt", "html", "rtf", "doc", "docx", "odt"]

func ext(of url: URL) -> String { url.pathExtension.lowercased() }

/// Presets that make sense for this source file and whose tool is installed.
func presets(for url: URL) -> [Preset] {
    let src = ext(of: url)
    guard let kind = kindOf[src] else { return [] }
    var out: [Preset] = []
    func has(_ t: String) -> Bool { Tools.find(t) != nil }
    switch kind {
    case .image:
        if has("magick") { out = builtIn.filter { $0.kind == .image && $0.ext != src && !($0.ext == "jpg" && src == "jpeg") && !($0.ext == "tiff" && src == "tif") } }
    case .audio:
        if has("ffmpeg") { out = builtIn.filter { $0.kind == .audio && $0.ext != src } }
    case .video:
        if has("ffmpeg") {
            out = builtIn.filter { $0.kind == .video && !($0.ext == src && $0.crf == nil) }
            out += builtIn.filter { $0.kind == .audio && ["mp3", "m4a", "opus", "flac", "wav"].contains($0.ext) }
        }
    case .document:
        out = builtIn.filter { $0.kind == .document && $0.ext != src && docRoute(src, $0.ext) != nil }
    }
    // BiRefNet presets stay hidden until their model is downloaded.
    if kind == .image { out += backgroundPresets.filter { BiRefNet.of($0.removeBackground!)?.installed ?? true } }
    if optimizable(src) { out = optimizePresets.filter { $0.strength != .lossless || ["png", "gif"].contains(src) } + out }
    return out
}

func optimizable(_ src: String) -> Bool {
    switch src {
    case "jpg", "jpeg", "png", "gif", "webp": return Tools.find("magick") != nil
    case "mp4", "m4v", "mov", "mkv": return Tools.find("ffmpeg") != nil
    case "pdf": return Tools.find("gs") != nil
    default: return false
    }
}

enum DocTool { case textutil, pandoc, soffice }

func docRoute(_ src: String, _ dst: String) -> DocTool? {
    if textutilIn.contains(src) && textutilOut.contains(dst) { return .textutil }
    if dst == "pdf" { return Tools.find("soffice") != nil && src != "md" && src != "epub" ? .soffice : nil }
    return Tools.find("pandoc") != nil && !["doc", "pdf"].contains(src) ? .pandoc : nil
}
