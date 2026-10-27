# How Lemon works

A closer look at each part of Lemon. Where Windows and Mac differ, both are described. To install, see [INSTALL.md](INSTALL.md); to build, see [BUILD.md](BUILD.md).

- [The conversion wheel](#the-conversion-wheel)
- [Rings and the hub](#rings-and-the-hub)
- [Customizing the wheel](#customizing-the-wheel)
- [Commands](#commands)
- [The batch queue](#the-batch-queue)
- [Progress and notifications](#progress-and-notifications)
- [What converts to what](#what-converts-to-what)
- [Presets](#presets)
- [Optimize](#optimize)
- [Remove background](#remove-background)
- [Resize](#resize)
- [Output files](#output-files)
- [Welcome guide](#welcome-guide)
- [Update check](#update-check)
- [Right-click menu and command line (Windows)](#right-click-menu-and-command-line-windows)
- [Where Lemon keeps things](#where-lemon-keeps-things)

## The conversion wheel

Lemon watches the mouse across the whole system. When a drag carries files (not text or a picture from a web page), or a folder or link you have [commands](#commands) for, and you **shake the mouse** or use the **wheel key**, a round menu opens centered under the pointer:

- **Drop on a format** to convert straight away. The files are saved next to the originals, and a notification reports the result.
- **Drop on the lemon** in the middle to add the files to the queue and open the window.
- **Esc** closes the wheel but keeps the drag going: drop the files anywhere, or shake or use the key again to bring the wheel back. With no wheel showing, Esc cancels the drag as usual.
- Releasing the mouse anywhere else closes the wheel.

A shake is four quick direction changes, each a real swing (at least 40 px), within 0.9 s, on either axis. Short jitters don't count.

**Opening the wheel** is set in Settings (Windows: *Conversion wheel*, and the *Wheel* tab under the preview; Mac: *Wheel › Opening the wheel*). Shaking and the key can each be turned off, but not both: turning off the last one turns the other back on, with a note saying so.

| | Windows | Mac |
|---|---|---|
| Default key | **Alt**, pressed (until 0.4.1: Space, which moves to Alt once) | **Control**, held (until 0.5.0: Shift, which moves to Control) |
| Other keys | Any key except Esc, Ctrl, Win and 0–9 (Shift works) | Shift (held), or any key except Esc, Tab, 0–9, ⌘ ⌥ and Fn, pressed once |
| How keys are read | A low-level keyboard hook | An event tap, only while the mouse button is down; needs Accessibility permission (holding Control or Shift doesn't) |
| Without a drag | Hold the key over files selected in File Explorer (below) | Hold the key over files selected in Finder (below); only with Control or Shift as the key |

**Selected files.** Select files or folders in File Explorer or on the desktop (Finder on the Mac), then hold the wheel key on its own for 0.7 s, and the wheel opens under the pointer for them:

- Both: the wheel stays up once you let go of the key. Point at a slice, or light it with **A** / **D** (or **W** / **S**; on Windows the arrow keys too, and with Ctrl or Win held these keys go to the app as usual; on the Mac the arrows only with Shift as the key, since Control + arrows switch Spaces). Then **tap the key** (or press **Return**, or click) to enter it: on the hub that opens the lit ring, on a ring it runs the slice. On the lemon, after moving off it, a tap adds the files to the queue. **Hold the key** for 0.7 s to close the wheel. **Esc** inside a ring goes back to the hub; on the hub (or with no hub) it closes the wheel. A tap only counts on its own: Control+W (Alt+Tab on Windows) isn't one. Clicking outside the wheel closes it too, and so does switching to another window.
- **Tab** turns the ring (Shift+Tab goes back), the number keys and the ring names under the wheel work as in a drag, and so does scrolling.
- Pressing any other key, a mouse button or scrolling while holding means the key was a modifier (Shift+click, Alt+F4…), so the wheel doesn't open. Nor does it while you're renaming a file.
- With Alt, letting go doesn't open Explorer's menu: Lemon slips a key that does nothing in between, as launchers do, and keeps the taps of Alt while the wheel is up to itself. Tab while the wheel is up turns the ring rather than opening the Alt+Tab switcher.
- Only File Explorer and the desktop share what's selected; other apps don't, so the key does nothing there. Items with no place on disk (inside a zip, This PC) are left out.
- On the Mac this works with Control or Shift as the wheel key (another key would type into Finder), and it reads Finder's selection by asking Finder, so macOS asks once whether Lemon may control Finder.

When a drag starts on a folder, Lemon looks inside it (on the Mac, the first 5,000 entries) to offer the right formats. Packages such as an app or a Photos library count as one item.

## Rings and the hub

The wheel has four built-in rings:

| Ring | What it does |
|---|---|
| **Convert** | Up to 8 formats for this kind of file, in your order |
| **Optimize** | *Lossless*, *Balanced*, *Smaller*: same format, smaller file ([details](#optimize)) |
| **Cut out** | Remove the background ([details](#remove-background)) |
| **Resize** | *4K*, *Full HD*, *HD*, *SD*, and *2×* / *4×* upscale ([details](#resize)) |

You can hide built-in rings and add up to **five of your own**, each mixing any presets (say, Optimize and Remove background together). A ring only appears when it has something for the dragged files. In a mixed drag (photos and videos together), the wheel offers what works for every file, then what fits the most common kind.

Switching rings mid-drag:

- **Tab** / **Shift+Tab** cycle, and the **scroll wheel** steps once per flick.
- **1–9** jump to a ring, **0** to the hub.
- **Rest the files** on a ring's name in the mode bar under the wheel.

**The hub** shows the rings themselves as slices; rest on one to open it. It's there when *Open on the hub* is on, or when there are more than four rings (too many for the mode bar). Inside a ring, the middle becomes a back arrow: rest on it to return to the hub. It only works once the pointer has left the middle since the ring opened, so a pause right after the shake doesn't jump back. *Back to the hub from a chip/button under the ring* uses a chip instead. Resting to switch takes 450 ms; on the Mac, opening a ring from the hub takes 250 ms.

## Customizing the wheel

The *Wheel* tab in Settings has a live preview at the top and sets:

- **Look:** built-in themes, including one that follows the system accent color, a custom accent, how strongly the glass takes the accent, the emoji in the middle and the text color.
- **Glass:** on Windows, blur from *None* to *Heavy* and opacity. Windows' own blur can't follow the wheel's round edge, so the blur is a snapshot of what's behind the wheel taken as it opens. On the Mac, Liquid Glass (macOS 26) on or off, frost from *Clear* to *Heavy*, and opacity.
- **Rings:** which built-in rings show, and your own rings.
- **Opens on:** the hub for everything, or a ring per kind of file.
- **Convert ring:** which formats go on it for each kind of file, up to 8, in order.

## Commands

The **Commands** ring runs your own shell commands (PowerShell on Windows) on dragged **files**, **folders** or **links**. Set them up in *Settings › Wheel › Commands*. Each one has a name, an icon, what it runs on (*Files*, optionally only some types such as `pdf, png`; *Folders*; or *Links*) and a command line. Up to 8 go on the ring, in Settings order.

- In the editor the placeholders are colored chips: **File**, **Folder**, **Files in Folder** (the files directly inside the folder, each as its own argument; on Windows a PowerShell list, so cmdlets get them as one array) and **Link**. The buttons under the box insert one at the cursor; typing `{file}`, `{folder}`, `{files}` or `{link}` turns into a chip too, and copying a chip copies that text. The first version's `{path}` and `{url}` still work. Chips are quoted for the shell already, so don't put quotes around them.
- On the Mac the line runs in a login `zsh` (so Homebrew's tools are on `PATH`); on Windows in Windows PowerShell, without a window. It runs once for each item dropped, in that folder (a file's own folder; for a link, your home folder). Lemon doesn't wait for it.
- On Windows the chips show as their colored text (`{file}`), and a link is read from what the browser drags, or from dragged text that is a web address.
- A file command also opens the wheel for files Lemon can't convert, like `.zip`; files keep opening on their usual ring, with Commands as one more.
- A folder dragged on its own opens the wheel on Commands, even when there's nothing in it to convert. A folder dragged with files gets Commands as one more ring.
- A link from a browser opens a wheel with only the Commands ring. With no link commands, a link drag doesn't open the wheel on the Mac; on Windows it opens with nothing to drop on.
- If a command fails (it stops with an error, or won't start), an alert (a notification on Windows) shows its name and the end of what it printed.

Examples:

| Name | Runs on | Command |
|---|---|---|
| Terminal | Folders | `open -na Ghostty --args -e /bin/zsh -lc 'cd "$0" && exec zsh -l' {folder}` |
| Claude | Folders | `open -na Ghostty --args -e /bin/zsh -lc 'cd "$0" && claude; exec zsh -l' {folder}` |
| VS Code | Folders | `open -a "Visual Studio Code" {folder}` |
| Zip the files | Folders | `zip -j "$HOME/Desktop/files.zip" {files}` |
| Copy path | Files | `printf %s {file} \| pbcopy` |
| Save for later | Links | `echo {link} >> ~/Desktop/later.txt` |

Ghostty ignores `--working-directory` when it's given `-e`, which is why the examples `cd` into the folder (passed as `$0`) themselves.

On Windows:

| Name | Runs on | Command |
|---|---|---|
| Terminal | Folders | `wt -d {folder}` |
| VS Code | Folders | `code {folder}` |
| Zip the files | Folders | `Compress-Archive {files} "$HOME\Desktop\files.zip" -Force` |
| Copy path | Files | `Set-Clipboard {file}` |
| Save for later | Links | `Add-Content "$HOME\Desktop\later.txt" {link}` |

## The batch queue

Drop files or whole folders on the window (folders are searched; hidden files and package contents are skipped). Every file gets its own preset picker; *Set all to* changes every file of one kind at once. Files convert in parallel, several at a time, with live progress, and can be cancelled.

## Progress and notifications

- Every file shows its real progress: FFmpeg's position in the video, ImageMagick's load/process/save stages, Ghostscript's pages, Optimize's candidate steps, and BiRefNet's stages. The AI model itself can't report progress, so that part is estimated from how long the previous run took.
- **Windows:** the lemon in the notification area becomes a progress ring; hover it for "Removing background · 2 of 5 done · 40%". When a batch finishes while the window isn't in front, a notification says what was done or what failed; click it to open Lemon.
- **Mac:** the menu bar lemon fills slice by slice, and its menu shows "Converting 3 of 7 · 42%" with Cancel. Drops on the wheel end with a notification.

## What converts to what

| Input | Output | Windows engine | Mac engine |
|---|---|---|---|
| PNG, JPG, WebP, AVIF, HEIC/HEIF, GIF, BMP, TIFF, ICO | PNG, JPG, WebP, AVIF, GIF, BMP, TIFF, ICO, PDF | ImageMagick (bundled) | ImageMagick (Homebrew) |
| MP3, WAV, FLAC, M4A/AAC, OGG, Opus, WMA, AIFF | MP3, M4A, Opus, OGG, FLAC, WAV, AIFF | FFmpeg | FFmpeg |
| MP4, MKV, WebM, MOV, AVI, WMV, M4V, FLV, animated GIF | MP4, MKV, WebM, MOV, animated GIF, or audio only | FFmpeg | FFmpeg |
| Markdown, TXT | HTML, TXT, MD | Built in | Pandoc (TXT: textutil) |
| DOCX, DOC, ODT, RTF, HTML, TXT | Each other | Pandoc | textutil (built in) |
| DOCX, ODT, RTF, HTML, MD, EPUB | DOCX, ODT, RTF, HTML, MD, TXT, EPUB | Pandoc | Pandoc |
| DOCX, DOC, ODT, RTF, XLSX, PPTX, … | PDF | LibreOffice | LibreOffice |

Notes:

- **HEIC** is input only (no HEVC encoder in ImageMagick).
- **Pictures to PDF:** one page per image; a multi-page TIFF becomes a multi-page PDF. Pages keep the image's print size when it fits on A4, and larger ones are scaled to fit. Photos are stored as JPEG inside the PDF; screenshots and graphics stay lossless.
- **PDFs can't become pictures** (reading PDFs would need Ghostscript as an input).
- **Missing tools:** formats whose tool isn't installed are hidden. On Windows, *Settings › Converters* shows each tool and can point to one in an unusual folder. Lemon looks on `PATH` and in the usual install folders (on the Mac: `/opt/homebrew/bin`, `/usr/local/bin`, MacPorts and LibreOffice.app).

## Presets

Built-in presets include *PNG*, *JPEG – High quality*, *WebP – Web optimized* (quality 80, at most 1920 px, metadata stripped), *AVIF – Small*, *Icon (256 px)*, *MP3 – 320 kbps*, *FLAC – Lossless*, *MP4 (H.264)*, *MP4 – 720p small*, *WebM (VP9)*, *Animated GIF* (12 fps, 480 px wide), *Word (DOCX)*, *EPUB e-book* and more.

Your own presets (*Presets* tab) pick a format and, depending on the kind, quality (1–100), longest side or max width/height, bitrate (kbps), CRF (lower is better), frame rate and metadata stripping. Empty boxes use the default. Custom presets appear in the window and can go on the wheel. A custom preset can also target the file's own format (for example a smaller JPEG), unlike a built-in one.

The format can also be **Keep the original format (resize pictures)**: a max width and/or height on Windows, the longest side on the Mac (one is required), and optionally quality and metadata stripping. Every picture stays in its own format and is shrunk to fit, like the built-in sizes, saved as `name (2560 px).ext`. These presets go on the Resize ring. The built-in *Resize – 4K … SD* are listed too, so one can be duplicated and given another size.

## Optimize

Optimize makes a file smaller in the same format and keeps the original if it can't. The copy is saved as `name (optimized).ext`; if no version is clearly smaller, the file is marked "Already optimized". Numbers are for *Balanced*, with *Smaller* in brackets. *Lossless* only does the lossless steps, so it's offered for JPEG, PNG and GIF only.

- **JPEG.** Lossless first: progressive encoding with optimized Huffman tables. If the file was saved above quality 85 [75], Lemon also tries a quality-85 [75] encode.
- **PNG.** Lossless recompression first. With more than 256 colors, Lemon also tries a 256-color palette with alpha kept, like pngquant.
- **WebP.** A quality-80 [70] encode.
- **GIF.** Lossless recompression only, every frame kept. Animated WebP and animated PNG are left alone.
- **Lossy versions** are only used when they save at least 10% more than the lossless one and pass a structural-similarity check against the original: DSSIM ≤ 0.01 (looks the same) [≤ 0.025].
- **Video.** Re-encoded as H.264 (CRF 23 [28], `slow`) with the audio copied untouched; MKV subtitles and attachments are kept. HEVC, AV1 and VP9 videos, and anything HDR or not 8-bit 4:2:0, are left alone, since H.264 wouldn't make them smaller or would lose color. Kept only if at least 10% smaller.
- **PDF.** Ghostscript subsets and compresses fonts, merges duplicate images and downsamples images above 300 dpi to 200 dpi [above 225 dpi to 150 dpi, with stronger JPEG]. Text and vector graphics stay sharp. Kept only if at least 10% smaller. Ghostscript rewrites the whole file, so digital signatures stop validating and accessibility tags are lost: don't optimize signed or tagged PDFs.
- **Audio** isn't optimized: re-encoding lossy audio only loses quality.

## Remove background

Cut out saves the subject on a transparent background as `name (no background).png`. Everything runs on your computer.

- **BiRefNet Lite** (224 MB) and **BiRefNet** (973 MB): the same pinned models on both platforms, downloaded once and checked against a SHA-256. Until one is downloaded, its preset is hidden.
- **Windows:** ONNX Runtime on the GPU through DirectML when available, otherwise the CPU.
- **Mac:** **Apple Vision** (built in, quick) is always there. BiRefNet runs on the CPU with ONNX Runtime, about 6 s per picture for Lite, using about 6.5 GB of memory; Lemon runs one picture at a time and frees the model 20 s after the last one. When Vision fails on a file, a downloaded BiRefNet model finishes it.

## Resize

*4K / Full HD / HD / SD* shrink a picture so its long edge is at most 3840, 1920, 1280 or 640 px, in the same format, saved as `name (1920 px).ext`. They never enlarge: a picture that already fits is left alone ("Already small enough").

**Upscale:** *2×* and *4×* enlarge a picture with a Real-ESRGAN AI model on your computer, in the same format, saved as `name (2×).ext`. Each comes in two qualities, and each only appears on the ring once its model is downloaded in *Settings › AI models*:

- **Real-ESRGAN General** (5 MB, *AI upscale*): fast, good for most pictures.
- **Real-ESRGAN x4plus** (67 MB, *Best quality*): sharper detail on photos, a few times slower.

The model always works at 4×; *2×* halves its result. It runs on the GPU when it can (DirectML on Windows; Core ML on the Mac, in a process of its own so its memory is handed back as soon as it finishes), in 256 px tiles so memory stays flat. Transparency, the color profile and metadata are kept (the alpha channel is enlarged normally). The result can be at most 16383 px on its long side. Not for GIF, ICO, PDF or animated WebP. The models are the official weights (BSD 3-Clause) exported to ONNX by `windows/packaging/export-upscale-models.py`, published on the `models-v1` release and pinned to a SHA-256.

## Output files

- The original is never overwritten. If the name is taken, Lemon saves `name (1).ext`, `name (2).ext`, and so on.
- Partial files are deleted when a conversion fails or is cancelled. Cancelling never saves a result.
- On Windows, converter processes are tied to Lemon with a job object, so they never outlive it, even after a crash (a force-killed Lemon can still leave a partial file). Closing the window mid-batch asks first. *Settings › Output* can send results to a folder instead of next to the originals.

## Welcome guide

Shown once on first launch (and once to people upgrading to 0.3.0). Skip, any button that leaves it, or (on Windows) switching tabs marks it as seen; *Settings › About › Show the welcome guide again* brings it back.

1. **What do you want to convert?** One card per kind of file: Pictures, Videos and music, Documents and ebooks, Making PDFs, Shrinking PDFs. Each shows **Ready** or **Add**.
   - **Windows:** Add runs `winget install --id <id> --exact` in a visible console window, which stays open on an error; Lemon checks again when it closes. Ghostscript isn't on winget, so its button (*Get it…*) opens the download page. Pictures are always ready.
   - **Mac:** Add opens a Terminal window running `brew install <formula>`, which stays open on an error. Without Homebrew the button is *Get it…* and opens brew.sh. Lemon checks again every 2 s and when it comes back to the front. Pictures need ImageMagick, so they have an Add button too.
2. **Convert without opening Lemon.** A 6-second loop acts it out: a photo is dragged, shaken (or the wheel key pops up), the wheel opens, the photo is dropped on JPG, and the JPG appears under the original, while *1. Drag · 2. Shake · 3. Drop* light up in turn. It's the real wheel with your own Convert ring and theme (on Windows without the blur). With animations off (Windows) or *Reduce motion* (Mac) it's a still picture. Buttons: *Back*, *Customize the wheel*, *Get started*.

## Update check

At startup, Lemon reads the repository's release list from GitHub (`api.github.com/repos/Kataruna/Lemon/releases`), keeps published releases (not drafts or pre-releases) tagged for its platform (`windows-v…` or `mac-v…`), and compares major.minor.patch with its own version. If one is newer, a notice with a **Download** button appears (Windows: on the Convert page; Mac: at the top of the main window), and *Settings › About* shows a link. Both open the release page; nothing installs by itself. Offline or a GitHub error just means no notice. *Settings › About › Check for updates when Lemon starts* turns it off. A Mac build run with `swift run` has no version and doesn't check.

## Right-click menu and command line (Windows)

**Right-click menu:** *Convert with Lemon › Open in Lemon… / To PNG / To MP3 / …* in File Explorer, turned on in setup or Settings. Selecting many files sends them all to one window. Not on the Mac yet.

**Command line:**

```
Lemon.exe [--background] [--to <ext> | --optimize] <files or folders…>
```

- `--to <ext>` or `--optimize` converts the files right away without showing the window (the right-click menu uses this).
- `--background` starts in the notification area (used for starting with Windows).
- Setup also uses `--register-context-menu` and `--unregister`.

## Where Lemon keeps things

| | Windows | Mac |
|---|---|---|
| Settings and presets | `%LOCALAPPDATA%\Lemon` | `defaults` domain `com.sarabunari.lemon` |
| Background-remover models | `%LOCALAPPDATA%\Lemon\models` | `~/Library/Application Support/Lemon/models` |
| Error log | `%LOCALAPPDATA%\Lemon\errors.log` | (shown on the file's row) |
| Installed app | `%LOCALAPPDATA%\Programs\Lemon` | Wherever you put Lemon.app |
