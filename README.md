# Lemon

A drag-and-drop file converter for Windows 11. Drop in images, audio, video or documents, pick a preset for each file (or for all of them at once), and convert the whole batch in parallel.

**Download:** see the [Releases page](https://github.com/Kataruna/Lemon/releases) and the [install guide](INSTALL.md). When it starts, the Windows app checks this repository's Releases page for a newer Windows version and shows a notice with a download link if there is one; nothing else is sent. Turn it off in *Settings › About*.

## Features

- **Conversion wheel.** While you drag files anywhere in Windows (Explorer, the desktop, a browser…), shake the mouse or press **Space** and a wheel of formats pops up under the pointer. Drop on a format to convert right away, next to the originals, or drop on the lemon in the middle to add the files to the queue. The key is only intercepted during an actual drag-and-drop. **Esc** closes the wheel and keeps the drag going (shake or press the key to bring it back); with no wheel showing, Esc cancels the drag as usual. Lemon must be running (minimized is fine). In Settings you can turn off shaking or the key (not both) and set the key to any key except Esc, the number keys and the modifiers.
- **Customizing the wheel.** The *Wheel* tab, with a live preview at the top, sets the look (themes including your Windows accent color, accent tint, glass blur from *None* to *Heavy*, opacity, the emoji in the middle, text color), which built-in rings appear, rings of your own (up to 5, mixing any presets: formats, Optimize, Resize, Remove background, your own presets), which ring each kind of file opens on or the hub, and the Convert ring per kind of file (up to 8, in your order). Under the ring, a mode bar has a chip per ring; rest the files on one, press its number or **Tab**, or scroll to switch. With more than four rings (or *Open on the hub*), the hub shows the rings themselves as slices: rest on one to open it; inside a ring, rest on the middle (or press **0**) to come back, or turn on *Back to the hub from a chip under the ring* to use a chip instead. Blur is a snapshot of what's behind the wheel taken as it opens (Windows' own blur backdrop can't follow the wheel's round, anti-aliased edge), so video playing behind it doesn't move in the glass.
- **Batch queue.** Drag files or whole folders onto the window. Every file gets its own preset, and *Set all to* changes every file of one type at once. Files convert in parallel with live progress and can be cancelled or retried.
- **Progress in the tray.** While files convert, the lemon in the notification area turns into a progress ring; hover it for "Removing background · 2 of 5 done · 40%". When a batch finishes while Lemon's window isn't in front, a notification says what was done (or what failed and why); click it to open Lemon. Remove background can't report progress from the AI model, so Lemon estimates it from how long the previous run took.
- **Optimize.** Like Clop on macOS: pick *Optimize* (in a file's preset list, *Optimize all*, the wheel or the right-click menu) to make images, videos and PDFs smaller in the same format, with the quality kept close to the original. There are three strengths: *Lossless* (same pixels, images only), *Balanced* (looks the same; the default) and *Smaller* (slightly lower quality, like Clop's aggressive mode). On the wheel they have their own ring: rest the dragged files on the *Optimize* chip in the mode bar under the ring, press **2**, or press **Tab** or scroll the mouse wheel to switch rings (Shift+Tab or scrolling up goes back). The optimized copy is saved as `name (optimized).ext` next to the original. If Lemon can't find a version that's clearly smaller, it keeps the original and says "Already optimized". See [How Optimize works](#how-optimize-works).
- **Remove background.** *Remove background – Lite / Full quality* cuts the subject out of an image with the BiRefNet AI model (runs locally, on the GPU through DirectML when available) and saves `name (no background).png`. The models are too big to bundle, so the installer offers to download them, or use *Settings > Background remover*. They're checked against a pinned SHA-256 and stored in `%LOCALAPPDATA%\Lemon\models` (Lite 224 MB, Full 973 MB). On the wheel it has its own ring: press **Tab** or scroll to cycle Convert, Optimize and Cut out, or press **1 / 2 / 3 / 4** or rest the files on a chip in the mode bar under the ring to jump to one (4 is Resize). Until a model is downloaded, its preset is hidden.
- **Resize.** *Resize – 4K / Full HD / HD / SD* shrinks images so their long edge is at most 3840, 1920, 1280 or 640 px, in the same format, saved as `name (1920 px).ext`. Images are never enlarged; one that already fits is left alone ("Already small enough"). On the wheel it has its own ring: press **4**, or rest the files on the *Resize* chip.
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

On first launch, a welcome guide lists the kinds of files these tools handle (videos and music, documents, making PDFs, shrinking PDFs) with an **Add** button for each one (it runs the `winget` command above in a console window, or opens the Ghostscript download page), then shows how to open the wheel. *Settings › About* brings it back.

## How Optimize works

The numbers below are for *Balanced*; *Smaller* values follow in brackets. *Lossless* only does the lossless steps, so it's offered for JPEG, PNG and GIF but not for WebP, video or PDF.

- **JPEG.** Lossless first: progressive encoding with optimized Huffman tables. If the file was saved above quality 85 [75], Lemon also tries a quality-85 [75] encode.
- **PNG.** Lossless recompression first. If the image has more than 256 colors, Lemon also tries a 256-color palette with alpha kept, like pngquant.
- **WebP.** Lemon tries a quality-80 [70] encode.
- **GIF.** Lossless recompression only, and every frame is kept. Animated WebP and animated PNG are left alone.
- **Lossy versions.** A lossy version is only used when it saves at least 10% more than the lossless one and passes a structural-similarity check against the original: DSSIM ≤ 0.01 (looks the same) [≤ 0.025].
- **Video.** The picture is re-encoded as H.264 (CRF 23 [28], `slow`) and the audio is copied untouched. MKV subtitles and attachments are kept. HEVC, AV1 and VP9 videos, and anything that is HDR or not 8-bit 4:2:0, are left alone: H.264 wouldn't make them smaller, or it would quietly lose color. A new video is only kept if it's at least 10% smaller.
- **PDF.** Ghostscript subsets and compresses fonts, merges duplicate images and downsamples images above 300 dpi to 200 dpi [above 225 dpi to 150 dpi, with stronger JPEG]. Text and vector graphics stay sharp. A new PDF is only kept if it's at least 10% smaller. Ghostscript rewrites the whole file, so digital signatures stop validating and accessibility tags are lost. Don't optimize signed or tagged PDFs.
- **Audio** isn't optimized. Re-encoding lossy audio only loses quality.

## Build and run

The Windows app lives in `windows/`. It requires the .NET 10 SDK (pinned in `windows/global.json`). Visual Studio isn't required. Everything that gets built (bin, obj, test results and packages) goes to `dist\Windows\` at the top of the repo, never next to the source (`windows/Directory.Build.props`).

```powershell
cd windows
dotnet build                                   # x64; for ARM64: dotnet build src/Lemon.App -p:Platform=ARM64
..\dist\Windows\build\bin\Lemon.App\debug_win-x64\Lemon.exe
dotnet test --project tests/Lemon.Core.Tests
```

## Packaging

The scripts are in `windows/packaging/`, can be run from anywhere, and write to `dist\Windows\`.

| Script | Output | |
|---|---|---|
| `make-installer.ps1` | `dist\Windows\Lemon-Setup.exe` | Per-user installer (no admin prompt) into `%LOCALAPPDATA%\Programs\Lemon`. It adds a Start menu shortcut and an uninstaller, with optional tasks for the right-click menu, starting with Windows, and a desktop icon. Needs Inno Setup 6 (`winget install JRSoftware.InnoSetup`). |
| `make-portable.ps1` | `dist\Windows\portable\Lemon.exe` | The whole app in one file, nothing to install. The first launch unpacks WinUI's files to `%TEMP%\.net\Lemon` (about 2 s); later launches are instant. |
| `make-app.ps1` | `dist\Windows\app\` | The self-contained app folder that the installer packages. |
| `make-icon.cs` | `src\Lemon.App\Assets\Lemon.ico/png` | Makes the icon from the Mac one (`mac/Lemon.png`): `dotnet run packaging/make-icon.cs` |

Neither package is code-signed yet, so SmartScreen warns on other PCs. FFmpeg, Pandoc and LibreOffice aren't bundled; Lemon finds them if they're installed.

Command line: `Lemon.exe [--background] [--to <ext> | --optimize] <files or folders…>`. With `--to` or `--optimize`, the files are converted right away without showing the window. The right-click menu uses this. `--background` starts in the notification area and is used for starting with Windows. Setup uses two more: `--register-context-menu` and `--unregister`.

## Layout

```
README.md, INSTALL.md, PARITY-LOG.md, CLAUDE.md
dist/                             Everything that gets built (not in git)
  Windows/                        build/ (bin, obj), TestResults/, app/, portable/, Lemon-Setup.exe
  MacOS/                          build/ (Swift), Lemon.app
windows/                          Windows 11 app (.NET 10, WinUI 3)
  Lemon.slnx, global.json
  Directory.Build.props           Sends all build output to dist/Windows
  packaging/                      make-*.ps1, make-icon.cs, and Lemon.iss (Inno Setup); see Packaging
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
    Program.cs, App.xaml          Single-instance startup, tray, batch notifications
    Views/                        Main window and the Convert, Presets and Settings pages
    ViewModels/                   Queue scheduler, per-file job state, preset editor
    Wheel/                        Global drag-gesture hooks and the animated round format picker
    Shell/                        Notification-area (tray) icon and notifications
    Interop/                      Win32 declarations
  tests/Lemon.Core.Tests/         Same folders as Lemon.Core; Conversion/ also converts real generated media
mac/                              macOS 14+ app (SwiftUI, Swift package)
  Package.swift
  packaging/make-app.sh           Builds dist/MacOS/Lemon.app
  Lemon.icon/                     App icon (Icon Composer); the menu bar icon is drawn from its layers
  Sources/Lemon/
    App/                          App entry, queue, main window, menu bar
    Wheel/                        Shake detection, rings, hub and the floating wheel
    Conversion/                   Presets, converters (ImageMagick, FFmpeg, Pandoc, LibreOffice, Ghostscript), BiRefNet
    Settings/                     Wheel look, rings, custom presets
```

## macOS version

A native SwiftUI port lives in `mac/` (macOS 14+). Drop files or folders on the window, pick a preset per file (or *Set all to*), and convert in parallel; Optimize works as described above. It shells out to Homebrew tools: `brew install ffmpeg imagemagick pandoc ghostscript` (LibreOffice is optional, for DOCX/ODT/RTF → PDF). DOCX/ODT/RTF/HTML/TXT conversions use macOS's built-in `textutil`. Presets for missing tools are hidden.

```sh
mac/packaging/make-app.sh      # builds dist/MacOS/Lemon.app
```

**Progress.** Every file shows its real progress: FFmpeg's encode position, ImageMagick's load/process/save meters, Ghostscript's pages, Optimize's candidate steps, and BiRefNet's stages (the model run itself is paced by how long the last one took). While anything converts, the lemon in the menu bar turns into a progress ring for the whole batch, the menu shows "Converting 3 of 7 · 42%" with Cancel, and the window toolbar shows the same as a bar.

**Wheel.** Lemon stays in the menu bar. While dragging files from anywhere, shake the mouse (or hold Shift, like Tangerine) and the wheel pops up under the pointer: drop on a format to convert next to the originals (a notification reports the result), drop on the lemon to add to the queue. The wheel has four built-in rings: **Convert**, **Optimize**, **Cut out** (remove the background onto a transparent `name (no background).png`) and **Resize** (*4K / Full HD / HD / SD*, the same sizes as on Windows: shrinks an image to that long edge in its own format as `name (1920 px).ext`, and leaves one that already fits alone). Cut out offers *Apple Vision* (built in, quick) and, like the Windows build, *BiRefNet Lite / Full quality*: the same pinned models, downloaded in *Settings › Background remover* (SHA-256 checked, stored in `~/Library/Application Support/Lemon/models`), run on the Mac's CPU with ONNX Runtime (about 6 s per image for Lite; ONNX Runtime 1.19's Core ML provider can't compile BiRefNet). While cutting out, the Lite model uses about 6.5 GB of memory; Lemon runs one image at a time and frees the model 20 seconds after the last one. BiRefNet handles large images and fine edges better; when Vision fails on a file, a downloaded BiRefNet model finishes it. Press **Tab** (Shift+Tab goes back) to cycle them or **1–9** to jump to one (**0** for the hub), or scroll, or rest the files on a ring's name in the mode bar under the ring. The keys need Accessibility permission (Lemon asks on first launch); without it, scrolling and the mode bar still work. It watches the mouse globally, which macOS allows without any permission. Releasing the mouse elsewhere closes the wheel.

**Customizing.** *Settings › Wheel* sets the look (themes including the macOS accent color, glass blur and opacity, the center emoji, text color), which formats go on the Convert ring for each kind of file, and which built-in rings show. You can make your own rings from any mix of presets (say, Optimize and Remove background together), open the wheel on a hub of rings, or pick the ring each kind of file opens on. *Settings › Presets* makes custom presets (format plus quality, size, bitrate, CRF, frame rate, metadata); they show up in the window and can go on the wheel.

Not ported: the Space key, the shake/key settings and the right-click menu.
