# Installing Lemon

Lemon is on the [Releases page](https://github.com/Kataruna/Lemon/releases). Windows releases are tagged `windows-v…`, so pick the newest of those.

- [Windows](#windows)
  - [Installer or portable?](#installer-or-portable)
  - [Install with the installer](#install-with-the-installer)
  - [Run the portable version](#run-the-portable-version)
  - [The SmartScreen warning](#the-smartscreen-warning)
  - [First steps](#first-steps)
  - [Optional tools for audio, video and documents](#optional-tools-for-audio-video-and-documents)
  - [Remove background models](#remove-background-models)
  - [Updating](#updating)
  - [Uninstalling](#uninstalling)
- [macOS](#macos)
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

Pick the installer unless you want to carry Lemon on a USB stick or try it without installing. Both versions keep their settings in the same place (`%LOCALAPPDATA%\Lemon`), so you can switch between them.

### Install with the installer

1. Download **Lemon-x.y.z-Setup.exe** from the release.
2. Run it. If Windows shows a blue SmartScreen box, see [the SmartScreen warning](#the-smartscreen-warning).
3. Choose the options you want:
   - **Add "Convert with Lemon" to the right-click menu**: convert files straight from File Explorer.
   - **Start Lemon in the notification area when I sign in**: keeps the conversion wheel ready at all times. The wheel only works while Lemon is running.
   - **Download the AI model for Remove background**: BiRefNet Lite (224 MB) is ticked by default; the higher-quality model (973 MB) is optional. Both need an internet connection, and you can add or remove them later in Settings.
   - **Create a desktop shortcut**.
4. Finish, and Lemon starts.

Lemon installs to `%LOCALAPPDATA%\Programs\Lemon`, for your user only.

### Run the portable version

1. Download **Lemon-x.y.z-Portable.exe** and put it wherever you like, for example in `Documents\Apps`.
2. Double-click it. The first launch takes a couple of seconds while it unpacks; later launches are instant.
3. Optional, in **Settings** (the gear at the top right):
   - *Show "Convert with Lemon" when you right-click a file*
   - *Start Lemon when I sign in to Windows*
   - *Background remover*: download a model.

Both the right-click menu and start with Windows point at the file where it is now. If you move or rename the file, switch those settings off and back on.

### The SmartScreen warning

Lemon isn't code-signed yet, so the first time you run a download, Windows may say *"Windows protected your PC"*. Click **More info**, then **Run anyway**. Windows only asks once per file.

If you'd rather check the download first, its SHA-256 is shown next to each file on the release page. Compare it with:

```powershell
Get-FileHash .\Lemon-0.2.0-Setup.exe -Algorithm SHA256
```

### First steps

- **Convert a batch:** open Lemon, drop files or folders onto the window (or click *Add files*), pick a preset for each file or use *Set all to*, then click **Convert**. Files are saved next to the originals; *Settings › Output* can send them to a folder instead.
- **Use the conversion wheel:** while Lemon is running (minimized or in the notification area is fine), start dragging files anywhere, for example from File Explorer or the desktop. Then **shake the mouse** or press **Space**, and the wheel pops up under the pointer. Drop on a format to convert straight away, or on the lemon in the middle to add the files to the queue. Switch rings with the chips under the wheel, **Tab**, the number keys or the scroll wheel.
- **Make the wheel yours:** the **Wheel** tab sets its look, its rings and what each kind of file opens on, with a live preview.
- **Closing the window** keeps Lemon in the notification area (the lemon by the clock), so the wheel keeps working. Right-click that lemon to quit. You can turn this off in Settings.

### Optional tools for audio, video and documents

Images (PNG, JPEG, WebP, AVIF, GIF, TIFF, BMP, ICO, HEIC input, PDF output) work out of the box, and so do Markdown and plain text to HTML or text. Other formats use free tools that Lemon finds once they're installed. Lemon tells you which one is missing when you add a file it can't convert yet.

| To convert | Install | Command (in PowerShell or Terminal) |
|---|---|---|
| Audio and video, and Optimize for videos | FFmpeg | `winget install Gyan.FFmpeg` |
| Documents (DOCX, ODT, RTF, HTML, Markdown, EPUB) | Pandoc | `winget install JohnMacFarlane.Pandoc` |
| Office files to PDF (DOCX, XLSX, PPTX, …) | LibreOffice | `winget install TheDocumentFoundation.LibreOffice` |
| Optimize for PDFs | Ghostscript | Installer from [ghostscript.com](https://ghostscript.com/releases/gsdnld.html) |

You don't need to restart Lemon: it looks for tools again each time you open the wheel or go back to the Convert tab, or when you click *Check again* in **Settings › Converters**. If a tool lives somewhere unusual, click **Locate…** next to it there.

### Remove background models

*Cut out* (Remove background) runs an AI model on your PC; no picture leaves it. The models are too big to include, so they're downloaded once:

- **BiRefNet Lite**, 224 MB: fast, good for most pictures. Recommended.
- **BiRefNet**, 973 MB: the best edges on hair and fine detail, but slower and needs a lot more memory.

Download or remove them in **Settings › Background remover** (or tick them in the installer). They're checked against a fixed SHA-256 and stored in `%LOCALAPPDATA%\Lemon\models`. Lemon uses your graphics card when it can, and the processor otherwise. Until a model is downloaded, the Cut out ring doesn't appear.

### Updating

- **Installer:** download the new Setup and run it over the old version. If Lemon is running, setup asks you to quit it first (right-click the lemon by the clock › *Quit Lemon*). Your settings, presets and models are kept.
- **Portable:** quit Lemon and replace the old file with the new one. If you use the right-click menu or start with Windows and the file name changed (it includes the version), switch those settings off and on again.

### Uninstalling

- **Installer:** *Settings › Apps › Installed apps › Lemon › Uninstall*. This also removes the right-click menu and start with Windows.
- **Portable:** first switch off the right-click menu and start with Windows in Lemon's Settings, then quit Lemon and delete the file. You can also delete `%TEMP%\.net\Lemon`.

Either way, your settings, presets and background-remover models stay in `%LOCALAPPDATA%\Lemon` in case you come back. Delete that folder to remove them too.

## macOS

There's no macOS download yet. To build the Mac version yourself (macOS 14 or later, with Xcode's command line tools), see [macOS version](README.md#macos-version) in the README.

## Troubleshooting

- **The wheel doesn't appear.** Lemon has to be running: look for the lemon by the clock. Check that *Open it by shaking the mouse* or a key is set in **Settings › Conversion wheel**, and that you're dragging actual files (not text or a picture from a web page). The key is only taken over while you drag.
- **A file is skipped or a format is missing.** That kind of file needs one of the [optional tools](#optional-tools-for-audio-video-and-documents); Lemon names the missing one at the top of the queue.
- **Remove background is missing from the wheel.** Download a model in **Settings › Background remover**.
- **Something failed.** The queue shows the error on the file's row. Lemon also writes details to `%LOCALAPPDATA%\Lemon\errors.log`, which is useful to attach when [reporting an issue](https://github.com/Kataruna/Lemon/issues).
