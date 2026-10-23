#!/bin/sh
# Builds dist/Lemon.app. Run from anywhere.
set -e
cd "$(dirname "$0")"
swift build -c release
APP=dist/Lemon.app
rm -rf "$APP" && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/Lemon "$APP/Contents/MacOS/Lemon"

I=$(mktemp -d)/Lemon.iconset && mkdir -p "$I"
for s in 16 32 128 256 512; do
  sips -z $s $s ../src/Lemon.App/Assets/Lemon.png --out "$I/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) ../src/Lemon.App/Assets/Lemon.png --out "$I/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$I" -o "$APP/Contents/Resources/Lemon.icns"

cat > "$APP/Contents/Info.plist" <<'PL'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Lemon</string>
<key>CFBundleExecutable</key><string>Lemon</string>
<key>CFBundleIdentifier</key><string>com.sarabunari.lemon</string>
<key>CFBundleIconFile</key><string>Lemon</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
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
