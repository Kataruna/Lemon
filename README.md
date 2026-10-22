# Lemon

A drag-and-drop file converter for Windows 11, in the spirit of Tangerine on macOS. Drop in images, audio, video or documents, pick a preset for each file (or for all of them at once), and convert the whole batch in parallel.

## Features

- **Conversion wheel.** While you drag files anywhere in Windows (Explorer, the desktop, a browser…), shake the mouse or press **Space** and a wheel of formats pops up under the pointer. Drop on a format to convert right away, next to the originals, or drop on the lemon in the middle to add the files to the queue. The key is only intercepted during an actual drag-and-drop. Lemon must be running (minimized is fine). Shake and key are configurable in Settings.
- **Batch queue.** Drag files or whole folders onto the window. Every file gets its own preset, and *Set all to* changes every file of one type at once. Files convert in parallel with live progress and can be cancelled or retried.
- **Optimize.** Like Clop on macOS: pick *Optimize* (in a file's preset list, *Optimize all*, the wheel or the right-click menu) to make images, videos and PDFs smaller in the same format, with the quality kept close to the original. The optimized copy is saved as `name (optimized).ext` next to the original. If Lemon can't find a version that's clearly smaller, it keeps the original and says "Already optimized". See [How Optimize works](#how-optimize-works).
- **Presets.** Built-in presets such as "WebP – Web optimized", "MP3 – 320 kbps", "MP4 – 720p small" and "Animated GIF". You can duplicate any of them and change quality, max size, bitrate, video CRF, frame rate and metadata stripping.
- **Right-click menu.** *Convert with Lemon › Open in Lemon… / To PNG / To MP3 / …* in File Explorer. Turn it on in Settings. Selecting many files sends them all to one window.
- **Safe output.** The original is never overwritten. If the target name is taken, Lemon saves `name (1).ext`, and it deletes partial files when a conversion fails or is cancelled. Closing the window mid-batch asks first. Converter processes are tied to Lemon with a job object, so they never outlive it, even after a crash. A forcibly killed Lemon can still leave a partial file behind.

## What converts to what

| Input | Output | Engine |
|---|---|---|
| PNG, JPG, WebP, AVIF, HEIC/HEIF, GIF, BMP, TIFF, ICO | PNG, JPG, WebP, AVIF, GIF (animated), BMP, TIFF, ICO, PDF | ImageMagick (built in) |
| MP3, WAV, FLAC, M4A/AAC, OGG, Opus, WMA, AIFF | MP3, M4A, Opus, OGG, FLAC, WAV, AIFF | FFmpeg |
| MP4, MKV, WebM, MOV, AVI, WMV, M4V, FLV, animated GIF | MP4, MKV, WebM, MOV, GIF, or audio only | FFmpeg |
| Markdown, TXT | HTML, TXT, MD | Built in |
| DOCX, ODT, RTF, HTML, MD, EPUB | DOCX, ODT, RTF, HTML, MD, TXT, EPUB | Pandoc |
| DOCX, DOC, ODT, RTF, XLSX, PPTX, … | PDF (and DOCX/ODT/RTF) | LibreOffice |

| JPEG, PNG, GIF, WebP | Same format, smaller (Optimize) | ImageMagick (built in) |
| MP4, M4V, MOV, MKV | Same format, smaller (Optimize) | FFmpeg |
| PDF | Same format, smaller (Optimize) | Ghostscript |

HEIC is input-only, because the bundled ImageMagick has no HEVC encoder. Images become one PDF page each, and a multi-page TIFF becomes a multi-page PDF. Pages keep the image's print size when it fits on A4; anything larger is scaled down to fit. Photos are stored as JPEG inside the PDF; screenshots and graphics stay lossless. Reading PDFs needs Ghostscript, so PDFs can't be converted to images. Lemon finds FFmpeg, Pandoc, LibreOffice and Ghostscript on `PATH` or in their usual install folders, and you can also point to them in Settings. Formats whose tool is missing are hidden, and the app tells you what to install:

```powershell
winget install Gyan.FFmpeg
winget install JohnMacFarlane.Pandoc
winget install TheDocumentFoundation.LibreOffice
```

Ghostscript isn't on winget. Get the Windows installer from [ghostscript.com](https://ghostscript.com/releases/gsdnld.html).

## How Optimize works

- **JPEG.** Lossless first: progressive encoding with optimized Huffman tables. If the file was saved above quality 85, Lemon also tries a quality-85 encode.
- **PNG.** Lossless recompression first. If the image has more than 256 colors, Lemon also tries a 256-color palette with alpha kept, like pngquant.
- **WebP.** Lemon tries a quality-80 encode.
- **GIF.** Lossless recompression only, and every frame is kept. Animated WebP and animated PNG are left alone.
- **Lossy versions.** A lossy version is only used when it saves at least 10% more than the lossless one and passes a structural-similarity check (DSSIM ≤ 0.01) against the original, so it looks the same.
- **Video.** The picture is re-encoded as H.264 (CRF 23, `slow`) and the audio is copied untouched. MKV subtitles and attachments are kept. HEVC, AV1 and VP9 videos, and anything that is HDR or not 8-bit 4:2:0, are left alone: H.264 wouldn't make them smaller, or it would quietly lose color. A new video is only kept if it's at least 10% smaller.
- **PDF.** Ghostscript subsets and compresses fonts, merges duplicate images and downsamples images above 300 dpi to 200 dpi. Text and vector graphics stay sharp. A new PDF is only kept if it's at least 10% smaller. Ghostscript rewrites the whole file, so digital signatures stop validating and accessibility tags are lost. Don't optimize signed or tagged PDFs.
- **Audio** isn't optimized. Re-encoding lossy audio only loses quality.

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

Command line: `Lemon.exe [--background] [--to <ext> | --optimize] <files or folders…>`. With `--to` or `--optimize`, the files are converted right away without showing the window. The right-click menu uses this. `--background` starts in the notification area and is used for starting with Windows. Setup uses two more: `--register-context-menu` and `--unregister`.

## Layout

```
make-*.ps1, make-icon.cs        Build scripts (see Packaging)
installer/Lemon.iss             Inno Setup script
src/Lemon.Core/                 Everything that isn't UI
  Formats/                      Known file types and kinds (image, audio, video, document)
  Presets/                      Preset model, the built-in presets, user presets on disk
  Conversion/                   ConversionService: picks a converter, reserves a unique output name, cleans up
    Converters/                 ImageMagick, FFmpeg, Markdown, Pandoc, LibreOffice, and the Optimize converters
  ExternalTools/                Finding and running ffmpeg/pandoc/soffice/gswin64c (killed with Lemon via a job object)
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
