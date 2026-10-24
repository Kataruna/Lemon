# Installing Lemon

Lemon is on the [Releases page](https://github.com/Kataruna/Lemon/releases). Windows and Mac are released separately: pick the newest release tagged `windows-v…` or `mac-v…`.

- [Windows](#windows)
  - [Installer or portable?](#installer-or-portable)
  - [Install with the installer](#install-with-the-installer)
  - [Run the portable version](#run-the-portable-version)
  - [The SmartScreen warning](#the-smartscreen-warning)
  - [First steps](#first-steps-on-windows)
  - [Converters](#converters-on-windows)
  - [Remove background models](#remove-background-models-on-windows)
  - [Updating and uninstalling](#updating-and-uninstalling-on-windows)
- [Mac](#mac)
  - [Install](#install-on-mac)
  - [Opening it the first time](#opening-it-the-first-time)
  - [Accessibility permission](#accessibility-permission)
  - [First steps](#first-steps-on-mac)
  - [Converters](#converters-on-mac)
  - [Remove background models](#remove-background-models-on-mac)
  - [Updating and uninstalling](#updating-and-uninstalling-on-mac)
- [Troubleshooting](#troubleshooting)

## Windows

You need Windows 10 (version 2004 or later) or Windows 11, on a 64-bit PC. Nothing else has to be installed first: .NET and the Windows App SDK are built in.

### Installer or portable?

| | **Lemon-x.y.z-Setup.exe** | **Lemon-x.y.z-Portable.exe** |
|---|---|---|
| What it is | Installs Lemon for your user | One file you run from anywhere |
| Admin rights | Not needed | Not needed |
| Start menu entry, uninstaller | Yes | No |
| Right-click *Convert with Lemon* menu | Ticked in setup, or in Settings | In Settings |
| Start with Windows | Ticked in setup, or in Settings | In Settings |
| Background-remover models | Setup can download them | Downloaded in Settings |
| First launch | Instant | About 2 s while it unpacks to `%TEMP%\.net\Lemon` (once per version) |

Pick the installer unless you want to carry Lemon on a USB stick or try it without installing. Both keep their settings in `%LOCALAPPDATA%\Lemon`, so you can switch between them.

### Install with the installer

1. Download **Lemon-x.y.z-Setup.exe** from the release.
2. Run it. If Windows shows a blue SmartScreen box, see [the SmartScreen warning](#the-smartscreen-warning).
3. Choose the options you want:
   - **Add "Convert with Lemon" to the right-click menu**: convert files straight from File Explorer.
   - **Start Lemon in the notification area when I sign in**: keeps the wheel ready at all times. The wheel only works while Lemon is running.
   - **Download the AI model for Remove background**: BiRefNet Lite (224 MB) is ticked; the full model (973 MB) is optional. You can add or remove them later in Settings.
   - **Create a desktop shortcut**.
4. Finish, and Lemon starts. It installs to `%LOCALAPPDATA%\Programs\Lemon`, for your user only.

### Run the portable version

1. Download **Lemon-x.y.z-Portable.exe** and put it wherever you like, for example in `Documents\Apps`.
2. Double-click it. The first launch takes a couple of seconds while it unpacks; later launches are instant.
3. Optional, in **Settings** (the gear at the top right): the right-click menu, starting with Windows, and the background-remover models.

The right-click menu and start with Windows point at the file where it is now. If you move or rename the file, switch those settings off and back on.

### The SmartScreen warning

Lemon isn't code-signed yet, so the first time you run a download, Windows may say *"Windows protected your PC"*. Click **More info**, then **Run anyway**. Windows only asks once per file.

To check the download first, compare its SHA-256 (shown next to each file on the release page) with:

```powershell
Get-FileHash .\Lemon-0.3.0-Setup.exe -Algorithm SHA256
```

### First steps on Windows

The first time Lemon opens, a short welcome guide asks what you want to convert besides pictures (each kind of file has an **Add** button that installs its converter with `winget`), then shows how to open the wheel. Skip it if you like; *Settings › About › Show the welcome guide again* brings it back.

- **Convert a batch:** drop files or folders onto the window (or click *Add files*), pick a preset for each file or use *Set all to*, then click **Convert**. Files are saved next to the originals; *Settings › Output* can send them to a folder instead.
- **Use the wheel:** with Lemon running (minimized or in the notification area is fine), start dragging files anywhere, then **shake the mouse** or press **Space**. Drop on a format, or on the lemon to queue the files.
- **Closing the window** keeps Lemon in the notification area (the lemon by the clock), so the wheel keeps working. Right-click that lemon to quit.

### Converters on Windows

Pictures (PNG, JPEG, WebP, AVIF, GIF, TIFF, BMP, ICO, HEIC input, PDF output) work out of the box, and so do Markdown and plain text to HTML or text. The rest uses free tools that Lemon finds once they're installed. The welcome guide installs them for you, or run:

| To convert | Install | Command |
|---|---|---|
| Audio and video, and Optimize for videos | FFmpeg | `winget install Gyan.FFmpeg` |
| Documents (DOCX, ODT, RTF, HTML, Markdown, EPUB) | Pandoc | `winget install JohnMacFarlane.Pandoc` |
| Office files to PDF (DOCX, XLSX, PPTX, …) | LibreOffice | `winget install TheDocumentFoundation.LibreOffice` |
| Optimize for PDFs | Ghostscript | Installer from [ghostscript.com](https://ghostscript.com/releases/gsdnld.html) (not on winget) |

No restart needed: Lemon looks again each time you open the wheel or go back to the Convert tab, or when you click *Check again* in **Settings › Converters**. If a tool lives somewhere unusual, click **Locate…** next to it there.

### Remove background models on Windows

*Cut out* runs an AI model on your PC; no picture leaves it. The models are downloaded once, in **Settings › Background remover** or by the installer:

- **BiRefNet Lite**, 224 MB: fast, good for most pictures. Recommended.
- **BiRefNet**, 973 MB: the best edges on hair and fine detail, but slower and needs much more memory.

They're checked against a fixed SHA-256 and stored in `%LOCALAPPDATA%\Lemon\models`. Lemon uses your graphics card when it can. Until a model is downloaded, the Cut out ring doesn't appear.

### Updating and uninstalling on Windows

Lemon tells you when a new Windows version is out (on the Convert page and in *Settings › About*).

- **Update with the installer:** run the new Setup over the old version. If Lemon is running, setup asks you to quit it first (right-click the lemon by the clock › *Quit Lemon*). Settings, presets and models are kept.
- **Update the portable version:** quit Lemon and replace the file. If the file name changed and you use the right-click menu or start with Windows, switch those off and on again.
- **Uninstall the installed version:** *Settings › Apps › Installed apps › Lemon › Uninstall*. This also removes the right-click menu and start with Windows.
- **Remove the portable version:** switch off the right-click menu and start with Windows in Lemon's Settings, quit Lemon and delete the file (and `%TEMP%\.net\Lemon`).

Either way, settings, presets and models stay in `%LOCALAPPDATA%\Lemon` in case you come back. Delete that folder to remove them too.

## Mac

You need a Mac with **Apple silicon** (M1 or later) and **macOS 14 Sonoma** or later. Intel Macs aren't supported by the release build.

### Install on Mac

1. Download **Lemon-x.y.z-mac.zip** from the release and double-click it to unzip.
2. Drag **Lemon.app** into your **Applications** folder (replace the old one when updating).
3. Open it, and see the next section the first time.

### Opening it the first time

Lemon isn't from the App Store or notarized by Apple, so macOS stops it the first time:

- **macOS 15 Sequoia or later:** a dialog says *"Lemon" Not Opened*. Click **Done**, open **System Settings › Privacy & Security**, scroll down to *"Lemon" was blocked to protect your Mac*, click **Open Anyway** and confirm. Then click **Open Anyway** once more.
- **macOS 14 Sonoma:** in Finder, Control-click Lemon.app, choose **Open**, then click **Open** in the dialog.
- Or, in Terminal: `xattr -dr com.apple.quarantine /Applications/Lemon.app`, then open Lemon normally.

You only do this once per version.

### Accessibility permission

Lemon asks for **Accessibility** access (*System Settings › Privacy & Security › Accessibility*). It's only used for keys while you drag: Tab, the number keys and Esc on the wheel, and a wheel key other than Shift. Shaking and holding Shift work without it, and so do scrolling and the ring names under the wheel.

### First steps on Mac

The first time Lemon opens, a welcome guide lists the kinds of files it converts. Each one shows **Ready**, or an **Add** button that installs its converter with Homebrew in a Terminal window (or *Get it…*, which opens [brew.sh](https://brew.sh) when Homebrew isn't installed). Then an animation on the real wheel shows how to use it. **Skip** is at the top right; *Settings › About › Show the welcome guide again* brings it back.

- **Convert a batch:** drop files or folders onto the window (or *Choose Files…*), pick a preset per file or use *Set all to* in the toolbar, then click **Convert**.
- **Use the wheel:** start dragging files from Finder or anywhere, then **shake the mouse** or **hold Shift**. Drop on a format, or on the lemon to queue the files.
- **Closing the window** keeps Lemon in the menu bar (the lemon icon), so the wheel keeps working. Quit from that menu.

### Converters on Mac

DOCX, DOC, ODT, RTF, HTML and plain text convert between each other out of the box, with macOS's own `textutil`. Everything else uses free tools from [Homebrew](https://brew.sh). The welcome guide installs them, or run:

| To convert | Command |
|---|---|
| Pictures, Resize, and Optimize for pictures | `brew install imagemagick` |
| Audio and video, and Optimize for videos | `brew install ffmpeg` |
| Documents and ebooks (Markdown, EPUB, …) | `brew install pandoc` |
| Office files to PDF | `brew install --cask libreoffice` |
| Optimize for PDFs | `brew install ghostscript` |

Formats appear as soon as their tool is found; no restart needed.

### Remove background models on Mac

*Cut out* works right away with **Apple Vision**. The BiRefNet models (better on large pictures and fine edges) are downloaded in **Settings › Background**, checked against a fixed SHA-256 and stored in `~/Library/Application Support/Lemon/models`.

### Updating and uninstalling on Mac

Lemon tells you when a new Mac version is out (at the top of the window and in *Settings › About*). To update, quit Lemon from the menu bar and replace **Lemon.app** in Applications with the new one; you may need to [open it the first time](#opening-it-the-first-time) again.

To uninstall, quit Lemon and move Lemon.app to the Trash. To remove its data too, delete `~/Library/Application Support/Lemon` and run `defaults delete com.sarabunari.lemon`.

## Troubleshooting

- **The wheel doesn't appear.** Lemon has to be running (the lemon by the clock on Windows, in the menu bar on Mac). Check *Opening the wheel* in Settings, and that you're dragging actual files, not text or a picture from a web page. Keys are only taken over while you drag.
- **A key on the wheel does nothing (Mac).** Lemon needs [Accessibility permission](#accessibility-permission). After allowing it, the next drag picks it up.
- **A format is missing.** That kind of file needs one of the converters above; the welcome guide (*Settings › About*) shows which are ready.
- **Remove background is missing.** On Windows, download a model in *Settings › Background remover*. On Mac, Apple Vision is always there; BiRefNet appears once downloaded.
- **Something failed.** The file's row shows the error. On Windows, details also go to `%LOCALAPPDATA%\Lemon\errors.log`, useful to attach when [reporting an issue](https://github.com/Kataruna/Lemon/issues).
