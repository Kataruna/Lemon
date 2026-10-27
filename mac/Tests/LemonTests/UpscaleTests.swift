import XCTest
@testable import Lemon

final class UpscaleTests: XCTestCase {
    /// A stand-in for Real-ESRGAN that repeats each pixel 4×4: the tiled result must then be a plain nearest-neighbour
    /// enlargement, which catches crop, placement and 2× averaging mistakes. 300×200 isn't a multiple of the tile.
    func testTilingMatchesNearestNeighbour() throws {
        let w = 300, h = 200
        let src = (0..<(w * h * 3)).map { UInt8(($0 * 7 + $0 / 3 / w * 13) % 256) }
        let nearest: ([Float], Int, Int) throws -> [Float] = { input, tw, th in
            var out = [Float](repeating: 0, count: 3 * tw * th * 16)
            for c in 0..<3 { for y in 0..<(th * 4) { for x in 0..<(tw * 4) {
                out[(c * th * 4 + y) * tw * 4 + x] = input[(c * th + y / 4) * tw + x / 4]
            } } }
            return out
        }
        for scale in [2, 4] {
            let got = try Upscale.tiled(src, w, h, scale: scale, model: nearest)
            var want = [UInt8](repeating: 0, count: w * scale * h * scale * 3)
            for y in 0..<(h * scale) { for x in 0..<(w * scale) { for c in 0..<3 {
                want[(y * w * scale + x) * 3 + c] = src[((y / scale) * w + x / scale) * 3 + c]
            } } }
            XCTAssertEqual(got, want, "\(scale)×")
        }
    }
}
