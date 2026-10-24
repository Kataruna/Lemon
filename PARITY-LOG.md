# Parity Log

Lemon has two separate apps: `windows/` (C# / WinUI) and `mac/` (Swift / SwiftUI). They share no code, so a change on one side has to be redone on the other by hand. This log tracks what changed on each side and whether the other side has it yet.

**How to use it**
- When you change a user-facing behavior on one side, add an entry at the top of the log.
- When you port it, change the other side's status to **Done** and note anything that came out different.
- To catch one side up, look for entries marked **To do** for that side.

Status: **Done** · **To do** · **Won't do** (with the reason) · **Partly** (say what's missing).

---

## 2026-10-24: Welcome guide on first launch (converters to install, then the wheel)

| Windows | Mac |
|---|---|
| **Done** | **To do** |

- **Step 2 is now an animation (same day):** instead of three text cards, a 6-second loop acts it out: a *Photo.heic* card is dragged, shaken (or a *Space* keycap pops up when only the key is on), a wheel of formats (JPG / PNG / WEBP / PDF, lemon in the middle) opens, the file is dropped on JPG, and *Photo.jpg* appears under the original. Under it, *1. Drag · 2. Shake · 3. Drop* light up in turn. With Windows animations turned off it shows a still of the open wheel and the new file. Mac: do the same in SwiftUI (respect *Reduce motion*).
- **Redesigned (same day) to be less technical:** the guide talks about kinds of files, not programs. No program names, paths, winget commands or settings switches on screen. A big lemon heading, cards, and dots for the two steps.
- On the first launch, the main window opens on a two-step welcome guide instead of the Convert tab. Skip (top right), any button that leaves it, or switching tabs marks it as seen (`AppSettings.WelcomeShown`). *Settings › About › Show the welcome guide again* reopens it.
- **Step 1: "Welcome to Lemon — Pictures work right away. What else do you want to convert?"** One card per kind of file: *Pictures* (always Ready), *Videos and music* (FFmpeg), *Documents and ebooks* (Pandoc), *Making PDFs* (LibreOffice), *Shrinking PDFs* (Ghostscript). Each shows **Ready** or an **Add** button (*Get it…* for Ghostscript), which that runs `winget install --id <id> --exact --source winget --accept-source-agreements --accept-package-agreements` in a visible console window, which stays open on an error. When the window closes, Lemon checks again. Ghostscript isn't on winget, so its button opens the download page. The background-remover card is gone from the guide (it's still in Settings).
- **Step 2: "Convert without opening Lemon."** Three cards side by side: *1. Drag*, *2. Shake* (or *2. Press Space* when only the key is on; worded for the current settings), *3. Drop*. No Esc / Tab / rings tips and no "Opening the wheel" switches here any more. Buttons: *Back*, *Customize the wheel* (opens the Wheel tab), *Get started*.
- Existing users upgrading to 0.3.0 see it once too, which is a way to introduce the new wheel-opening settings.
- Windows files: `Lemon.App/Views/WelcomePage.xaml(.cs)`, `Views/MainWindow.xaml.cs` (`ShowWelcome`, `ShowWheelPage`), `Views/SettingsPage.xaml.cs` (`ToolStatus.CheckAll`, shared with Settings › Converters), `Lemon.Core/ExternalTools/ToolLocator.cs` (`WingetId`, `DownloadPage`), `Views/WheelTriggers.xaml.cs` (`Changed` event).
- Mac notes: the Mac's tools come from Homebrew (`brew install ffmpeg imagemagick pandoc ghostscript`, LibreOffice optional). An Install button could run `brew install <formula>` in Terminal (for example through `osascript` telling Terminal to do it), and needs a fallback that links to brew.sh when Homebrew isn't installed. Step 2 should describe the Mac's shake / hold Shift, and the Accessibility permission the keys need.

## 2026-10-24: Windows 0.3.0, with a check for updates at startup

| Windows | Mac |
|---|---|
| **Done** | **To do** |

- Windows is now version **0.3.0** (`Lemon.App.csproj` `<Version>`; the installer reads it from there, and `packaging/Lemon.iss` has the same number as a fallback). Mac is still 0.2.0 (`mac/packaging/make-app.sh`, `CFBundleShortVersionString`). Raise it when the Mac app catches up with the To-do items in this log.
- When it starts, the app asks GitHub for the repo's release list (`api.github.com/repos/Kataruna/Lemon/releases`) and picks the newest published release whose tag starts with `windows-v`. Drafts and pre-releases are skipped. If it's newer than this copy, an "Update available" notice with a **Download** button appears on the Convert page, and Settings › About shows a download link. Both open the release page on GitHub. Nothing installs by itself.
- Settings › About has *Check for updates when Lemon starts* (`AppSettings.CheckForUpdates`, on by default). Being offline or a GitHub error just means no notice.
- Windows files: `Lemon.Core/UpdateCheck.cs` (+ `tests/.../UpdateCheckTests.cs`), `Lemon.App/App.xaml.cs` (`CheckForUpdatesAsync`), `Views/ConvertPage.xaml(.cs)`, `Views/SettingsPage.xaml(.cs)`.
- Mac notes: do the same with tags starting with `mac-v`. Compare major.minor.patch only, and send a User-Agent header, because GitHub's API rejects requests without one.
- Releasing: tag Windows releases `windows-vX.Y.Z` and Mac releases `mac-vX.Y.Z`, as now. The check depends on that naming.

