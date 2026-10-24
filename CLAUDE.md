# Lemon

Two separate apps with no shared code: `windows/` (C# / WinUI) and `mac/` (Swift / SwiftUI).

- Before working on one side, read `PARITY-LOG.md` for changes the other side made that this side still needs.
- After changing user-facing behavior on one side, add an entry to the top of `PARITY-LOG.md`.

## Where things go

- Everything that gets built goes to `dist/` at the top of the repo, split into `dist/Windows/` and `dist/MacOS/`. That includes compiler output (bin, obj, Swift's build folder), test results and packages. Nothing is built next to the source.
  - Windows: `windows/Directory.Build.props` sends bin/obj to `dist/Windows/build` and test results to `dist/Windows/TestResults`. The packaging scripts write `dist/Windows/app`, `dist/Windows/portable/Lemon.exe` and `dist/Windows/Lemon-Setup.exe`.
  - Mac: `mac/packaging/make-app.sh` builds into `dist/MacOS/build` and writes `dist/MacOS/Lemon.app`.
- `windows/` and `mac/` hold only what the app needs: source, tests, assets, project files, and the build scripts in each side's `packaging/` folder. A new script or tool that produces files must write them under `dist/Windows` or `dist/MacOS`.
- Build the Windows app from `windows/` with `dotnet build`. The exe is `dist/Windows/build/bin/Lemon.App/debug_win-x64/Lemon.exe`.
- After finishing a new feature or fix, build the package so the user can test it:
  - Windows: `windows/packaging/make-installer.ps1` writes `dist/Windows/Lemon-Setup.exe`.
  - Mac: `mac/packaging/make-app.sh` writes `dist/MacOS/Lemon.app`. It needs a Mac (Swift, `xcrun`). On Windows, say it wasn't built instead of skipping it silently.
