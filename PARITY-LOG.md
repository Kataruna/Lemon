# Parity Log

Lemon has two separate apps: `windows/` (C# / WinUI) and `mac/` (Swift / SwiftUI). They share no code, so a change on one side has to be redone on the other by hand. This log tracks what changed on each side and whether the other side has it yet.

**How to use it**
- When you change a user-facing behavior on one side, add an entry at the top of the log.
- When you port it, change the other side's status to **Done** and note anything that came out different.
- To catch one side up, look for entries marked **To do** for that side.

Status: **Done** · **To do** · **Won't do** (with the reason) · **Partly** (say what's missing).

---

## 2026-10-27: The wheel over a selection stays up; tap the key to enter, hold to close (Mac)

| Windows | Mac |
|---|---|
| **To do** | **Done** (the original) |

- Over selected files the wheel no longer closes when the key is let go: it stays up until something is picked or it's closed. **A / W** and **D / S** light the slice (or the hub's ring) before or after the lit one, from the top and wrapping round; the pointer lights them too. A **tap of the key** (let go within 0.7 s, with no other key meanwhile), **Return** or a click enters what's lit: on the hub the ring, on a ring the slice runs. On the lemon after moving off it, a tap queues the files (or goes to the hub when the center is the way back). **Holding the key** 0.7 s closes the wheel. **Esc** inside a ring goes back to the hub; on the hub, or with no hub, it closes the wheel. A click outside closes it. Tab goes back with Shift+Tab again. Mid-drag nothing changes.
- Not arrows: on the Mac Control + arrows switch Spaces before an app sees them; they still work with Shift as the key. On Windows, Alt + arrows are free, but the same letters keep the two apps alike.
- Mac files: `Wheel/Wheel.swift` (`KeyTap` for tap / hold / spoiled, `WheelModel.step`, `WheelController.key`, `track`, `activate`), tests in `Tests/LemonTests/UpscaleTests.swift` (`WheelStepTests`, `KeyTapTests`). A key only spoils the tap when its own event has the wheel key held, so Control+D counts even if D beats the 60 Hz poll. The wheel closes when another app comes to the front (or the key tap would keep taking WASD there) and on a click or Control-click outside it; ⌘ keys always pass through.

---|---|
| **To do** | **Done** (the original) |

- With the wheel up over selected files (the key held, no drag), **A / W** and **D / S** light the slice before or after the lit one (from the top, wrapping round), as ⌘-Tab does with apps. Letting go of the key, or **Return** (keypad Enter too), runs the lit slice; on the hub Return opens the lit ring. Moving the pointer takes over from the keys. Mid-drag these keys do nothing new: a drop goes where the pointer is. Not the arrows, because on the Mac Control + arrows switch Spaces before an app sees them; they still work with Shift as the key. On Windows, Alt + arrows are free, so either would do, but the same letters keep the two apps alike.
- Mac files: `Wheel/Wheel.swift` (`WheelModel.step`, `WheelController.key` (key codes A 0, S 1, D 2, W 13), `track(force:)`, `lastMouse`), test in `Tests/LemonTests/UpscaleTests.swift` (`WheelStepTests`).

---

## 2026-10-27: Control is the Mac's wheel key

| Windows | Mac |
|---|---|
| **Won't do**: Windows has its own default (Alt), and Ctrl can't be picked there | **Done** (the original) |

- The Mac's default wheel key is now **Control**, held (was Shift). Settings saved Shift as "the default", so everyone on Shift moves to Control; Shift can still be picked, as can a pressed key. Control works like Shift did: held mid-drag, or held alone for 0.7 s over a Finder selection, read off the mouse / modifier events, no Accessibility permission needed mid-drag. Control (left or right) is no longer refused when picking a key.
- Mac files: `Settings/Customize.swift` (`WheelKey.control`, `flag`, `held`, `Prefs.key`), `Wheel/Wheel.swift` (drag trigger, `holdEvent`, `track`, Tab), `App/Welcome.swift`.

---

## 2026-10-26: Custom Resize presets (keep the format)

| Windows | Mac |
|---|---|
| **Done** (the original) | **Done** |

