#!/bin/sh
# Builds dist/Lemon.app. Run from anywhere.
set -e
cd "$(dirname "$0")"
swift build -c release
APP=dist/Lemon.app
rm -rf "$APP" && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/Lemon "$APP/Contents/MacOS/Lemon"

# Lemon.icon (Icon Composer) becomes Assets.car for macOS 26 and Lemon.icns for older systems.
xcrun actool Lemon.icon --compile "$APP/Contents/Resources" --app-icon Lemon --target-device mac --platform macosx \
  --minimum-deployment-target 14.0 --output-partial-info-plist "$(mktemp)" >/dev/null
cp Lemon.icon/Assets/*.svg "$APP/Contents/Resources/"   # the menu bar icon is drawn from these

cat > "$APP/Contents/Info.plist" <<'PL'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Lemon</string>
<key>CFBundleExecutable</key><string>Lemon</string>
<key>CFBundleIdentifier</key><string>com.sarabunari.lemon</string>
<key>CFBundleIconFile</key><string>Lemon</string>
<key>CFBundleIconName</key><string>Lemon</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.2.0</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>CFBundleDocumentTypes</key><array><dict>
<key>CFBundleTypeName</key><string>Files</string><key>CFBundleTypeRole</key><string>Viewer</string>
<key>LSItemContentTypes</key><array><string>public.item</string><string>public.folder</string></array>
</dict></array>
</dict></plist>
PL
# Sign with a real certificate when there is one: macOS keys the Accessibility grant (needed for the wheel's keys) to
# the signature, and an ad-hoc signature changes with every build, so each rebuild would ask for permission again.
# Uses the "Thanayos Siratranont" certificate (self-signed, in the login keychain; untrusted is fine for codesign).
# Override with LEMON_SIGN_ID=<name or hash>; with neither available, fall back to ad-hoc.
ID=${LEMON_SIGN_ID:-$(security find-identity -p codesigning | grep '"Thanayos Siratranont"' | head -1 | awk '{print $2}')}
codesign --force --deep -s "${ID:--}" "$APP"
echo "Built mac/$APP (signed with ${ID:-ad-hoc})"
