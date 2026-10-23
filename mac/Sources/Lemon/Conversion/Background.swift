import Foundation
import CoreImage
import CryptoKit
import OnnxRuntimeBindings

/// The BiRefNet ONNX models (MIT licence, onnx-community on Hugging Face), pinned to a commit and a SHA-256.
/// Same files as the Windows build (src/Lemon.Core/Models/BackgroundModel.cs); keep them in sync.
/// Too big to ship in the app, so they're downloaded on request into ~/Library/Application Support/Lemon/models.
struct BiRefNet {
    let engine: BGEngine, name: String, file: String, url: URL, sha256: String, bytes: Int64

    static let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Lemon/models")

    static let all = [
        BiRefNet(engine: .lite, name: "BiRefNet Lite", file: "birefnet-lite.onnx",
                 url: URL(string: "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/de15b22ba131738a16dff04aab8bdf8dc32e3ac1/onnx/model.onnx")!,
                 sha256: "5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333", bytes: 224_005_088),
        BiRefNet(engine: .full, name: "BiRefNet", file: "birefnet.onnx",
                 url: URL(string: "https://huggingface.co/onnx-community/BiRefNet-ONNX/resolve/534d3c82d3bb8b2f0867db6dfbc3a525b8e42f67/onnx/model.onnx")!,
                 sha256: "58f621f00f5d756097615970a88a791584600dcf7c45b18a0a6267535a1ebd3c", bytes: 972_666_916),
    ]

    static func of(_ e: BGEngine) -> BiRefNet? { all.first { $0.engine == e } }

    var path: URL { Self.folder.appendingPathComponent(file) }
    var installed: Bool { FileManager.default.fileExists(atPath: path.path) }

    /// Downloads, checks the hash and only then moves the model into place.
    func download(progress: @escaping @Sendable (Double) -> Void) async throws {
        let fm = FileManager.default
        try fm.createDirectory(at: Self.folder, withIntermediateDirectories: true)
        let (tmp, response) = try await URLSession.shared.download(from: url, delegate: ProgressWatcher(progress))
        defer { try? fm.removeItem(at: tmp) }
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw LemonError(message: "Download failed (\((response as? HTTPURLResponse)?.statusCode ?? 0)).") }
        guard try sha256(of: tmp) == sha256 else { throw LemonError(message: "The downloaded \(name) model is corrupt (hash mismatch). Try again.") }
        _ = try? fm.removeItem(at: path)
        try fm.moveItem(at: tmp, to: path)
    }

    private func sha256(of url: URL) throws -> String {
        let h = try FileHandle(forReadingFrom: url); defer { try? h.close() }
        var hash = SHA256()
        while let chunk = try h.read(upToCount: 8 << 20), !chunk.isEmpty { hash.update(data: chunk) }
        return hash.finalize().map { String(format: "%02x", $0) }.joined()
    }
}

private final class ProgressWatcher: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    let report: @Sendable (Double) -> Void
    var watch: NSKeyValueObservation?
    init(_ report: @escaping @Sendable (Double) -> Void) { self.report = report }
    func urlSession(_ session: URLSession, didCreateTask task: URLSessionTask) {
        watch = task.progress.observe(\.fractionCompleted) { [report] p, _ in report(p.fractionCompleted) }
    }
}

// MARK: running the model (port of BackgroundRemovalConverter.cs)

enum BiRefNetRunner {
    private static let size = 1024
    private static let mean: [Float] = [0.485, 0.456, 0.406], std: [Float] = [0.229, 0.224, 0.225]
    private static let lock = NSLock()
    /// One inference at a time: a run already uses every core, so parallel runs only add memory and contention.
    private static let runLock = NSLock()
    nonisolated(unsafe) private static var sessions: [String: ORTSession] = [:]
    nonisolated(unsafe) private static var env: ORTEnv?
    nonisolated(unsafe) private static var lastUse = Date()
    /// A loaded model plus its working memory is several GB (measured: Lite ~6.6 GB at 1024x1024), and Lemon lives in
    /// the menu bar, so let go of it soon after use. Reloading takes about a second.
    private static let idleRelease: TimeInterval = 20