- The preset editor's format list starts with **Keep the original format (resize pictures)**: a max width and/or height (Save stays disabled until one is set, or the preset would be an Optimize one), plus optional quality and metadata stripping. The preset counts as Resize (same format, shrink only, "Already small enough", `name (N px).ext`) and goes on the Resize ring, ahead of the built-in sizes.
- The built-in *Resize – 4K/Full HD/HD/SD* now show in the preset list (read-only), so they can be duplicated and resized. Upscale, Optimize and Remove background stay out of the list. The editor's "Convert to" is now "Format".
- Windows files: `Lemon.App/ViewModels/PresetsViewModel.cs` (`KeepFormat`, `NeedsSize`), `Views/PresetsPage.xaml`.
- Mac files: `Settings/Customize.swift` (`PresetEditor.needsSize`/`existing`, `PresetSettings`' *Built-in sizes* section), `Conversion/Presets.swift` (`presets(for:)`), `Wheel/Wheel.swift` (Resize ring order). Came out different on Mac: one *Longest side* box instead of a max width and/or height (as the Mac's other picture presets have), and the built-in sizes are a *Built-in sizes* section with *Duplicate* under the custom presets. The editor was already called "Format". Before this, a Mac custom preset could only resize in its own format when the source was already that format.

---

## 2026-10-26: Hold the wheel key over selected files; Alt is the Windows key

| Windows | Mac |
|---|---|
| **Done** (the original) | **Done** (Shift only) |

- Select files or folders in File Explorer or on the desktop and hold the wheel key alone for 0.7 s: the wheel opens under the pointer for them. Keep holding and point; let go on a slice to run it (a click works too). Letting go on the lemon after moving off it adds them to the queue. Letting go anywhere else, or on the lemon without moving, closes the wheel. Tab / number keys / scrolling / ring names and Esc work as in a drag. Another key, a mouse button or a scroll during the hold cancels it (it was a modifier). So does renaming a file (Win11's address and search boxes aren't plain edit boxes, so they aren't caught). Folders open on Commands as in a drag.
- Windows' default wheel key is now **Alt** (was Space; Space moves to Alt once, a key picked by hand stays). Alt and Shift can be picked; Ctrl and Win can't. With Shift as the key, Tab always goes forward (Shift is held). Letting go of a held Alt sends a key that does nothing first, so Explorer's menu doesn't open. Tab with the wheel up never reaches Alt+Tab.
- Mac: Shift stays its key.
- Mac files: `Wheel/Wheel.swift` (`WheelController.holdEvent`, `finderSelection`, `track`, `release`, `typing`), `packaging/make-app.sh` (`NSAppleEventsUsageDescription`). Came out different on Mac: only with Shift as the wheel key (a key like Space would type into Finder, or open Quick Look) and only over Finder (which is the desktop too). The hold starts from a global flagsChanged monitor (the only always-on one: no keyDown monitor waking Lemon for every keystroke); when it fires, `CGEventSource.secondsSinceLastEventType` says whether a key, click or scroll came meanwhile, which cancels it; once the wheel is up, a 60 Hz timer follows the pointer and Shift, since the wheel's own panel takes the events then. The selection comes from `osascript` asking Finder (1 s timeout), after `AEDeterminePermissionToAutomateTarget` has asked for Automation permission to control Finder (once, without a time limit, so the question isn't cut off). A focused text field (rename, the search box, read through Accessibility) means typing, so no wheel. Tab always goes forward. Nothing to mask for Alt.
- Windows files: `Lemon.Core/Wheel/KeyHold.cs` (+ test), `Lemon.App/Wheel/DragGestureMonitor.cs` (hold timer on the hook thread, Alt masking, click to pick), `Wheel/ExplorerSelection.cs` (Shell.Application, the active tab through IShellBrowser, the desktop through FindWindowSW, on an STA thread with a 1 s timeout), `Wheel/WheelWindow.xaml.cs` (`ShowForSelection`, `PickAsync`, `Act` shared with drops), `App.xaml.cs` (`KeyName`, the move to Alt), `Views/WheelTriggers.xaml.cs`.

---

## 2026-10-26: Review fixes (queue default, idle memory)

