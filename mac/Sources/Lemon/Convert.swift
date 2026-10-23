import Foundation
import CoreImage
import Vision

struct LemonError: Error { let message: String }

enum Tools {
    // GUI apps start with a bare PATH, so look in the Homebrew/MacPorts locations too.
    static let dirs = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", "/usr/bin", "/Applications/LibreOffice.app/Contents/MacOS"]
    private static let lock = NSLock()
    nonisolated(unsafe) private static var cache: [String: (path: String?, at: Date)] = [:]

    /// Listing presets asks this many times per file, so answers are cached for a few seconds;
    /// a tool installed while Lemon runs still shows up right after.
    static func find(_ name: String) -> String? {
        lock.lock(); defer { lock.unlock() }
        if let hit = cache[name], Date().timeIntervalSince(hit.at) < 5 { return hit.path }
        let path = dirs.map { "\($0)/\(name)" }.first { FileManager.default.isExecutableFile(atPath: $0) }
        cache[name] = (path, Date())
        return path
    }
    static func need(_ name: String) throws -> String {
        guard let p = find(name) else { throw LemonError(message: "\(name) isn't installed (brew install \(name == "magick" ? "imagemagick" : name == "gs" ? "ghostscript" : name == "soffice" ? "--cask libreoffice" : name))") }
        return p
    }
}

private final class Box: @unchecked Sendable {
    let lock = NSLock(); var err = Data(), out = Data(); var proc: Process?
    /// stderr is only kept for the error message, and progress meters can write megabytes of it.
    private static let errTail = 64 << 10
    private var partial = [Data(), Data()]
    /// Stores a chunk and returns the complete lines in it; a line cut off at the chunk's end waits for the next one.
    /// Progress meters redraw with \r, so that ends a line too. `final` flushes the leftover.
    func append(_ d: Data, err isErr: Bool, final: Bool = false) -> [String] {
        lock.lock(); defer { lock.unlock() }
        if isErr { err.append(d); if err.count > 2 * Self.errTail { err = err.suffix(Self.errTail) } } else { out.append(d) }
        let i = isErr ? 1 : 0
        partial[i].append(d)
        guard var text = String(data: partial[i], encoding: .utf8) else { return [] }
        var keep = ""
        if !final, let last = text.lastIndex(where: { $0 == "\n" || $0 == "\r" }) {
            keep = String(text[text.index(after: last)...]); text = String(text[...last])
        } else if !final { return [] }
        partial[i] = Data(keep.utf8)
        return text.split(whereSeparator: { $0 == "\n" || $0 == "\r" }).map(String.init)
    }
    func text(err isErr: Bool) -> String { lock.lock(); defer { lock.unlock() }; return String(data: isErr ? err : out, encoding: .utf8) ?? "" }
}

/// Runs a tool; stdout lines go to onLine, stderr lines to onErr (and are kept for the error message).
/// Cancelling the task kills the process.
@discardableResult
func run(_ exe: String, _ args: [String], onLine: (@Sendable (String) -> Void)? = nil, onErr: (@Sendable (String) -> Void)? = nil) async throws -> String {
    // Cancelled before launch: the handler below can't stop a process that hasn't started, so it would run to the end.
    try Task.checkCancellation()
    let box = Box(), p = Process(), out = Pipe(), err = Pipe()
    p.executableURL = URL(fileURLWithPath: exe); p.arguments = args
    p.standardOutput = out; p.standardError = err; p.standardInput = FileHandle.nullDevice
    box.proc = p
    // Read each pipe until EOF, so the last lines (often the final progress line) arrive, in order, before we return.
    let eof = DispatchGroup()
    for (pipe, isErr) in [(out, false), (err, true)] {
        eof.enter()
        pipe.fileHandleForReading.readabilityHandler = { h in
            let d = h.availableData
            if d.isEmpty { h.readabilityHandler = nil; eof.leave(); return }
            box.append(d, err: isErr).forEach { (isErr ? onErr : onLine)?($0) }
        }
    }
    let status: Int32 = try await withTaskCancellationHandler {
        try await withCheckedThrowingContinuation { c in
            p.terminationHandler = { c.resume(returning: $0.terminationStatus) }
            do { try p.run() } catch { c.resume(throwing: error) }
        }
    } onCancel: { if box.proc?.isRunning == true { box.proc?.terminate() } }   // terminate() before launch raises
    // A child the tool left running can hold a pipe open, so don't wait for EOF forever.
    await withCheckedContinuation { c in DispatchQueue.global().async { _ = eof.wait(timeout: .now() + 2); c.resume() } }
    box.append(Data(), err: false, final: true).forEach { onLine?($0) }
    box.append(Data(), err: true, final: true).forEach { onErr?($0) }
    try Task.checkCancellation()
    guard status == 0 else {
        let lines = box.text(err: true).split(whereSeparator: { $0 == "\n" || $0 == "\r" }).map(String.init)
        throw LemonError(message: lines.last(where: { !$0.isEmpty && !$0.contains("% complete") }) ?? "\((exe as NSString).lastPathComponent) failed (exit \(status))")
    }
    return box.text(err: false)
}

