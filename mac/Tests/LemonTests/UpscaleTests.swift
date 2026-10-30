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
        for (scale, tile) in [(2, 256), (4, 256), (4, 128)] {
            let got = try Upscale.tiled(src, w, h, scale: scale, tile: tile, model: nearest)
            var want = [UInt8](repeating: 0, count: w * scale * h * scale * 3)
            for y in 0..<(h * scale) { for x in 0..<(w * scale) { for c in 0..<3 {
                want[(y * w * scale + x) * 3 + c] = src[((y / scale) * w + x / scale) * 3 + c]
            } } }
            XCTAssertEqual(got, want, "\(scale)×")
        }
    }
}

@MainActor final class WheelStepTests: XCTestCase {
    /// Arrow keys over a selection: from nothing lit, → lights the top wedge and ← the last; both wrap around.
    func testArrowsWalkTheWedges() {
        let m = WheelModel(), p = Array(builtIn.prefix(3))
        m.load(demo: p)
        m.step(1); XCTAssertEqual(m.hovered, p[0])
        m.step(-1); XCTAssertEqual(m.hovered, p[2])
        m.step(1); XCTAssertEqual(m.hovered, p[0])
        m.hovered = nil; m.step(-1); XCTAssertEqual(m.hovered, p[2])
    }
}

final class WheelKeySideTests: XCTestCase {
    func testSideIsReadFromTheLowBits() {
        let control = NSEvent.ModifierFlags.control.rawValue
        XCTAssertTrue(WheelKey.control.isDown(in: control))
        XCTAssertTrue(WheelKey.control.on(.left).isDown(in: control | 0x1))
        XCTAssertFalse(WheelKey.control.on(.left).isDown(in: control | 0x2000))
        XCTAssertTrue(WheelKey.control.on(.right).isDown(in: control | 0x2000))
        XCTAssertFalse(WheelKey.control.on(.right).isDown(in: 0x2000))   // the bit without the modifier
        XCTAssertTrue(WheelKey.shift.on(.right).isDown(in: NSEvent.ModifierFlags.shift.rawValue | 0x4))
        XCTAssertFalse(WheelKey.shift.on(.right).isDown(in: NSEvent.ModifierFlags.shift.rawValue | 0x2))
    }
    func testASidedKeyStaysItsKey() {
        XCTAssertEqual(WheelKey.control.on(.right).name, "Right Control")
        XCTAssertTrue(WheelKey.control.on(.right).held)
        XCTAssertEqual(WheelKey.control.on(.right).on(nil), .control)
    }
}

final class KeyTapTests: XCTestCase {
    func testOpeningHoldIsNeitherTapNorClose() {
        var t = KeyTap()
        XCTAssertNil(t.update(held: true, now: 0)); XCTAssertNil(t.update(held: true, now: 2))
        XCTAssertNil(t.update(held: false, now: 2.1))
    }
    func testTapEntersHoldCloses() {
        var t = KeyTap()
        _ = t.update(held: false, now: 0)
        XCTAssertNil(t.update(held: true, now: 1)); XCTAssertEqual(t.update(held: false, now: 1.2), .enter)
        XCTAssertNil(t.update(held: true, now: 2)); XCTAssertEqual(t.update(held: true, now: 2.7), .close)
        XCTAssertNil(t.update(held: true, now: 3)); XCTAssertNil(t.update(held: false, now: 3.1))
    }
    /// Control+D: D can arrive before the first poll sees Control; either way it's neither a tap nor a close.
    func testAnotherKeySpoilsThePress() {
        var t = KeyTap()
        _ = t.update(held: false, now: 0)
        t.otherKey()
        XCTAssertNil(t.update(held: true, now: 1)); XCTAssertNil(t.update(held: true, now: 2)); XCTAssertNil(t.update(held: false, now: 2.1))
        XCTAssertNil(t.update(held: true, now: 3)); t.otherKey(); XCTAssertNil(t.update(held: false, now: 3.1))
        XCTAssertNil(t.update(held: true, now: 4)); XCTAssertEqual(t.update(held: false, now: 4.1), .enter)
    }
}