| Windows | Mac |
|---|---|
| **Done** (the original) | **Done** (checked) |

- **Default preset for new files.** Dropping a file that's already waiting in the queue on a wheel slice (or a right-click *To PNG* on it) made that slice the default for every file of that kind added later, e.g. new pictures defaulting to *Upscale 4×*. The fallback chosen after a preset change did the same. Only picking a preset in the list (or *Set all to*) changes the default now.
- **Memory after a batch.** Idle in the tray, Lemon hardly allocates, so .NET never collected after a batch, and the GC kept hundreds of MB committed. 40 s after a batch, once the AI models are let go, Lemon runs one aggressive collection (measured after upscaling: 394 → 182 MB private). Mac: check what Lemon holds after a batch.
- Mac, checked: the default-preset problem doesn't exist (nothing remembers a default; each new file starts on its first Convert preset, and a wheel drop only sets the jobs it runs). Swift frees memory as it goes, and BiRefNet's sessions were already let go 20 s after use. Upscale's Core ML memory isn't let go with its session (measured 1.3 GB after a 4×, 2.4 GB after x4plus), so each upscale runs in a child process: see the AI upscale entry. Unreadable prefs already fall back to defaults. *Delete* in *Settings › AI models* now reports a file it couldn't delete. The Mac's notification has no per-action verb ("3 of 3 done"), so there's no "Upscaled" to add.
- Windows only: the wheel lets go of its blurred backdrop when it closes. The mouse/keyboard hooks only read event details when needed. A locked or unreadable `settings.json` / `presets.json` falls back to defaults instead of stopping Lemon from starting. The finished-batch notification says "Upscaled …". Upscaling a CMYK picture runs the model on sRGB and converts back, instead of treating C, M and Y as colors and leaving K blocky. *Remove* in Settings › AI models reports a file it couldn't delete. The package drops DirectML's debug DLL and symbols (14 MB).

---

## 2026-10-26: AI upscale on the Resize ring

| Windows | Mac |
|---|---|
| **Done** (the original) | **Done** |

