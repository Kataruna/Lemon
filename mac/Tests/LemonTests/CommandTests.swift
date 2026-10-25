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

final class CommandTokenTests: XCTestCase {
    func testChipsRoundTrip() {
        let s = chipString("open {folder} {x} {files} {url} {", font: .systemFont(ofSize: 12))
        XCTAssertEqual(chipText(s), "open {folder} {x} {files} {link} {")   // {url} comes back as {link}
    }

    func testFilesInFolder() {
        XCTAssertEqual(commandLine("ls {folder} {files}", "/a b", files: ["/a b/x", "/a b/y'z"]), #"ls '/a b' '/a b/x' '/a b/y'\''z'"#)
    }

    func testFileTypes() {
        let c = WheelCommand(name: "PDF", line: "x {file}", runsOn: .files, types: "pdf, .PNG")
        XCTAssertTrue(c.takes(URL(fileURLWithPath: "/a.PDF")))
        XCTAssertTrue(c.takes(URL(fileURLWithPath: "/a.png")))
        XCTAssertFalse(c.takes(URL(fileURLWithPath: "/a.txt")))
        XCTAssertTrue(WheelCommand(runsOn: .files).takes(URL(fileURLWithPath: "/a.zip")))
    }

    func testOldSavedCommandStillDecodes() throws {
        let c = try JSONDecoder().decode(WheelCommand.self, from: Data(#"{"id":"1","name":"Keep","symbol":"link","forLinks":true,"line":"k {url}"}"#.utf8))
        XCTAssertEqual(c.on, .links)
    }
}

@MainActor final class FileCommandRingTests: XCTestCase {
    private var saved: [WheelCommand]?
    override func setUp() async throws { saved = PrefStore.shared.p.commands }
    override func tearDown() async throws { PrefStore.shared.p.commands = saved }

    func testFileCommandOnlyForItsTypes() {
        PrefStore.shared.p.commands = [WheelCommand(name: "Sign", line: "sign {file}", runsOn: .files, types: "pdf")]
        let pdf = URL(fileURLWithPath: "/tmp/a.pdf"), txt = URL(fileURLWithPath: "/tmp/b.zip")
        let m = WheelModel(); m.load([], files: [pdf, txt])
        XCTAssertEqual(m.choices.map(\.name), ["Sign"])
        XCTAssertEqual(m.targets(PrefStore.shared.p.commands![0]), [pdf])
        m.load([], files: [txt])
        XCTAssertTrue(m.modes.isEmpty)
    }
}

final class CommandOnePassTests: XCTestCase {
    func testNamesWithPlaceholdersStayQuoted() throws {
        let p = Process(), out = Pipe()
        p.executableURL = URL(fileURLWithPath: "/bin/zsh")
        p.arguments = ["-c", commandLine("printf '%s|' {folder} {files}", "/x/{link}'a", files: ["/x/{folder}$b"])]
        p.standardOutput = out
        try p.run(); p.waitUntilExit()
        XCTAssertEqual(String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self), "/x/{link}'a|/x/{folder}$b|")
    }
}
