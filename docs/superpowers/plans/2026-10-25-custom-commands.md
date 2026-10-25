# Custom Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Commands ring on the Mac wheel that runs the user's own shell lines on dragged folders and links.

**Architecture:** A command shows on the wheel as a `Preset` with `command` set to the command's id, so hover, labels and
drop reuse the existing wheel code. `WheelModel.load` gains the dragged folders and links and fills a `.commands` ring
from `Prefs.commands`. `WheelController` reads web links from the drag pasteboard, and its pick runs the command through
`/bin/zsh -lc` with `{path}` / `{url}` replaced by the shell-quoted item.

**Tech Stack:** Swift 5.9, SwiftUI/AppKit, XCTest (`swift test` in `mac/`).

**Spec:** `docs/superpowers/specs/2026-10-25-custom-commands-design.md`

## Global Constraints

- Mac only; Windows gets a **To do** in `PARITY-LOG.md`.
- New `Prefs` fields are Optional (an old prefs blob must still decode).
- Placeholders: `{path}`, `{url}`, always single-quoted for the shell.
- Runs as `/bin/zsh -lc <line>`, once per item, in the folder (links: the home folder), without waiting.
- Build output goes only under `dist/MacOS` (`mac/packaging/make-app.sh`).

## Review Focus

- A path with spaces, `'` or `$` reaches the command unchanged (Task 1 roundtrip test).
- A folder with nothing convertible still opens the wheel when a folder command exists (Task 2 test).
- A link-only drag opens only the Commands ring. A drag with no fitting commands has no Commands ring (Task 2 tests).
- Dropping a link on the lemon in the middle doesn't open an empty queue (Task 2: guard in `performDragOperation`).
- A failing command shows its name and error output (Task 1: alert on non-zero exit or launch error; manual check in Task 4).

---

### Task 1: Command model and runner

**Files:**
- Create: `mac/Sources/Lemon/Wheel/Commands.swift`
- Modify: `mac/Sources/Lemon/Settings/Customize.swift` (`Prefs`: add `var commands: [WheelCommand]? = nil`)
- Test: `mac/Tests/LemonTests/CommandTests.swift`

**Interfaces:**
- Produces: `struct WheelCommand: Codable, Hashable, Identifiable { id = UUID().uuidString, name = "", symbol = "terminal", forLinks = false, line = "" }`, `static let symbols: [String]`
- Produces: `func commandLine(_ line: String, _ value: String) -> String`
- Produces: `func runCommand(_ c: WheelCommand, on items: [URL])` (folders are file URLs, links are http(s) URLs)

- [ ] **Step 1: Test** `testQuotesForTheShell`: `commandLine("echo {path} {url}", "a b'c$d")` run through `/bin/zsh -c` prints `a b'c$d a b'c$d`.
- [ ] **Step 2:** `swift test --filter CommandTests` fails (no `commandLine`).
- [ ] **Step 3:** Implement. Quote as `'` + value with `'` → `'\''` + `'`. `runCommand`: per item, a `Process`, stdout to null and stderr to a `Pipe`. On a background queue, read stderr to the end, wait, then on non-zero exit (or `run()` throwing) show an `NSAlert` on main: “\(name)” failed, plus the last 600 characters of stderr (or the exit code).
- [ ] **Step 4:** Tests pass.
- [ ] **Step 5:** Commit.

### Task 2: Commands ring on the wheel

**Files:**
- Modify: `mac/Sources/Lemon/Conversion/Presets.swift` (`Preset`: `var command: String? = nil`)
- Modify: `mac/Sources/Lemon/Wheel/Wheel.swift`
- Test: `mac/Tests/LemonTests/CommandTests.swift`

**Interfaces:**
- Consumes: `WheelCommand`, `runCommand` (Task 1)
- Produces: `WheelMode.commands` (id `"commands"`, in `builtIns` after resize; title "Commands", symbol "terminal", detail "Your commands"; `holds` is false)
- Produces: `WheelModel.load(_ urls: [URL], folders: [URL] = [], links: [URL] = [])` and `private(set) var folders, links`

- [ ] **Step 1: Tests**, with `PrefStore.shared.p.commands` set and restored:
  - `testFolderGetsCommandsRing`: a folder command, `load([], folders: [/tmp])`, so `modes == [.commands]` and `choices.map(\.name) == ["Terminal"]`.
  - `testLinkGetsOnlyLinkCommands`: one folder and one link command, `load([], links: [https://example.com])`, so only the link command is shown.
  - `testNoCommandsNoRing`: `commands = []`, `load([], folders: [/tmp])`, so `modes` is empty.
- [ ] **Step 2:** Tests fail.
- [ ] **Step 3:** Implement.
  - `load`: `byMode[.commands]` = commands where `forLinks ? !links.isEmpty : !folders.isEmpty`, as `Preset(name:, ext: "cmd", kind: .document, command: id)`. Keep it even when `main` is nil.
  - `wheelLabel` / `wheelSymbol`: a command preset returns (name, nil) and its symbol.
  - Controller `open()`: read every `NSURL`. If any is http(s), show links only. Otherwise use file URLs as now.
  - `show(files, links:)`: pass the non-package folders through.
  - `pick`: a preset with `command` runs `runCommand` on `model.links` or `model.folders`.
  - `WheelHost`: register `.URL` too, and the lemon only queues when the drop has file URLs.
- [ ] **Step 4:** Tests pass. `swift build` is clean.
- [ ] **Step 5:** Commit.

### Task 3: Settings

**Files:**
- Modify: `mac/Sources/Lemon/Settings/Customize.swift`

- [ ] **Step 1:** Add a "Commands" section after "Rings" in `WheelSettings`: one row per command (symbol, name, "Folders"/"Links", Edit, trash), plus a "New Command…" button. Add `CommandEditor` (modeled on `RingEditor`): Name, Icon picker, "Runs on" picker (Folders/Links), and a "Command" `TextField` with axis `.vertical`, prompt `open -na Ghostty --args --working-directory={path}`. Footer: `{path} is the folder, {url} the link. Runs in zsh, once for each item.` Save is disabled while the name or line is empty.
- [ ] **Step 2:** `swift build` is clean. Run the app and add, edit and delete a command.
- [ ] **Step 3:** Commit.

### Task 4: Docs, parity, package

- [ ] `DETAIL.md`: a Commands section with the Ghostty/Claude examples. `README.md`: a feature line. `PARITY-LOG.md`: an entry at the top (Mac **Done**, Windows **To do**). Note that Windows' wheel already opens on links but ignores the drop.
- [ ] Build with `mac/packaging/make-app.sh`, then check by hand: a folder runs Terminal, a link runs a command, and a failing line (`false`) shows an alert.
- [ ] Commit.