- The Resize ring gets four Upscale choices: *2×* and *4×*, each as *AI upscale* (Real-ESRGAN General, `realesr-general-x4v3`, 5 MB) and *Best quality* (`RealESRGAN_x4plus`, 67 MB). Ring order: 4× Best, 4×, 2× Best, 2×, then 4K … SD. Each one appears only once its model is downloaded. The ring's description is now "Shrink or upscale images".
- Same format out, saved as `name (2×).ext`. The model always works at 4× in 256 px tiles (16 px of overlap, cropped off). *2×* averages each 2×2 block of the tile's result. Color profile and metadata come from the original enlarged with a quick Scale, and the model's RGB replaces its colors. Transparency is the original's alpha enlarged with Lanczos. Speed (RTX 5070, 1000×625 photo, warm): 4× 1.2 s, 2× 0.65 s, 4× x4plus 3.2 s. 512 px tiles weren't faster and took up to 11 GB of video memory, and fp16 exports failed on DirectML. The result is capped at 16383 px on its long side. Not for GIF, ICO, PDF or animated WebP ("Can't upscale animated pictures."). The wheel says "Upscaling 1 file 4×" and the queue says "Upscaling".
- Models: the official BSD-3 weights exported to ONNX (fp32, opset 17, any input size) by `windows/packaging/export-upscale-models.py`, which checks the upstream `.pth` hashes and compares the ONNX output to PyTorch. They're published as assets of the `models-v1` GitHub release. Input is RGB 0..1 with no normalization, and the output is clamped to 0..1. The Mac can load the same `.onnx` files with ONNX Runtime, as it does for BiRefNet.
- *Settings › Background remover* became *Settings › AI models* and lists all four models.
- Windows only (the Mac has no installer): the installer's model checkboxes moved off the tasks page onto an **Optional downloads** page. It has a Converters group (FFmpeg, Pandoc, LibreOffice through `winget` after the install, each in a console window; Ghostscript opens its download page) and an AI models group (all four; BiRefNet Lite and Real-ESRGAN General ticked; ones already there greyed out as *downloaded*). The Ready page lists the choices. Silent installs (winget) skip it all.
- Mac files: `Conversion/Upscale.swift` (`Upscale.run`, `tiled` + `UpscaleTests`, `inChild`/`child`, `Gate`), `Conversion/Background.swift` (`AIModel`, was `BiRefNet`, now with the Real-ESRGAN entries; `ModelRunner`, was `BiRefNetRunner`, opens Real-ESRGAN on Core ML), `Conversion/Presets.swift` (`Upscaler`, `Preset.upscale/scale`, `upscalePresets`, `upscalable`), `Conversion/Convert.swift`, `Wheel/Wheel.swift` (label, symbol, ring order, "Shrink or upscale images"), `App/LemonApp.swift` (`ModelSettings`, `Entry`).
- Came out different on Mac: the model runs on Core ML (`MLProgram`, all compute units), with the CPU as a fallback. M2 Pro, 1000×1000 photo, release build, model load included: 4× 5.1 s, 2× 4.9 s, 4× x4plus 22 s (27 s with its 128 px tiles; CPU only: 12.7 s, 13.9 s, 211 s; Core ML's output is 72–97 dB PSNR from the CPU's). The Neural Engine alone was as fast for General but 2.7× slower for x4plus. Tiles are 256 px for General but 128 px for x4plus: at 256 px x4plus's child peaked at 4.4 GB (1.6 GB at 128 px, 12% slower); 512 px was no faster for either and took 6 and 15 GB. Core ML keeps its memory after the session goes, so each upscale runs in `Lemon --upscale <model> <scale> <in> <out>`, a child process that prints its progress and exits; one at a time. It lets go of its session before exiting, or Core ML's compiled model stays in the temp folder; Cancel's SIGTERM is caught and stops it at the next tile so that still happens. A Core ML model cache (`ModelCacheDirectory`) was measured and left out: 0.5 s off a 4–24 s run for 138 MB on disk. Pixels are read with Core Image in the picture's own RGB space (Display P3 stays P3; CMYK and gray go through sRGB), with straight alpha so see-through edges don't get halos. Metadata is copied with orientation reset to 1. ImageIO writes the result where it can; WebP (and AVIF where ImageIO can't) goes through a PNG and magick. An animated PNG is refused too, not only WebP. The wheel and the queue don't name the action ("Upscaling …"), as for every other action on the Mac.
- Windows files: `Lemon.Core/Models/ModelFile.cs` (shared download base), `Models/UpscaleModel.cs`, `Conversion/Converters/UpscaleConverter.cs`, `Presets/Preset.cs` (`Upscale`, `Scale`; Upscale presets count as `IsResize`), `Presets/BuiltInPresets.cs`, `Wheel/ConversionWheel.cs`, `Conversion/ConversionService.cs`, `Lemon.App/Views/SettingsPage`.

---

## 2026-10-25: Commands for files, and placeholder chips

| Windows | Mac |
|---|---|
| **Done** | **Done** (the original) |

- Commands can run on **Files** too (*Runs on: Files · Folders · Links*), with optional file types (`pdf, png`; empty means any). They run once per matching dragged file, in its folder. A file command also opens the wheel for files Lemon can't convert. Files still open on their usual ring.
- Placeholders are `{file}`, `{folder}`, `{files}` (the files directly inside the folder, sorted, no hidden ones, each quoted, one run per folder) and `{link}`. `{path}` / `{url}` still work.
- The command box shows each placeholder as a colored chip (File blue, Folder orange, Files in Folder purple, Link green), with buttons under it to insert the ones that fit *Runs on* at the cursor. Copy and paste carry the `{…}` text, and typed placeholders turn into chips. Smart quotes and dashes are off in the box.
- Mac files: `Wheel/Commands.swift` (`RunsOn`, `Token`, `WheelCommand.on/takes`), `Settings/ChipField.swift`, `Wheel/Wheel.swift` (`WheelModel.targets`), `Settings/Customize.swift` (`CommandEditor`). Saved commands from the first version (`forLinks`) still decode.
- Windows files: `Lemon.Core/Wheel/Commands.cs` (`RunsOn`, `WheelCommand.Takes`, `CommandToken`, `CommandLine.Tokenize/Build`), `Lemon.App/Views/CommandEditor.cs` (dialog and chip box), `Wheel/WheelWindow.xaml.cs` (`Targets`). Came out different: a chip is the `{file}` text itself, colored and bold in a RichEditBox, not a capsule with the name, and it deletes one character at a time. Undo can take the coloring off a chip until the next edit. `{files}` becomes a PowerShell list (`'a', 'b'`) so cmdlets get one array; programs still get separate arguments. Windows never had `forLinks`, so there's nothing old to decode.