    /// One cached CPU session per model.
    // ponytail: CPU only, ~3 s per image for Lite and ~7.5 s for Full on an M2 Pro. Core ML was measured on ORT 1.24.2
    // and lost: NeuralNetwork mode hangs compiling; ML Program needs the model patched (Convs without `pads`) and then
    // loads in 66-106 s and runs 4x slower than CPU. Retry when ORT's Core ML provider handles BiRefNet's dynamic shapes.
    private static func session(_ m: BiRefNet) throws -> ORTSession {
        lock.lock(); defer { lock.unlock() }
        lastUse = Date()
        if let s = sessions[m.file] { return s }
        if env == nil { env = try ORTEnv(loggingLevel: .warning) }
        let options = try ORTSessionOptions()
        // Performance cores only: the efficiency cores finish their share last and hold up every layer
        // (measured: Lite 3.1 s on the 6 P-cores vs 3.4 s with ORT's default of all 10 cores, 3.8 s on 12 threads).
        var pCores: Int32 = 0, len = MemoryLayout<Int32>.size
        if sysctlbyname("hw.perflevel0.physicalcpu", &pCores, &len, nil, 0) == 0, pCores > 0 { try options.setIntraOpNumThreads(pCores) }
        let s = try ORTSession(env: env!, modelPath: m.path.path, sessionOptions: options)
        sessions[m.file] = s
        scheduleRelease()
        return s
    }

