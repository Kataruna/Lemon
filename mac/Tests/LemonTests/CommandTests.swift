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