---

## 2026-10-25: Commands ring for dragged folders and links

| Windows | Mac |
|---|---|
| **Done** | **Done** (the original) |

- *Settings › Wheel › Commands*: the user's own shell lines, each with a name, an icon, *Folders* or *Links*, and the line. Up to 8 of each kind go on a **Commands** ring, in Settings order. Not one of the hideable built-in rings, and not offered in *Opens on*. No commands means no ring.
- Dropping on one runs the line once per dragged item, in a login shell, without waiting. `{path}` / `{url}` become the item, single-quoted for the shell. A non-zero exit or launch error shows an alert with the command's name and the end of its stderr.
- A drag of only folders opens on Commands, even with nothing convertible inside. With a link command set up, a drag with an http(s) link counts as a link (even if the browser also adds a file) and gets only the Commands ring. Dropping a link on the lemon does nothing.
- Mac files: `mac/Sources/Lemon/Wheel/Commands.swift` (model, `commandLine`, `runCommand`), `Wheel/Wheel.swift` (`WheelMode.commands`, `WheelModel.load(_:folders:links:)`, `WheelController.open/show/pick`), `Settings/Customize.swift` (`CommandEditor`, Commands section), `Conversion/Presets.swift` (`Preset.command`).
- Windows files: `Lemon.Core/Wheel/Commands.cs`, `Wheel/ConversionWheel.cs` (`WheelRing.Commands`, `Plan(..., commands, onCommands)`), `Presets/Preset.cs` (`CommandId`), `Lemon.App/Wheel/CommandRunner.cs`, `Wheel/WheelWindow.xaml.cs` (link reading, command drops), `Views/WheelSettingsPage` (Commands card).
- Came out different on Windows: lines run in Windows PowerShell 5.1 (`powershell.exe -File` on a temp script, no window), and placeholders are single-quoted for PowerShell. There's no login shell. A failure shows a Windows notification from the tray icon, with the last 200 characters of stderr, instead of an alert. A link is read from `WebLink`, or from dragged text that is an http(s) link.

---

## 2026-10-24: Welcome demo shows the real wheel; wheel hints follow the settings

| Windows | Mac |
|---|---|
| **Done** | **Done** (the original) |

- The welcome guide's step 2 draws the actual wheel instead of a fixed JPG / PNG / WEBP / PDF drawing: the user's Convert ring for pictures (up to 8 formats, Settings order) in their theme and glass. The photo is dropped on the JPG wedge (the first wedge if JPG isn't on the ring), which lights up as in a real drag, and the new file is named with that format. The files are drawn with the system's own icons, and the dragged copy is see-through with a pointer on it.
- The empty main window ("Or shake or hold Shift while dragging files anywhere for the wheel") and the menu bar menu describe how the wheel opens right now: shaking, the key, or both, with the key's name.
- Mac files: `mac/Sources/Lemon/App/Welcome.swift` (`WheelDemo`), `Wheel/Wheel.swift` (`WheelModel.load(demo:)`), `Settings/Customize.swift` (`Prefs.summon`), `App/LemonApp.swift` (`MenuHint`).
- Windows files: `Views/WelcomePage.xaml(.cs)` (`ShowDemoWheel`, `ShowFileIcons`), `Views/WheelPreview.cs` (`RenderDemo`), `App.xaml.cs` (`WheelHint`, the tray tooltip), `Views/ConvertPage.xaml` (the empty queue's hint).
- Came out different on Windows: the demo disc has the theme's tint but no blur behind it, since there's nothing behind it on the page to blur. The hint goes in the tray icon's tooltip, because Windows has no menu bar menu. The file icons come from empty stand-in files in the temp folder.
