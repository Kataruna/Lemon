import XCTest
@testable import Lemon

final class CommandTests: XCTestCase {
    func testQuotesForTheShell() throws {
        let p = Process(), out = Pipe()
        p.executableURL = URL(fileURLWithPath: "/bin/zsh")
        p.arguments = ["-c", commandLine("echo {path} {url}", "a b'c$d")]
        p.standardOutput = out
        try p.run(); p.waitUntilExit()
        XCTAssertEqual(String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self), "a b'c$d a b'c$d\n")
    }
}

@MainActor final class CommandRingTests: XCTestCase {
    private var saved: [WheelCommand]?
    override func setUp() async throws { saved = PrefStore.shared.p.commands }
    override func tearDown() async throws { PrefStore.shared.p.commands = saved }
    private let terminal = WheelCommand(name: "Terminal", line: "open {path}"), keep = WheelCommand(name: "Keep", forLinks: true, line: "keep {url}")

    func testFolderGetsCommandsRing() {
        PrefStore.shared.p.commands = [terminal, keep]
        let m = WheelModel(); m.load([], folders: [URL(fileURLWithPath: "/tmp", isDirectory: true)])
        XCTAssertEqual(m.modes, [.commands])
        XCTAssertEqual(m.choices.map(\.name), ["Terminal"])
    }

    func testLinkGetsOnlyLinkCommands() {
        PrefStore.shared.p.commands = [terminal, keep]
        let m = WheelModel(); m.load([], links: [URL(string: "https://example.com")!])
        XCTAssertEqual(m.modes, [.commands])
        XCTAssertEqual(m.choices.map(\.name), ["Keep"])
    }

    func testNoCommandsNoRing() {
        PrefStore.shared.p.commands = []
        let m = WheelModel(); m.load([], folders: [URL(fileURLWithPath: "/tmp", isDirectory: true)])
        XCTAssertTrue(m.modes.isEmpty)
    }
}
