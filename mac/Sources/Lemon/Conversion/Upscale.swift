import Foundation
import CoreImage
import ImageIO
import UniformTypeIdentifiers

/// Enlarges an image 2× or 4× with Real-ESRGAN (port of UpscaleConverter.cs).
enum Upscale {
    /// The networks always enlarge 4×; for 2× each tile's result is halved.
    static let netScale = 4
    /// The model sees the picture in tiles of this size, so its memory stays the same whatever the picture's size.
    static let tile = 256
    /// Context around each tile that's cropped off its result, so the seams between tiles don't show.
    static let pad = 16
    /// Longest side of a result: WebP's limit, and it keeps the result's pixels around 1 GB.
    static let maxSide = 16383

    /// Core ML keeps what it allocated for a model (measured: 1.3 GB after a 4x upscale, 2.4 GB for x4plus) after the
    /// session is let go, and Lemon lives in the menu bar for days. So each upscale runs in a child process
    /// (`Lemon --upscale`) and the system takes it all back when that exits. Cancelling kills it. One at a time:
    /// a run already uses the whole GPU.
    static func inChild(_ m: AIModel, _ src: URL, _ dst: URL, scale: Int, _ progress: @escaping @Sendable (Double?) -> Void) async throws {
        guard let exe = Bundle.main.executablePath else { throw LemonError(message: "Upscaling failed.") }
        await gate.enter()
        defer { Task { await gate.leave() } }
        try await Lemon.run(exe, ["--upscale", m.file, "\(scale)", src.path, dst.path], onLine: { line in
            if let p = Double(line) { progress(p) }
        })
    }
    private static let gate = Gate()

    /// The child: prints progress as lines of 0...1, and an error as the last line of stderr.
    static func child(_ a: [String]) -> Int32 {
        guard a.count == 4, let m = AIModel.all.first(where: { $0.file == a[0] }), let scale = Int(a[1]) else { return 2 }
        do {
            try run(m, URL(fileURLWithPath: a[2]), URL(fileURLWithPath: a[3]), scale: scale) { p in if let p { print(p); fflush(stdout) } }
            return 0
        } catch {
            FileHandle.standardError.write(((error as? LemonError)?.message ?? "Upscaling failed: \(error.localizedDescription)").data(using: .utf8)! + Data([10]))
            return 1
        }
    }