/// Keeps progress monotonic and reports only steps of at least 1%, not one per scanline.
final class Meter: @unchecked Sendable {
    private let lock = NSLock(); private var best = 0.0
    func raise(to p: Double) -> Double? {
        lock.lock(); defer { lock.unlock() }
        guard p > best + 0.01 || (p >= 1 && best < 1) else { return nil }
        best = p; return p
    }
}

enum Outcome { case done(URL), alreadyOptimized }

/// Moves the result to name.ext, or name (1).ext, ... – never overwrites. FileManager.moveItem checks then renames, so
/// two parallel jobs that want the same name can both pass the check and one silently replaces the other (seen in
/// testing); the kernel's exclusive rename, or an exclusive copy across volumes, claims the name atomically.
func moveToUnique(_ tmp: URL, dir: URL, base: String, ext: String) throws -> URL {
    for n in 0..<10_000 {
        let u = dir.appendingPathComponent(n == 0 ? "\(base).\(ext)" : "\(base) (\(n)).\(ext)")
        if renamex_np(tmp.path, u.path, UInt32(RENAME_EXCL)) == 0 { return u }
        if errno == EEXIST { continue }
        guard errno == EXDEV || errno == ENOTSUP else { break }   // other volume, or a file system without RENAME_EXCL
        if copyfile(tmp.path, u.path, nil, copyfile_flags_t(COPYFILE_ALL | COPYFILE_EXCL)) == 0 { return u }
        if errno == EEXIST { continue }
        break
    }
    throw LemonError(message: "Couldn't save \(base).\(ext): \(String(cString: strerror(errno)))")
}

/// A private folder for a job's scratch files: on the source's volume, so the final move is a rename, but outside the
/// user's folder (nothing is left there if Lemon dies mid-job) and free of % (ImageMagick expands %d in output names).
private func scratchFolder(near dir: URL) -> URL {
    let fm = FileManager.default
    if let u = try? fm.url(for: .itemReplacementDirectory, in: .userDomainMask, appropriateFor: dir, create: true), !u.path.contains("%") { return u }
    let u = fm.temporaryDirectory.appendingPathComponent("Lemon-\(UUID().uuidString)")
    try? fm.createDirectory(at: u, withIntermediateDirectories: true)
    return u
}

/// CIContext is expensive to create and safe to share across threads.
let sharedCIContext = CIContext()

