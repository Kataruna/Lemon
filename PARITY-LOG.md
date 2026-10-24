# Parity Log

Lemon has two separate apps: `windows/` (C# / WinUI) and `mac/` (Swift / SwiftUI). They share no code, so a change on one side has to be redone on the other by hand. This log tracks what changed on each side and whether the other side has it yet.

**How to use it**
- When you change a user-facing behavior on one side, add an entry at the top of the log.
- When you port it, change the other side's status to **Done** and note anything that came out different.
- To catch one side up, look for entries marked **To do** for that side.

Status: **Done** · **To do** · **Won't do** (with the reason) · **Partly** (say what's missing).

---

## 2026-10-24: Welcome demo shows the real wheel; wheel hints follow the settings

| Windows | Mac |
|---|---|
| **To do** | **Done** (the original) |

- The welcome guide's step 2 draws the actual wheel instead of a fixed JPG / PNG / WEBP / PDF drawing: the user's Convert ring for pictures (up to 8 formats, Settings order) in their theme and glass. The photo is dropped on the JPG wedge (the first wedge if JPG isn't on the ring), which lights up as in a real drag, and the new file is named with that format. The files are drawn with the system's own icons, and the dragged copy is see-through with a pointer on it.
- The empty main window ("Or shake or hold Shift while dragging files anywhere for the wheel") and the menu bar menu describe how the wheel opens right now: shaking, the key, or both, with the key's name.
- Mac files: `mac/Sources/Lemon/App/Welcome.swift` (`WheelDemo`), `Wheel/Wheel.swift` (`WheelModel.load(demo:)`), `Settings/Customize.swift` (`Prefs.summon`), `App/LemonApp.swift` (`MenuHint`).
- Windows notes: `WelcomePage.xaml` draws its own wheel; reusing `WheelPreview` (as Settings › Wheel does) would match the real one.