    static func run(_ m: AIModel, _ src: URL, _ dst: URL, scale: Int, _ progress: @escaping @Sendable (Double?) -> Void) throws {
        let e = ext(of: src)
        guard let source = CGImageSourceCreateWithURL(src as CFURL, nil) else { throw LemonError(message: "Couldn't read the image.") }
        // Only the first frame would survive.
        if (e == "webp" && CGImageSourceGetCount(source) > 1) || (e == "png" && isAnimatedPNG(src)) {
            throw LemonError(message: "Can't upscale animated pictures.")
        }
        guard let image = CIImage(contentsOf: src, options: [.applyOrientationProperty: true]) else { throw LemonError(message: "Couldn't read the image.") }
        let w = Int(image.extent.width), h = Int(image.extent.height)
        guard max(w, h) * scale <= maxSide else { throw LemonError(message: "Too big to upscale \(scale)×: the result would be over \(maxSide) px on its long side.") }
        progress(0.05)

        // The model sees RGB in the picture's own space (Display P3 stays P3); CMYK and gray go through sRGB.
        let space = image.colorSpace.flatMap { $0.model == .rgb ? $0 : nil } ?? CGColorSpace(name: CGColorSpace.sRGB)!
        let base = image.transformed(by: CGAffineTransform(translationX: -image.extent.minX, y: -image.extent.minY))
        // Straight (not premultiplied) colors: premultiplied ones are darker where the picture is see-through, and the
        // model would draw that in as halos.
        let rgba = render(base, w, h, space)
        var rgb = [UInt8](repeating: 0, count: w * h * 3)
        for i in 0..<(w * h) { rgb[i * 3] = rgba[i * 4]; rgb[i * 3 + 1] = rgba[i * 4 + 1]; rgb[i * 3 + 2] = rgba[i * 4 + 2] }

        ModelRunner.runLock.lock()
        let result: [UInt8]
        do {
            defer { ModelRunner.runLock.unlock() }
            result = try tiled(rgb, w, h, scale: scale, progress: { progress(0.05 + 0.85 * $0) }) { input, tw, th in
                try ModelRunner.predict(ModelRunner.session(m), input, shape: [1, 3, th, tw])
            }
        }

        // Transparency is the original's alpha enlarged with Lanczos; the model only does colors.
        let ow = w * scale, oh = h * scale
        let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any] ?? [:]
        let alpha = props[kCGImagePropertyHasAlpha] as? Bool ?? false
        let big = alpha ? render(base.applyingFilter("CILanczosScaleTransform", parameters: [kCIInputScaleKey: scale, kCIInputAspectRatioKey: 1]), ow, oh, space) : nil
        var out = [UInt8](repeating: 255, count: ow * oh * 4)
        for i in 0..<(ow * oh) {
            out[i * 4] = result[i * 3]; out[i * 4 + 1] = result[i * 3 + 1]; out[i * 4 + 2] = result[i * 3 + 2]
            if let big { out[i * 4 + 3] = big[i * 4 + 3] }
        }
        progress(0.93)
        guard let picture = CGImage(width: ow, height: oh, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: ow * 4, space: space,
                                    bitmapInfo: CGBitmapInfo(rawValue: (alpha ? CGImageAlphaInfo.last : .noneSkipLast).rawValue),
                                    provider: CGDataProvider(data: Data(out) as CFData)!, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        else { throw LemonError(message: "Upscaling failed.") }
        try write(picture, props, to: dst, e)
        progress(1)
    }

    private static func render(_ image: CIImage, _ w: Int, _ h: Int, _ space: CGColorSpace) -> [UInt8] {
        var px = [UInt8](repeating: 0, count: w * h * 4)
        sharedCIContext.render(image.unpremultiplyingAlpha(), toBitmap: &px, rowBytes: w * 4, bounds: CGRect(x: 0, y: 0, width: w, height: h), format: .RGBA8, colorSpace: space)
        return px
    }

    /// Writes in the source's format with its metadata (upright now, so orientation 1). ImageIO can't write WebP (and
    /// on some systems AVIF), so those go through a PNG and magick.
    private static func write(_ picture: CGImage, _ props: [CFString: Any], to dst: URL, _ e: String) throws {
        var meta = props
        meta[kCGImagePropertyOrientation] = 1
        if var tiff = meta[kCGImagePropertyTIFFDictionary] as? [CFString: Any] { tiff[kCGImagePropertyTIFFOrientation] = 1; meta[kCGImagePropertyTIFFDictionary] = tiff }
        meta[kCGImageDestinationLossyCompressionQuality] = 0.95
        let writable = CGImageDestinationCopyTypeIdentifiers() as? [String] ?? []
        let type = UTType(filenameExtension: e)?.identifier ?? ""
        let direct = writable.contains(type)
        let target = direct ? dst : dst.deletingPathExtension().appendingPathExtension("png")
        guard let d = CGImageDestinationCreateWithURL(target as CFURL, direct ? type as CFString : UTType.png.identifier as CFString, 1, nil) else {
            throw LemonError(message: "Can't write .\(e) files.")
        }
        CGImageDestinationAddImage(d, picture, meta as CFDictionary)
        guard CGImageDestinationFinalize(d) else { throw LemonError(message: "Couldn't save the upscaled image.") }
        guard !direct else { return }
        defer { try? FileManager.default.removeItem(at: target) }
        let p = Process(); p.executableURL = URL(fileURLWithPath: try Tools.need("magick"))
        p.arguments = [target.path, "-quality", "95", dst.path]
        try p.run(); p.waitUntilExit()
        guard p.terminationStatus == 0 else { throw LemonError(message: "Couldn't save the upscaled image as .\(e).") }
    }