    private static func scheduleRelease() {
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + idleRelease) {
            lock.lock(); defer { lock.unlock() }
            guard !sessions.isEmpty else { return }
            if Date().timeIntervalSince(lastUse) >= idleRelease { sessions.removeAll() } else {
                DispatchQueue.global(qos: .utility).async { scheduleRelease() }   // still in use; look again later
            }
        }
    }

    /// Drops a model's session, e.g. when its file is deleted.
    static func release(_ m: BiRefNet) { lock.lock(); sessions[m.file] = nil; lock.unlock() }

    /// How long the last run of each model took here, to pace the progress bar during inference.
    nonisolated(unsafe) private static var lastRun: [String: Double] = [:]

    static func removeBackground(_ m: BiRefNet, _ src: URL, _ dst: URL, _ progress: @escaping @Sendable (Double?) -> Void) throws {
        progress(0.02)
        guard let image = CIImage(contentsOf: src, options: [.applyOrientationProperty: true]) else { throw LemonError(message: "Couldn't read the image.") }
        let ctx = sharedCIContext, e = image.extent, n = size * size
        let srgb = CGColorSpace(name: CGColorSpace.sRGB)!

        // Squash to 1024x1024 over white (alpha removed), then normalize into a 1x3x1024x1024 tensor.
        let small = image.transformed(by: CGAffineTransform(translationX: -e.minX, y: -e.minY))
            .transformed(by: CGAffineTransform(scaleX: CGFloat(size) / e.width, y: CGFloat(size) / e.height))
            .composited(over: CIImage(color: .white)).cropped(to: CGRect(x: 0, y: 0, width: size, height: size))
        var rgba = [UInt8](repeating: 0, count: n * 4)
        ctx.render(small, toBitmap: &rgba, rowBytes: size * 4, bounds: small.extent, format: .RGBA8, colorSpace: srgb)
        var input = [Float](repeating: 0, count: n * 3)
        for i in 0..<n { for c in 0..<3 { input[c * n + i] = (Float(rgba[i * 4 + c]) / 255 - mean[c]) / std[c] } }

        progress(0.15)

        // The model is one opaque call, so the bar moves on the time the previous run took, stopping short of done.
        // The clock starts once it's this file's turn, not while it waits behind another one.
        runLock.lock()
        lock.lock(); let expected = lastRun[m.file] ?? (m.engine == .lite ? 5 : 10); lock.unlock()
        let start = Date(), timer = DispatchSource.makeTimerSource(queue: .global())
        timer.schedule(deadline: .now(), repeating: 0.25)
        timer.setEventHandler { progress(0.15 + 0.7 * min(Date().timeIntervalSince(start) / expected, 0.97)) }
        timer.resume()
        let out: [Float]
        do { defer { timer.cancel(); runLock.unlock() }; out = try predict(session(m), input) }
        // Idle counts from the end of a run: a long one (Full on a big queue) mustn't let the release drop the session
        // just before the next file needs it.
        lock.lock(); lastRun[m.file] = Date().timeIntervalSince(start); lastUse = Date(); lock.unlock()
        progress(0.86)
        guard out.count >= n else { throw LemonError(message: "Background removal failed: unexpected model output.") }

        // The export returns raw logits; squash them to 0..1 unless it already did.
        let mask = out.suffix(n), logits = mask.min()! < 0 || mask.max()! > 1
        let bytes = mask.map { v -> UInt8 in UInt8((min(max(logits ? 1 / (1 + exp(-v)) : v, 0), 1) * 255).rounded()) }
        // Read back with the same bitmap convention the input was rendered with, so rows line up.
        let maskImage = CIImage(bitmapData: Data(bytes), bytesPerRow: size, size: CGSize(width: size, height: size), format: .L8, colorSpace: nil)
            .transformed(by: CGAffineTransform(scaleX: e.width / CGFloat(size), y: e.height / CGFloat(size)))
            .transformed(by: CGAffineTransform(translationX: e.minX, y: e.minY))
        let cut = image.applyingFilter("CIBlendWithMask", parameters: [kCIInputBackgroundImageKey: CIImage.empty(), kCIInputMaskImageKey: maskImage]).cropped(to: e)
        try ctx.writePNGRepresentation(of: cut, to: dst, format: .RGBA8, colorSpace: image.colorSpace ?? srgb)
        progress(1)
    }

    private static func predict(_ s: ORTSession, _ input: [Float]) throws -> [Float] {
        let data = input.withUnsafeBufferPointer { NSMutableData(bytes: $0.baseAddress, length: $0.count * 4) }
        let tensor = try ORTValue(tensorData: data, elementType: .float, shape: [1, 3, NSNumber(value: size), NSNumber(value: size)])
        guard let inName = try s.inputNames().first, let outName = try s.outputNames().first else { throw LemonError(message: "Unexpected model.") }
        // Without this, ONNX Runtime's CPU arena keeps growing between runs (measured: 6.4 GB after one, 11 GB after two).
        let options = try ORTRunOptions()
        try options.addConfigEntry(withKey: "memory.enable_memory_arena_shrinkage", value: "cpu:0")
        let result = try s.run(withInputs: [inName: tensor], outputNames: [outName], runOptions: options)
        guard let raw = try result[outName]?.tensorData() as Data? else { throw LemonError(message: "No model output.") }
        return raw.withUnsafeBytes { Array($0.bindMemory(to: Float.self)) }
    }
}

// MARK: Settings › Background remover

@MainActor final class ModelStore: ObservableObject {
    static let shared = ModelStore()
    @Published var progress: [String: Double] = [:]
    @Published var error: String?
    private var tasks: [String: Task<Void, Never>] = [:]

    func download(_ m: BiRefNet) {
        guard tasks[m.file] == nil else { return }
        error = nil; progress[m.file] = 0
        tasks[m.file] = Task {
            // The download reports every chunk; redrawing Settings for each is wasted work, so only 0.5% steps get through.
            do { try await m.download { p in Task { @MainActor in
                if let old = self.progress[m.file], p - old >= 0.005 || p >= 1 { self.progress[m.file] = p }
            } } }
            catch is CancellationError {}
            catch let e as LemonError { error = e.message }
            catch { if (error as? URLError)?.code != .cancelled { self.error = error.localizedDescription } }
            progress[m.file] = nil; tasks[m.file] = nil
        }
    }
    func cancel(_ m: BiRefNet) { tasks[m.file]?.cancel() }
    func delete(_ m: BiRefNet) { BiRefNetRunner.release(m); try? FileManager.default.removeItem(at: m.path); objectWillChange.send() }
}
