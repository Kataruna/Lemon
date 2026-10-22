# Lemon

A drag-and-drop file converter for Windows 11, in the spirit of Tangerine on macOS. Drop in images, audio, video or documents, pick a preset for each file (or for all of them at once), and convert the whole batch in parallel.

## Features

- **Conversion wheel.** While you drag files anywhere in Windows (Explorer, the desktop, a browser…), shake the mouse or press **Space** and a wheel of formats pops up under the pointer. Drop on a format to convert right away, next to the originals, or drop on the lemon in the middle to add the files to the queue. The key is only intercepted during an actual drag-and-drop. Lemon must be running (minimized is fine). Shake and key are configurable in Settings.
- **Batch queue.** Drag files or whole folders onto the window. Every file gets its own preset, and *Set all to* changes every file of one type at once. Files convert in parallel with live progress and can be cancelled or retried.
- **Presets.** Built-in presets such as "WebP – Web optimized", "MP3 – 320 kbps", "MP4 – 720p small" and "Animated GIF". You can duplicate any of them and change quality, max size, bitrate, video CRF, frame rate and metadata stripping.
- **Right-click menu.** *Convert with Lemon › Open in Lemon… / To PNG / To MP3 / …* in File Explorer. Turn it on in Settings. Selecting many files sends them all to one window.
- **Safe output.** The original is never overwritten. If the target name is taken, Lemon saves `name (1).ext`, and it deletes partial files when a conversion fails or is cancelled. Closing the window mid-batch asks first. Converter processes are tied to Lemon with a job object, so they never outlive it, even after a crash. A forcibly killed Lemon can still leave a partial file behind.

## What converts to what

| Input | Output | Engine |
|---|---|---|
| PNG, JPG, WebP, AVIF, HEIC/HEIF, GIF, BMP, TIFF, ICO | PNG, JPG, WebP, AVIF, GIF (animated), BMP, TIFF, ICO | ImageMagick (built in) |
| MP3, WAV, FLAC, M4A/AAC, OGG, Opus, WMA, AIFF | MP3, M4A, Opus, OGG, FLAC, WAV, AIFF | FFmpeg |
| MP4, MKV, WebM, MOV, AVI, WMV, M4V, FLV, animated GIF | MP4, MKV, WebM, MOV, GIF, or audio only | FFmpeg |
| Markdown, TXT | HTML, TXT, MD | Built in |
| DOCX, ODT, RTF, HTML, MD, EPUB | DOCX, ODT, RTF, HTML, MD, TXT, EPUB | Pandoc |
| DOCX, DOC, ODT, RTF, XLSX, PPTX, … | PDF (and DOCX/ODT/RTF) | LibreOffice |

HEIC is input-only, because the bundled ImageMagick has no HEVC encoder. Lemon finds FFmpeg, Pandoc and LibreOffice on `PATH` or in their usual install folders, and you can also point to them in Settings. Formats whose tool is missing are hidden, and the app tells you what to install:

```powershell
winget install Gyan.FFmpeg
winget install JohnMacFarlane.Pandoc
winget install TheDocumentFoundation.LibreOffice
```

## Build and run

Requires the .NET 10 SDK (pinned in `global.json`). Visual Studio isn't required.

```powershell
dotnet build                                   # x64; for ARM64: dotnet build src/Lemon.App -p:Platform=ARM64
src\Lemon.App\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\Lemon.exe
dotnet test --project tests/Lemon.Core.Tests
```

## Packaging

| Script | Output | |
|---|---|---|
| `make-installer.ps1` | `dist\Lemon-Setup.exe` | Per-user installer (no admin prompt) into `%LOCALAPPDATA%\Programs\Lemon`. It adds a Start menu shortcut and an uninstaller, with optional tasks for the right-click menu, starting with Windows, and a desktop icon. Needs Inno Setup 6 (`winget install JRSoftware.InnoSetup`). |
| `make-portable.ps1` | `dist\Lemon.exe` | The whole app in one file, nothing to install. The first launch unpacks WinUI's files to `%TEMP%\.net\Lemon` (about 2 s); later launches are instant. |
| `make-app.ps1` | `dist\app\` | The self-contained app folder that the installer packages. |
| `make-icon.cs` | `src\Lemon.App\Assets\Lemon.ico/png` | Redraws the icon: `dotnet run make-icon.cs -- src/Lemon.App/Assets` |

Neither package is code-signed yet, so SmartScreen warns on other PCs. FFmpeg, Pandoc and LibreOffice aren't bundled; Lemon finds them if they're installed.

Command line: `Lemon.exe [--background] [--to <ext>] <files or folders…>`. With `--to`, the files are converted right away without showing the window. The right-click menu uses this. `--background` starts in the notification area and is used for starting with Windows. Setup uses two more: `--register-context-menu` and `--unregister`.

## Layout

```
make-*.ps1, make-icon.cs        Build scripts (see Packaging)
installer/Lemon.iss             Inno Setup script
src/Lemon.Core/                 Everything that isn't UI
  Formats/                      Known file types and kinds (image, audio, video, document)
  Presets/                      Preset model, the built-in presets, user presets on disk
  Conversion/                   ConversionService: picks a converter, reserves a unique output name, cleans up
    Converters/                 ImageMagick, FFmpeg, Markdown, Pandoc, LibreOffice
  ExternalTools/                Finding and running ffmpeg/pandoc/soffice (killed with Lemon via a job object)
  Configuration/                Settings and JSON storage in %LOCALAPPDATA%\Lemon
  Shell/                        Explorer right-click menu, start with Windows
  Wheel/                        Shake detection and which presets go on the wheel
  LaunchRequest.cs              Command-line parsing
src/Lemon.App/                  WinUI 3 app (unpackaged, self-contained Windows App SDK)
  Program.cs, App.xaml          Single-instance startup, tray, error notifications
  Views/                        Main window and the Convert, Presets and Settings pages
  ViewModels/                   Queue scheduler, per-file job state, preset editor
  Wheel/                        Global drag-gesture hooks and the animated round format picker
  Shell/                        Notification-area (tray) icon and notifications
  Interop/                      Win32 declarations
tests/Lemon.Core.Tests/         Same folders as Lemon.Core; Conversion/ also converts real generated media
```