    /// Runs `model` tile by tile over interleaved RGB bytes and returns the enlarged RGB bytes. `model` takes planar
    /// RGB 0...1 of tw × th and returns planar RGB 4× as large.
    static func tiled(_ src: [UInt8], _ width: Int, _ height: Int, scale: Int,
                      progress: (Double) -> Void = { _ in }, model: ([Float], Int, Int) throws -> [Float]) throws -> [UInt8] {
        // Every tile has the same shape (edge tiles are moved inward), so the runtime doesn't re-plan per tile.
        let tw = min(tile + 2 * pad, width), th = min(tile + 2 * pad, height)
        let outWidth = width * scale
        var result = [UInt8](repeating: 0, count: outWidth * height * scale * 3)
        var buffer = [Float](repeating: 0, count: 3 * tw * th)
        let columns = (width + tile - 1) / tile, rows = (height + tile - 1) / tile
        let nw = tw * netScale, nh = th * netScale, tileWidth = tw * scale, step = netScale / scale
        let weight = 255 / Float(step * step)
        var piece = [UInt8](repeating: 0, count: tileWidth * th * scale * 3)

        for row in 0..<rows {
            for column in 0..<columns {
                let x0 = column * tile, y0 = row * tile
                let x1 = min(x0 + tile, width), y1 = min(y0 + tile, height)
                let wx = min(max(x0 - pad, 0), width - tw), wy = min(max(y0 - pad, 0), height - th)

                // Planar CHW, 0..1 (Real-ESRGAN takes no mean/std normalization).
                for c in 0..<3 { for y in 0..<th { for x in 0..<tw {
                    buffer[(c * th + y) * tw + x] = Float(src[((wy + y) * width + wx + x) * 3 + c]) / 255
                } } }
                let output = try model(buffer, tw, th)
                guard output.count >= 3 * nw * nh else { throw LemonError(message: "Upscaling failed: unexpected model output.") }

                // Planar floats to interleaved bytes, a row per thread. For 2× each 2×2 block is averaged, an exact halving.
                output.withUnsafeBufferPointer { o in piece.withUnsafeMutableBufferPointer { t in
                    DispatchQueue.concurrentPerform(iterations: th * scale) { y in
                        for c in 0..<3 {
                            var i = y * tileWidth * 3 + c
                            for x in 0..<tileWidth {
                                var sum: Float = 0
                                for dy in 0..<step { for dx in 0..<step { sum += min(max(o[(c * nh + y * step + dy) * nw + x * step + dx], 0), 1) } }
                                t[i] = UInt8(sum * weight + 0.5); i += 3
                            }
                        }
                    }
                } }

                // Keep only the tile's own area; the padding around it was context.
                let length = (x1 - x0) * scale * 3
                for y in ((y0 - wy) * scale)..<((y1 - wy) * scale) {
                    let from = (y * tileWidth + (x0 - wx) * scale) * 3, to = ((wy * scale + y) * outWidth + x0 * scale) * 3
                    result.replaceSubrange(to..<(to + length), with: piece[from..<(from + length)])
                }
                progress(Double(row * columns + column + 1) / Double(rows * columns))
            }
        }
        return result
    }
}

/// One holder at a time, waiting without blocking a thread.
actor Gate {
    private var busy = false
    private var waiting: [CheckedContinuation<Void, Never>] = []
    func enter() async {
        guard busy else { busy = true; return }
        await withCheckedContinuation { waiting.append($0) }   // handed over by leave(), still busy
    }
    func leave() { if waiting.isEmpty { busy = false } else { waiting.removeFirst().resume() } }
}