## 2026-10-24: Esc closes the wheel without cancelling the drag

| Windows | Mac |
|---|---|
| **Done** | **To do** |

- While the wheel is showing mid-drag, Esc only closes the wheel. The files are still being dragged and can be dropped anywhere, and shaking or pressing the wheel key opens the wheel again in the same drag.
- With no wheel showing, Esc cancels the drag as the system normally does.
- Windows: the low-level keyboard hook swallows Esc (down, repeats and up) only while the wheel is open and the left button is down, so the drag never sees it (`Lemon.App/Wheel/DragGestureMonitor.cs`, `_swallowingEscape`). It also resets the "already opened this drag" flag and the shake detector, so the wheel can be opened again.
- Mac notes: `Wheel.swift` has no Esc handling today. The key tap that already handles Tab / 1–9 / 0 while the wheel is up could take Esc too: return nil to swallow it, close the wheel, and reset `fired` and the shake detector.

## 2026-10-24: Wheel can be opened by shaking, by a key, or both; the key can be changed

| Windows | Mac |
|---|---|
| **Done** | **To do** |

- Settings › Conversion wheel has two switches: *Open it by shaking the mouse while dragging* and *Open it by pressing a key while dragging*.
- Both can't be off. Turning off the last one turns the other back on, and a note appears under the switches: *"You can't turn off both ways to open the wheel, so opening it by {shaking the mouse / pressing a key} is back on."*
- The key is chosen by clicking a button and pressing the key (Esc cancels). Not allowed: Esc (cancels the drag), 0–9 (switch rings), and Shift / Ctrl / Alt / Win (the low-level hook sees them as left/right keys). Pressing one of those shows "Pick another key…".
- These settings appear in two places: Settings › Conversion wheel and Settings › Wheel (an "Opening the wheel" card under the preview). On Windows both use one shared control, `Lemon.App/Views/WheelTriggers.xaml(.cs)`.
- The default is shaking on, key on, key = Space. Older settings that had both off now start with shaking on.
- Windows files: `Lemon.Core/Configuration/AppSettings.cs` (`WheelOnShake`, `WheelOnKey`, `WheelKey` as a `VirtualKey` name), `Lemon.App/App.xaml.cs` (`ApplyWheelSettings`), `Lemon.App/Views/SettingsPage.xaml(.cs)`.
- Mac notes: the Mac wheel opens on a shake or by holding Shift (`Wheel.swift`, the drag watcher), and neither can be turned off. The README's Mac section says "Not ported: the Space key, the shake/key settings". A port needs both switches with the same both-can't-be-off rule, and a key picker. Watching keys globally needs Accessibility permission, which the Mac app already asks for.

## 2026-10-24: Windows app icon is now the Mac icon

| Windows | Mac |
|---|---|
| **Done** | **Done** (the source) |

- `windows/packaging/make-icon.cs` now builds `Assets/Lemon.png` (256 px) and `Assets/Lemon.ico` (16–256 px) from `mac/Lemon.png`, instead of drawing its own lemon.
- If the Mac icon changes: update `mac/Lemon.png`, then run `dotnet run windows/packaging/make-icon.cs`.
- Difference: the Windows icon is the plain artwork on a transparent background, without the Mac version's gradient background and glass effects (Icon Composer only).

## 2026-10-24: Hub mode — the middle of the ring leads back to the hub

| Windows | Mac |
|---|---|
| **Done** (ported from Mac) | **Done** (the original) |

- When the hub is on and you're inside a ring, the middle of the wheel shows a back arrow and "All rings". Resting the files there goes back to the hub, and dropping on it does nothing. The "All rings" chip under the ring is hidden.
- The middle only works as "back" after the pointer has left it once since the ring opened, so a pause right after the shake doesn't jump to the hub.
- A setting brings back the chip instead: Windows *Back to the hub from a chip under the ring* (`WheelSettings.BackChip`); Mac *Back to the hub from a button under the ring* (`backPill`). Off by default on both.
- Windows files: `Lemon.App/Wheel/WheelWindow.xaml.cs` (`CenterBack`, `_centerArmed`, `ShowCenter`), `Lemon.App/Views/WheelPreview.cs`, `Lemon.App/Views/WheelSettingsPage.xaml(.cs)`, `Lemon.Core/Wheel/WheelSettings.cs`.
- Difference: Windows waits 450 ms for every rest-to-switch. Mac waits 250 ms to open a ring from the hub and 450 ms for everything else.
