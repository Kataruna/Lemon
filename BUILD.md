# Building Lemon

Lemon is two separate apps with no shared code: `windows/` (C# / .NET 10 / WinUI 3) and `mac/` (Swift / SwiftUI). A change on one side has to be redone on the other by hand; [PARITY-LOG.md](PARITY-LOG.md) tracks what each side still needs.

- [Where things go](#where-things-go)
- [Windows](#windows)
- [Mac](#mac)
- [Releasing](#releasing)
- [Code layout](#code-layout)

## Where things go

Everything that gets built goes to `dist/` at the top of the repo (not in git), never next to the source: compiler output, test results and packages.

```
dist/
  Windows/   build/ (bin, obj), TestResults/, app/, portable/Lemon.exe, Lemon-Setup.exe
  MacOS/     build/ (Swift), Lemon.app, Lemon-x.y.z-mac.zip
```

`windows/` and `mac/` hold only what the app needs: source, tests, assets, project files, and the build scripts in each side's `packaging/` folder. A new script that produces files must write them under `dist/Windows` or `dist/MacOS`.

## Windows

You need the **.NET 10 SDK** (pinned in `windows/global.json`). Visual Studio isn't required. `windows/Directory.Build.props` sends bin/obj to `dist/Windows/build` and test results to `dist/Windows/TestResults`.

```powershell
cd windows
dotnet build                                    # x64
dotnet build src/Lemon.App -p:Platform=ARM64    # ARM64
..\dist\Windows\build\bin\Lemon.App\debug_win-x64\Lemon.exe
dotnet test --project tests/Lemon.Core.Tests
```

The tests mirror `Lemon.Core`'s folders; the conversion tests convert real generated media.

### Packaging

The scripts are in `windows/packaging/`, can be run from anywhere, and write to `dist\Windows\`.

| Script | Output | |
|---|---|---|
| `make-installer.ps1` | `dist\Windows\Lemon-Setup.exe` | Per-user installer (no admin prompt) into `%LOCALAPPDATA%\Programs\Lemon`, with a Start menu shortcut, an uninstaller, and optional right-click menu, start with Windows, model downloads and desktop icon. Runs `make-app.ps1` first. Needs Inno Setup 6 (`winget install JRSoftware.InnoSetup`). |
| `make-portable.ps1` | `dist\Windows\portable\Lemon.exe` | The whole app in one self-contained file. WinUI needs its files on disk, so the first launch unpacks them to `%TEMP%\.net\Lemon` (about 2 s). |
| `make-app.ps1` | `dist\Windows\app\` | The self-contained app folder that the installer packages. |
| `make-icon.cs` | `src\Lemon.App\Assets\Lemon.ico/png` | Makes the Windows icon from the Mac one (`mac/Lemon.png`). Run `dotnet run packaging/make-icon.cs` after the Mac icon changes. |

`make-installer.ps1` passes the version from `Lemon.App.csproj` (`<Version>`) to `Lemon.iss`. Neither package is code-signed yet, so SmartScreen warns on other PCs. FFmpeg, Pandoc, LibreOffice and Ghostscript aren't bundled; ImageMagick is.

## Mac

You need a Mac with **Swift 5.9+ and Xcode** (Xcode's `actool` compiles the icon). The app targets macOS 14. The one dependency, ONNX Runtime (for BiRefNet), comes through Swift Package Manager.

```sh
cd mac
swift build --scratch-path ../dist/MacOS/build    # debug build
swift run --scratch-path ../dist/MacOS/build      # run without packaging (no version, so no update check)
swift test --scratch-path ../dist/MacOS/build     # tests
```

Always pass `--scratch-path ../dist/MacOS/build`, or Swift builds into `mac/.build`.

### Packaging

```sh
mac/packaging/make-app.sh      # writes dist/MacOS/Lemon.app; run from anywhere
```

It builds a release binary, compiles `Lemon.icon` (Icon Composer) into `Assets.car` for macOS 26 and `Lemon.icns` for older systems, copies the icon's layers (the menu bar icon is drawn from them), writes `Info.plist` and signs the app.

- **Version:** `CFBundleShortVersionString` in `make-app.sh`. The update check reads it.
- **Signing:** with the "Thanayos Siratranont" certificate when it's in the keychain (self-signed is fine), or `LEMON_SIGN_ID=<name or hash>`; otherwise ad hoc. A stable signature matters: macOS ties the Accessibility permission to it, and an ad-hoc signature changes with every build, so each rebuild would ask again.
- **Zip for a release:** `ditto -c -k --keepParent dist/MacOS/Lemon.app dist/MacOS/Lemon-x.y.z-mac.zip`. The build is Apple silicon only.

## Releasing

Windows and Mac are released separately, each with its own tag. **The tag naming matters:** each app's update check only looks at releases whose tag starts with its own prefix.

| | Windows | Mac |
|---|---|---|
| Tag | `windows-vX.Y.Z` | `mac-vX.Y.Z` |
| Title | `Lemon - Windows X.Y.Z` | `Lemon - Mac X.Y.Z` |
| Version lives in | `windows/src/Lemon.App/Lemon.App.csproj` (and `packaging/Lemon.iss` as a fallback) | `mac/packaging/make-app.sh` |
| Files | `Lemon-X.Y.Z-Setup.exe`, `Lemon-X.Y.Z-Portable.exe` (renamed from the script output) | `Lemon-X.Y.Z-mac.zip` |

Steps:

1. Raise the version, build the packages, run the tests.
2. Commit and push to `main`.
3. Publish a full release (not a draft or pre-release, which the update check skips) on `main`, for example:

   ```sh
   gh release create mac-v0.4.0 dist/MacOS/Lemon-0.4.0-mac.zip --target main --title "Lemon - Mac 0.4.0" --notes-file notes.md
   ```

4. **Mac only:** update the Homebrew cask in [Kataruna/homebrew-tap](https://github.com/Kataruna/homebrew-tap): set `version` and `sha256` (`shasum -a 256 dist/MacOS/Lemon-X.Y.Z-mac.zip`) in `Casks/lemon.rb`, run `brew audit --cask --strict kataruna/tap/lemon`, and push.

The release notes say what's new, the requirements, and the install steps (including the SmartScreen or Gatekeeper workaround).

## Code layout

```
README.md, INSTALL.md, DETAIL.md, BUILD.md, PARITY-LOG.md, CLAUDE.md
docs/images/                      Logo and screenshots used by the README
windows/                          Windows app (.NET 10, WinUI 3)
  Lemon.slnx, global.json
  Directory.Build.props           Sends all build output to dist/Windows
  packaging/                      make-*.ps1, make-icon.cs, Lemon.iss (Inno Setup)
  src/Lemon.Core/                 Everything that isn't UI
    Formats/                      Known file types and kinds (image, audio, video, document)
    Presets/                      Preset model, built-in presets, user presets on disk
    Conversion/                   ConversionService: picks a converter, reserves a unique output name, cleans up
      Converters/                 ImageMagick, FFmpeg, Markdown, Pandoc, LibreOffice, and the Optimize converters
    ExternalTools/                Finding and running ffmpeg/pandoc/soffice/gswin64c (tied to Lemon by a job object), winget ids
    Configuration/                Settings and JSON storage in %LOCALAPPDATA%\Lemon, error log
    Shell/                        Explorer right-click menu, start with Windows
    Wheel/                        Shake detection and which presets go on the wheel
    UpdateCheck.cs                Newest windows-v release on GitHub
    LaunchRequest.cs              Command-line parsing
  src/Lemon.App/                  WinUI 3 app (unpackaged, self-contained Windows App SDK)
    Program.cs, App.xaml          Single-instance startup, tray, notifications, update check
    Views/                        Main window, Convert, Presets, Settings, Wheel settings, welcome guide
    ViewModels/                   Queue scheduler, per-file job state, preset editor
    Wheel/                        Global drag-gesture hooks and the animated wheel window
    Shell/                        Notification-area icon and notifications
    Interop/                      Win32 declarations
  tests/Lemon.Core.Tests/         Same folders as Lemon.Core
mac/                              Mac app (SwiftUI, Swift package)
  Package.swift
  packaging/make-app.sh           Builds dist/MacOS/Lemon.app
  Lemon.icon/                     App icon (Icon Composer); the menu bar icon is drawn from its layers
  Sources/Lemon/
    App/                          App entry, queue, main window, menu bar, welcome guide, update check
    Wheel/                        Shake detection, rings, hub, key tap and the floating wheel
    Conversion/                   Presets, converters (ImageMagick, FFmpeg, textutil, Pandoc, LibreOffice, Ghostscript), BiRefNet
    Settings/                     Saved preferences, wheel look, rings, opening the wheel, custom presets, About
  Tests/LemonTests/               Update check
```