func convert(_ src: URL, _ preset: Preset, progress: @escaping @Sendable (Double?) -> Void) async throws -> Outcome {
    let fm = FileManager.default
    let srcExt = ext(of: src), optimize = preset.strength != nil
    let outExt = optimize ? srcExt : preset.ext
    let suffix = optimize ? " (optimized)" : preset.removeBackground != nil ? " (no background)" : ""
    let dir = src.deletingLastPathComponent()
    let scratch = scratchFolder(near: dir)
    let tmp = scratch.appendingPathComponent("lemon-\(UUID().uuidString).\(outExt)")
    defer { try? fm.removeItem(at: scratch) }

    if let strength = preset.strength {
        try await optimizeFile(src, tmp, srcExt, strength, progress)
        let before = (try? src.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        let after = (try? tmp.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? Int.max
        guard after > 0, Double(after) <= Double(before) * 0.9 else { return .alreadyOptimized }
    } else if let engine = preset.removeBackground {
        // Seconds of blocking work (and a wait for the one-at-a-time model lock), so on a thread of its own rather than
        // one of the few the async runtime shares between every job.
        try await offPool {
            if let model = BiRefNet.of(engine) {
                guard model.installed else { throw LemonError(message: "Download \(model.name) in Settings first.") }
                try BiRefNetRunner.removeBackground(model, src, tmp, progress)
            } else {
                // Vision chokes on some large images; a downloaded BiRefNet model takes over.
                do { try removeBackground(src, tmp, progress) }
                catch {
                    guard let fallback = [BGEngine.lite, .full].compactMap(BiRefNet.of).first(where: \.installed) else { throw error }
                    try BiRefNetRunner.removeBackground(fallback, src, tmp, progress)
                }
            }
        }
        // The thread above can't be interrupted; if the job was cancelled meanwhile, drop the result instead of saving it.
        try Task.checkCancellation()
    } else {
        switch kindOf[srcExt]! {
        case .image: try await imageConvert(src, tmp, preset, progress)
        case .audio, .video: try await ffmpegConvert(src, tmp, preset, progress)
        case .document: try await docConvert(src, tmp, srcExt, preset.ext)
        }
    }
    let base = src.deletingPathExtension().lastPathComponent + suffix
    return .done(try moveToUnique(tmp, dir: dir, base: base, ext: outExt))
}

/// Runs blocking work on a GCD thread and awaits it.
private func offPool(_ work: @escaping @Sendable () throws -> Void) async throws {
    try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
        DispatchQueue.global(qos: .userInitiated).async { c.resume(with: Result { try work() }) }
    }
}

// MARK: images

/// Runs magick with -monitor and turns its per-phase meters into one 0...1 progress:
/// loading is the first 40%, processing the next 30%, saving/encoding the rest.
private func runMagick(_ m: String, _ args: [String], _ progress: @escaping @Sendable (Double?) -> Void) async throws {
    let meter = Meter()
    try await run(m, ["-monitor"] + args, onErr: { line in
        guard let r = line.range(of: #"(\d+) of (\d+)"#, options: .regularExpression) else { return }
        let nums = line[r].split(separator: " ").compactMap { Double($0) }
        guard nums.count == 2, nums[1] > 0 else { return }
        let f = min(nums[0] / nums[1], 1)
        let lower = line.lowercased()
        let p = lower.hasPrefix("load") ? f * 0.4 : (lower.hasPrefix("save") || lower.hasPrefix("encode")) ? 0.7 + f * 0.3 : 0.4 + f * 0.3
        if let next = meter.raise(to: p) { progress(next) }
    })
}

private func imageConvert(_ src: URL, _ dst: URL, _ p: Preset, _ progress: @escaping @Sendable (Double?) -> Void) async throws {
    let m = try Tools.need("magick")
    let multi = ["gif", "webp", "pdf", "tiff"].contains(p.ext)   // formats that keep animation / pages
    var a = [multi ? src.path : src.path + "[0]", "-auto-orient"]
    if p.ext == "jpg" || p.ext == "bmp" { a += ["-background", "white", "-alpha", "remove", "-alpha", "off"] }
    if p.strip { a.append("-strip") }
    if let w = p.maxWidth, let h = p.maxHeight { a += ["-resize", "\(w)x\(h)>"] }
    if let q = p.quality { a += ["-quality", "\(q)"] }
    try await runMagick(m, a + [dst.path], progress)
}

// MARK: audio / video

private func ffmpegConvert(_ src: URL, _ dst: URL, _ p: Preset, _ progress: @escaping @Sendable (Double?) -> Void) async throws {
    let ff = try Tools.need("ffmpeg")
    var a = ["-hide_banner", "-nostdin", "-y", "-i", src.path, "-sn", "-dn"]
    let audioOnly = ["mp3", "m4a", "ogg", "opus", "flac", "wav", "aiff"].contains(p.ext)
    func audio(_ t: String, _ fallback: Int) -> [String] {
        let b = "\(p.kbps ?? fallback)k"
        switch t {
        case "mp3": return ["-c:a", "libmp3lame", "-b:a", b]
        case "m4a": return ["-c:a", "aac", "-b:a", b]
        case "ogg": return ["-c:a", "libvorbis", "-b:a", b]
        case "opus": return ["-c:a", "libopus", "-b:a", b]
        case "flac": return ["-c:a", "flac"]
        case "wav": return ["-c:a", "pcm_s16le"]
        default: return ["-c:a", "pcm_s16be"]
        }
    }
    if audioOnly { a += ["-vn"] + audio(p.ext, 192) }
    else if p.ext == "gif" {
        let w = p.maxWidth.map { "'min(iw,\($0))'" } ?? "iw"
        a += ["-vf", "fps=\(p.fps ?? 12),scale=w=\(w):h=-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", "-an", "-loop", "0"]
    } else {
        // x264/VP9 with yuv420p need even dimensions, so always scale.
        let w = p.maxWidth.map { "'min(iw,\($0))'" } ?? "iw", h = p.maxHeight.map { "'min(ih,\($0))'" } ?? "ih"
        a += ["-vf", "scale=w=\(w):h=\(h):force_original_aspect_ratio=decrease:force_divisible_by=2", "-pix_fmt", "yuv420p"]
        if let f = p.fps { a += ["-r", "\(f)"] }
        if p.ext == "webm" { a += ["-c:v", "libvpx-vp9", "-crf", "\(p.crf ?? 32)", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"] + audio("opus", 128) }
        else { a += ["-c:v", "libx264", "-crf", "\(p.crf ?? 23)", "-preset", "medium"] + audio("m4a", 160) + (["mp4", "mov"].contains(p.ext) ? ["-movflags", "+faststart"] : []) }
    }
    if p.strip { a += ["-map_metadata", "-1"] }
    try await runFfmpeg(ff, a + ["-progress", "pipe:1", "-stats_period", "0.25", "-nostats", dst.path], src, progress)
}

private func runFfmpeg(_ ff: String, _ args: [String], _ src: URL, _ progress: @escaping @Sendable (Double?) -> Void) async throws {
    var probed: Double?
    if let probe = Tools.find("ffprobe") {
        probed = Double((try? await run(probe, ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", src.path]))?.trimmingCharacters(in: .whitespacesAndNewlines) ?? "")
    }
    let duration = probed, meter = Meter()
    try await run(ff, args, onLine: { line in
        // out_time_us is microseconds (out_time_ms is too, despite its name). It can dip near the end, hence the meter.
        if let d = duration, d > 0, line.hasPrefix("out_time_us="), let us = Double(line.dropFirst(12)),
           let p = meter.raise(to: min(max(us / 1e6 / d, 0), 1)) { progress(p) }
    })
}

// MARK: documents

private func docConvert(_ src: URL, _ dst: URL, _ s: String, _ t: String) async throws {
    switch docRoute(s, t) {
    case .textutil: try await run("/usr/bin/textutil", ["-convert", t, "-output", dst.path, src.path])
    case .pandoc: try await run(try Tools.need("pandoc"), [src.path, "-s", "-o", dst.path])
    case .soffice:
        let out = dst.deletingLastPathComponent().appendingPathComponent("soffice-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: out) }
        // LibreOffice allows one process per profile: a second one (a parallel job, or LibreOffice open on screen) hands
        // the file to the first and exits without converting. So each concurrent job borrows a profile of its own.
        let slot = SofficeProfiles.take(); defer { SofficeProfiles.give(slot) }
        try await run(try Tools.need("soffice"), ["-env:UserInstallation=" + slot.absoluteString, "--headless", "--convert-to", t, "--outdir", out.path, src.path])
        let made = out.appendingPathComponent(src.deletingPathExtension().lastPathComponent + "." + t)
        try FileManager.default.moveItem(at: made, to: dst)
    case nil: throw LemonError(message: "Can't convert .\(s) to .\(t) with the installed tools.")
    }
}

/// LibreOffice profiles, one per concurrent conversion, kept for the session (creating one costs a few seconds).
private enum SofficeProfiles {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var free: [URL] = [], made = 0
    static func take() -> URL {
        lock.lock(); defer { lock.unlock() }
        if let u = free.popLast() { return u }
        made += 1
        return FileManager.default.temporaryDirectory.appendingPathComponent("Lemon-soffice-\(made)", isDirectory: true)
    }
    static func give(_ u: URL) { lock.lock(); free.append(u); lock.unlock() }
}

// MARK: optimize

private func optimizeFile(_ src: URL, _ dst: URL, _ s: String, _ strength: Strength, _ progress: @escaping @Sendable (Double?) -> Void) async throws {
    let smaller = strength == .smaller
    switch s {
    case "mp4", "m4v", "mov", "mkv":
        let ff = try Tools.need("ffmpeg")
        // H.264 re-encode only helps non-HEVC/AV1/VP9, 8-bit 4:2:0 sources; anything else is left alone.
        if let probe = Tools.find("ffprobe") {
            let info = try await run(probe, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,pix_fmt", "-of", "csv=p=0", src.path])
            let f = info.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: ",")
            if f.count == 2, ["hevc", "av1", "vp9"].contains(String(f[0])) || f[1] != "yuv420p" { try Data().write(to: dst); return }
        }
        // H.264 in 4:2:0 needs even dimensions; odd-sized captures would otherwise fail the encode.
        var a = ["-hide_banner", "-nostdin", "-y", "-i", src.path, "-map", "0:v:0", "-map", "0:a?", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                 "-c:v", "libx264", "-crf", smaller ? "28" : "23", "-preset", "slow", "-c:a", "copy"]
        if s == "mkv" { a += ["-map", "0:s?", "-c:s", "copy", "-map", "0:t?", "-c:t", "copy"] } else { a += ["-sn", "-movflags", "+faststart"] }
        try await runFfmpeg(ff, a + ["-progress", "pipe:1", "-stats_period", "0.25", "-nostats", dst.path], src, progress)
    case "pdf":
        let dpi = smaller ? "150" : "200", gs = try Tools.need("gs")
        // Ghostscript prints "Page N" per page; count the pages first so that becomes a fraction.
        let pages = Double((try? await run(gs, ["-q", "-dNODISPLAY", "--permit-file-read=" + src.path, "-c",
            "(\(src.path.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "(", with: "\\(").replacingOccurrences(of: ")", with: "\\)"))) (r) file runpdfbegin pdfpagecount = quit"]))?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? "") ?? 0
        try await run(gs, ["-dNOPAUSE", "-dBATCH", "-dSAFER", "-sDEVICE=pdfwrite", smaller ? "-dPDFSETTINGS=/ebook" : "-dPDFSETTINGS=/printer",
            "-dDownsampleColorImages=true", "-dColorImageResolution=" + dpi, "-dColorImageDownsampleThreshold=1.5",
            "-dDownsampleGrayImages=true", "-dGrayImageResolution=" + dpi, "-dGrayImageDownsampleThreshold=1.5",
            // Ghostscript reads % in the output name as a page-number format.
            "-sOutputFile=" + dst.path.replacingOccurrences(of: "%", with: "%%"), src.path], onLine: { line in
                if pages > 0, line.hasPrefix("Page "), let n = Double(line.dropFirst(5)) { progress(min(n / pages, 1)) }
            })
    default: try await optimizeImage(src, dst, s, strength, progress)
    }
}

/// Builds candidates, keeps the smallest one that still looks like the original (DSSIM threshold, like Windows Lemon).
private func optimizeImage(_ src: URL, _ dst: URL, _ s: String, _ strength: Strength, _ progress: @escaping @Sendable (Double?) -> Void) async throws {
    let m = try Tools.need("magick"), smaller = strength == .smaller, fm = FileManager.default
    // Progress by step: each candidate encode and each similarity check is one step.
    let steps = Double(s == "png" ? (strength == .lossless ? 1 : 3) : s == "gif" ? 1 : 2)
    var done = 0.0
    func step(_ sub: Double) { progress(min((done + sub) / steps, 1)) }
    func size(_ u: URL) -> Int { (try? u.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? Int.max }
    func make(_ args: [String]) async throws -> URL {
        let u = dst.deletingLastPathComponent().appendingPathComponent("candidate-\(UUID().uuidString).\(s)")
        let base = done
        try await runMagick(m, [src.path] + args + [u.path]) { p in progress(min((base + (p ?? 0)) / steps, 1)) }
        done += 1; step(0); return u
    }
    func looksSame(_ u: URL) async -> Bool {
        // `compare` prints the metric on stderr and exits 1 for "different", so go through sh to read it.
        let r = try? await run("/bin/sh", ["-c", "\"$0\" compare -metric DSSIM \"$1\" \"$2\" null: 2>&1; true", m, src.path, u.path])
        guard let v = r?.split(whereSeparator: { " (".contains($0) }).first.flatMap({ Double($0) }) else { return false }
        return v <= (smaller ? 0.025 : 0.01)
    }
    var cands: [URL] = [], lossy: [URL] = []
    defer { (cands + lossy).forEach { try? fm.removeItem(at: $0) } }
    switch s {
    case "png":
        // ImageMagick reads only an APNG's first frame; leave animated PNGs alone, as the Windows build does.
        if isAnimatedPNG(src) { try Data().write(to: dst); return }
        cands.append(try await make(["-define", "png:compression-level=9"]))
        if strength != .lossless { lossy.append(try await make(["-colors", "256", "-define", "png:compression-level=9"])) }
    case "gif": cands.append(try await make(["-layers", "optimize"]))
    case "webp": lossy.append(try await make(["-quality", smaller ? "70" : "80"]))
    default:   // jpg
        lossy.append(try await make(["-quality", smaller ? "75" : "85", "-interlace", "Plane"]))
    }
    var best = cands.min { size($0) < size($1) }
    for u in lossy where (best == nil || Double(size(u)) <= Double(size(best!)) * 0.9) && size(u) < size(src) {
        let same = await looksSame(u); done += 1; step(0)
        if same { best = u }   // ponytail: first passing lossy candidate wins; fine with ≤1 per format
    }
    guard let best else { try Data().write(to: dst); return }   // empty file = "nothing smaller"; the size check in convert() rejects it
    try? fm.removeItem(at: dst)
    try fm.copyItem(at: best, to: dst)
}

// MARK: remove background

/// Lifts the subject(s) with Vision, the same model as Photos' "Copy Subject", onto a transparent PNG.
private func removeBackground(_ src: URL, _ dst: URL, _ progress: @escaping @Sendable (Double?) -> Void) throws {
    progress(0.05)
    guard let image = CIImage(contentsOf: src, options: [.applyOrientationProperty: true]) else { throw LemonError(message: "Couldn't read the image.") }
    let handler = VNImageRequestHandler(ciImage: image)
    let request = VNGenerateForegroundInstanceMaskRequest()
    try handler.perform([request])
    progress(0.7)
    guard let result = request.results?.first, !result.allInstances.isEmpty else { throw LemonError(message: "No subject found to keep.") }
    let masked = try result.generateMaskedImage(ofInstances: result.allInstances, from: handler, croppedToInstancesExtent: false)
    let space = image.colorSpace ?? CGColorSpace(name: CGColorSpace.sRGB)!
    try sharedCIContext.writePNGRepresentation(of: CIImage(cvPixelBuffer: masked), to: dst, format: .RGBA8, colorSpace: space)
    progress(1)
}

/// An APNG has an acTL chunk before its first IDAT.
func isAnimatedPNG(_ url: URL) -> Bool {
    guard let h = try? FileHandle(forReadingFrom: url) else { return false }
    defer { try? h.close() }
    guard let sig = try? h.read(upToCount: 8), sig.count == 8 else { return false }
    while let head = try? h.read(upToCount: 8), head.count == 8 {
        let len = head.prefix(4).reduce(0) { $0 << 8 | UInt64($1) }
        switch String(decoding: head.suffix(4), as: UTF8.self) {
        case "acTL": return true
        case "IDAT", "IEND": return false
        default: guard (try? h.seek(toOffset: h.offsetInFile + len + 4)) != nil else { return false }   // skip data + CRC
        }
    }
    return false
}
