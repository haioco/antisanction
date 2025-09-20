#!/bin/bash

Arch="$1"
OutputPath="$2"
Version="$3"

# Download v2ray core binaries (keep original URL for compatibility)
FileName="v2rayN-${Arch}.zip"
wget -nv -O $FileName "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$FileName"
7z x $FileName
cp -rf v2rayN-${Arch}/* $OutputPath

PackagePath="haio-antisanction-Package-${Arch}"
mkdir -p "$PackagePath/HAIO Anti-Sanction.app/Contents/Resources"
cp -rf "$OutputPath" "$PackagePath/HAIO Anti-Sanction.app/Contents/MacOS"
cp -f "$PackagePath/HAIO Anti-Sanction.app/Contents/MacOS/haio-antisanction.icns" "$PackagePath/HAIO Anti-Sanction.app/Contents/Resources/AppIcon.icns" 2>/dev/null || echo "Icon file not found, using default"
echo "When this file exists, app will not store configs under this folder" > "$PackagePath/HAIO Anti-Sanction.app/Contents/MacOS/NotStoreConfigHere.txt"
chmod +x "$PackagePath/HAIO Anti-Sanction.app/Contents/MacOS/haio-antisanction"

cat >"$PackagePath/HAIO Anti-Sanction.app/Contents/Info.plist" <<-EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>English</string>
  <key>CFBundleDisplayName</key>
  <string>HAIO Anti-Sanction</string>
  <key>CFBundleExecutable</key>
  <string>haio-antisanction</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIconName</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>com.haio.antisanction</string>
  <key>CFBundleName</key>
  <string>HAIO Anti-Sanction</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${Version}</string>
  <key>CSResourcesFileMapped</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

create-dmg \
    --volname "HAIO Anti-Sanction Installer" \
    --window-size 700 420 \
    --icon-size 100 \
    --icon "HAIO Anti-Sanction.app" 160 185 \
    --hide-extension "HAIO Anti-Sanction.app" \
    --app-drop-link 500 185 \
    "haio-antisanction-${Arch}.dmg" \
    "$PackagePath/HAIO Anti-Sanction.app"