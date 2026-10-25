import AppKit

/// A line of shell the user runs from the Commands ring on a dragged folder or link.
struct WheelCommand: Codable, Hashable, Identifiable {
    var id = UUID().uuidString
    var name = ""
    var symbol = "terminal"
    var forLinks = false   // false: folders
    var line = ""
    static let symbols = ["terminal", "chevron.left.forwardslash.chevron.right", "sparkles", "folder", "globe", "link", "camera", "bookmark", "tray.and.arrow.down", "star", "bolt", "paperplane"]
}

/// `line` with every {path} and {url} replaced by `value`, single-quoted so spaces, quotes and `$` arrive unchanged.
func commandLine(_ line: String, _ value: String) -> String {
    let q = "'" + value.replacingOccurrences(of: "'", with: #"'\''"#) + "'"
    return line.replacingOccurrences(of: "{path}", with: q).replacingOccurrences(of: "{url}", with: q)
}

/// Runs the command once per item in a login shell (so Homebrew's tools are on PATH), without waiting for it. A failure
/// shows an alert with the end of what it wrote to stderr.
func runCommand(_ c: WheelCommand, on items: [URL]) {
    for item in items {
        let p = Process(), err = Pipe()
        p.executableURL = URL(fileURLWithPath: "/bin/zsh")
        p.arguments = ["-lc", commandLine(c.line, item.isFileURL ? item.path : item.absoluteString)]
        p.currentDirectoryURL = item.isFileURL ? item : FileManager.default.homeDirectoryForCurrentUser
        p.standardInput = FileHandle.nullDevice; p.standardOutput = FileHandle.nullDevice; p.standardError = err
        do { try p.run() } catch { commandFailed(c, error.localizedDescription); continue }
        // ponytail: one waiting thread per running command; a command that leaves a child holding stderr keeps it until that child exits.
        DispatchQueue.global().async {
            let out = String(decoding: err.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
            p.waitUntilExit()
            guard p.terminationStatus != 0 else { return }
            let tail = out.trimmingCharacters(in: .whitespacesAndNewlines).suffix(600)
            commandFailed(c, tail.isEmpty ? "It stopped with exit code \(p.terminationStatus)." : String(tail))
        }
    }
}

private func commandFailed(_ c: WheelCommand, _ detail: String) {
    DispatchQueue.main.async {
        let a = NSAlert()
        a.messageText = "“\(c.name)” failed"
        a.informativeText = detail
        // A menu bar app: without this the alert can open behind the app the drag came from.
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }
}
