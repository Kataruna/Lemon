<p align="center"><img src="docs/images/logo.png" width="128" alt="Lemon logo"></p>

<h1 align="center">Lemon</h1>

A drag-and-drop file converter for **Windows** and **macOS**. Drop in pictures, audio, video or documents, pick a format for each file (or for all of them at once), and convert the whole batch in parallel. Or skip the window: while you drag files anywhere on your computer, shake the mouse and a wheel of formats pops up under the pointer. Drop on one and the converted copy appears next to the original.

<p align="center"><img src="docs/images/wheel.png" width="640" alt="The conversion wheel open over a Finder window, with a photo resting on JPG"></p>

**Download:** [Releases page](https://github.com/Kataruna/Lemon/releases) (Windows releases are tagged `windows-v…`, Mac releases `mac-v…`). See the [install guide](INSTALL.md).

| Guide | What's in it |
|---|---|
| [INSTALL.md](INSTALL.md) | Installing, first steps, converters, updating and uninstalling, on Windows and Mac |
| [DETAIL.md](DETAIL.md) | How each part works: the wheel, the queue, every conversion, Optimize, Remove background, settings |
| [BUILD.md](BUILD.md) | Building, testing, packaging and releasing both apps, and where the code lives |

## Features

- **Conversion wheel.** While you drag files from anywhere (File Explorer, Finder, the desktop, a browser…), shake the mouse or press a key (Space on Windows, Shift on Mac, or your own) and a round menu of formats opens under the pointer. Drop on a format to convert right away; drop on the lemon in the middle to add the files to the queue. **Esc** closes the wheel without cancelling the drag.
- **Rings.** The wheel has rings for *Convert*, *Optimize*, *Cut out* (remove the background) and *Resize*, plus up to five rings of your own. Switch with **Tab**, the number keys, the scroll wheel, or by resting the files on a ring's name. A hub can show all the rings at once.
- **Batch queue.** Drop files or whole folders on the window, give each file its own preset (or use *Set all to*), and convert them in parallel with live progress.
- **Optimize.** Makes images, videos and PDFs smaller in the same format, with the quality kept close to the original, in three strengths: *Lossless*, *Balanced* and *Smaller*.
- **Remove background.** Cuts the subject out of a picture onto a transparent PNG, with the BiRefNet AI model running on your own computer (and Apple Vision on the Mac).
- **Resize.** Shrinks pictures to 4K, Full HD, HD or SD in the same format. Never enlarges.
- **Presets.** Dozens built in (*WebP – Web optimized*, *MP3 – 320 kbps*, *MP4 – 720p small*, *Animated GIF*…), and your own with quality, size, bitrate, CRF, frame rate and metadata settings.
- **Make it yours.** Themes (including your system's accent color), glass, the emoji in the middle, which formats go on the wheel, and which ring each kind of file opens on, all with a live preview.
- **Welcome guide.** On first launch, Lemon offers to install the free converters it uses (with `winget` on Windows, Homebrew on Mac) and shows how the wheel works.
- **Update check.** Lemon tells you when a newer version of your platform's app is on the Releases page. Nothing installs by itself, and nothing else is sent. It can be turned off in *Settings › About*.
- **Safe output.** Originals are never overwritten. A taken name becomes `name (1).ext`, and half-written files are removed when a conversion fails or is cancelled.

<p align="center"><img src="docs/images/window.png" width="560" alt="Lemon's main window with six files queued, each with its own format"></p>

## Windows and Mac

The two apps are written separately (C# / WinUI 3 and Swift / SwiftUI) and share no code, but they work the same way. The differences:

| | Windows | Mac |
|---|---|---|
| Runs on | Windows 10 (2004+) and 11, 64-bit | macOS 14 Sonoma or later, Apple silicon |
| Lives in | The notification area (by the clock) | The menu bar |
| Wheel key by default | Space (pressed) | Shift (held) |
| Pictures | Built in (bundled ImageMagick) | ImageMagick from Homebrew |
| Simple documents | Markdown and text built in | DOCX, ODT, RTF, HTML and text built in (`textutil`) |
| Remove background | BiRefNet on the GPU (DirectML) or CPU | Apple Vision, or BiRefNet on the CPU |
| Right-click *Convert with Lemon* | Yes | Not yet |
| Packages | Installer and portable `.exe` | `Lemon.app` in a zip |

## Issues

Report problems on the [issue tracker](https://github.com/Kataruna/Lemon/issues). On Windows, `%LOCALAPPDATA%\Lemon\errors.log` is useful to attach.
