import AppKit

enum RunsOn: String, Codable, CaseIterable { case files = "Files", folders = "Folders", links = "Links" }

/// A line of shell the user runs from the Commands ring on dragged files, folders or links.
struct WheelCommand: Codable, Hashable, Identifiable {
    var id = UUID().uuidString
    var name = ""
    var symbol = "terminal"
    var forLinks = false   // the first version's switch (false: folders); `runsOn` replaces it, kept so saved commands decode
    var line = ""
    var runsOn: RunsOn? = nil
    var types: String? = nil   // Files: extensions such as "pdf, png"; empty means any file
    static let symbols = ["terminal", "chevron.left.forwardslash.chevron.right", "sparkles", "folder", "globe", "link", "camera", "bookmark", "tray.and.arrow.down", "star", "bolt", "paperplane"]

    var on: RunsOn { runsOn ?? (forLinks ? .links : .folders) }
    /// Whether a dragged file is one of `types`.
    func takes(_ u: URL) -> Bool {
        let list = (types ?? "").lowercased().split { $0 == "," || $0 == " " }.map { $0.hasPrefix(".") ? String($0.dropFirst()) : String($0) }
        return list.isEmpty || list.contains(u.pathExtension.lowercased())
    }
}

/// A placeholder in a command line, shown as a chip in the editor.
enum Token: String, CaseIterable {
    case file, folder, files, link
    var text: String { "{\(rawValue)}" }
    var label: String { [.file: "File", .folder: "Folder", .files: "Files in Folder", .link: "Link"][self]! }
    var color: NSColor { [.file: NSColor.systemBlue, .folder: .systemOrange, .files: .systemPurple, .link: .systemGreen][self]! }
    /// The first version's {path} and {url} still work.
    static func named(_ s: String) -> Token? { s == "{path}" ? .folder : s == "{url}" ? .link : allCases.first { $0.text == s } }
    static func offered(_ on: RunsOn) -> [Token] { [.files: [.file], .folders: [.folder, .files], .links: [.link]][on]! }
}

private func quote(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: #"'\''"#) + "'" }

/// `line` with each placeholder replaced by `value` (a path or link) and {files} by `files`, all single-quoted so
/// spaces, quotes and `$` arrive unchanged.
func commandLine(_ line: String, _ value: String, files: [String] = []) -> String {
    var out = line.replacingOccurrences(of: Token.files.text, with: files.map(quote).joined(separator: " "))
    for t in ["{path}", "{url}", Token.file.text, Token.folder.text, Token.link.text] { out = out.replacingOccurrences(of: t, with: quote(value)) }
    return out
}

/// The files directly inside a folder, by name, without hidden ones or subfolders.
private func filesIn(_ folder: URL) -> [String] {
    // ponytail: top level only; recurse if someone wants a whole tree in one command.
    let all = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.isRegularFileKey], options: .skipsHiddenFiles)) ?? []
    return all.filter { (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true }.map(\.path).sorted()
}

/// Runs the command once per item in a login shell (so Homebrew's tools are on PATH), without waiting for it. A failure
/// shows an alert with the end of what it wrote to stderr.
func runCommand(_ c: WheelCommand, on items: [URL]) {
    for item in items {
        let p = Process(), err = Pipe()
        let files = c.on == .folders && c.line.contains(Token.files.text) ? filesIn(item) : []
        p.executableURL = URL(fileURLWithPath: "/bin/zsh")
        p.arguments = ["-lc", commandLine(c.line, item.isFileURL ? item.path : item.absoluteString, files: files)]
        // In the folder; a file's own folder; a link's in home.
        p.currentDirectoryURL = !item.isFileURL ? FileManager.default.homeDirectoryForCurrentUser : c.on == .files ? item.deletingLastPathComponent() : item
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
