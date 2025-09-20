#!/bin/bash
set -euo pipefail

# Install deps
sudo apt update -y
sudo apt install -y libfuse2 wget file p7zip-full

# Get tools
wget -qO appimagetool https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool

# Download and extract core binaries for both architectures
echo "Downloading core binaries..."
wget -nv -O "v2rayN-linux-64.zip" "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-linux-64.zip"
wget -nv -O "v2rayN-linux-arm64.zip" "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-linux-arm64.zip"
7z x "v2rayN-linux-64.zip"
7z x "v2rayN-linux-arm64.zip"

# x86_64 AppDir
APPDIR_X64="AppDir-x86_64"
rm -rf "$APPDIR_X64"
mkdir -p "$APPDIR_X64/usr/lib/haio-antisanction" "$APPDIR_X64/usr/bin" "$APPDIR_X64/usr/share/applications" "$APPDIR_X64/usr/share/pixmaps"
cp -rf "$OutputPath64"/* "$APPDIR_X64/usr/lib/haio-antisanction" || true
# Copy core binaries for x86_64
cp -rf v2rayN-linux-64/* "$APPDIR_X64/usr/lib/haio-antisanction/" || true
[ -f "$APPDIR_X64/usr/lib/haio-antisanction/haio-antisanction.png" ] && cp "$APPDIR_X64/usr/lib/haio-antisanction/haio-antisanction.png" "$APPDIR_X64/usr/share/pixmaps/haio-antisanction.png" || true
[ -f "$APPDIR_X64/usr/lib/haio-antisanction/haio-antisanction.png" ] && cp "$APPDIR_X64/usr/lib/haio-antisanction/haio-antisanction.png" "$APPDIR_X64/haio-antisanction.png" || true

printf '%s\n' '#!/bin/sh' 'HERE="$(dirname "$(readlink -f "$0")")"' 'cd "$HERE/usr/lib/haio-antisanction"' 'exec "$HERE/usr/lib/haio-antisanction/haio-antisanction" "$@"' > "$APPDIR_X64/AppRun"
chmod +x "$APPDIR_X64/AppRun"
ln -sf usr/lib/haio-antisanction/haio-antisanction "$APPDIR_X64/usr/bin/haio-antisanction"
cat > "$APPDIR_X64/haio-antisanction.desktop" <<EOF
[Desktop Entry]
Name=HAIO Anti-Sanction
Comment=A cross-platform anti-sanction proxy client with advanced features
Exec=haio-antisanction
Icon=haio-antisanction
Terminal=false
Type=Application
Categories=Network;
EOF
install -Dm644 "$APPDIR_X64/haio-antisanction.desktop" "$APPDIR_X64/usr/share/applications/haio-antisanction.desktop"

ARCH=x86_64 ./appimagetool "$APPDIR_X64" "haio-antisanction-${OutputArch}.AppImage"
file "haio-antisanction-${OutputArch}.AppImage" | grep -q 'x86-64'

# aarch64 AppDir
APPDIR_ARM64="AppDir-aarch64"
rm -rf "$APPDIR_ARM64"
mkdir -p "$APPDIR_ARM64/usr/lib/haio-antisanction" "$APPDIR_ARM64/usr/bin" "$APPDIR_ARM64/usr/share/applications" "$APPDIR_ARM64/usr/share/pixmaps"
cp -rf "$OutputPathArm64"/* "$APPDIR_ARM64/usr/lib/haio-antisanction" || true
# Copy core binaries for aarch64
cp -rf v2rayN-linux-arm64/* "$APPDIR_ARM64/usr/lib/haio-antisanction/" || true
[ -f "$APPDIR_ARM64/usr/lib/haio-antisanction/haio-antisanction.png" ] && cp "$APPDIR_ARM64/usr/lib/haio-antisanction/haio-antisanction.png" "$APPDIR_ARM64/usr/share/pixmaps/haio-antisanction.png" || true
[ -f "$APPDIR_ARM64/usr/lib/haio-antisanction/haio-antisanction.png" ] && cp "$APPDIR_ARM64/usr/lib/haio-antisanction/haio-antisanction.png" "$APPDIR_ARM64/haio-antisanction.png" || true

printf '%s\n' '#!/bin/sh' 'HERE="$(dirname "$(readlink -f "$0")")"' 'cd "$HERE/usr/lib/haio-antisanction"' 'exec "$HERE/usr/lib/haio-antisanction/haio-antisanction" "$@"' > "$APPDIR_ARM64/AppRun"
chmod +x "$APPDIR_ARM64/AppRun"
ln -sf usr/lib/haio-antisanction/haio-antisanction "$APPDIR_ARM64/usr/bin/haio-antisanction"
cat > "$APPDIR_ARM64/haio-antisanction.desktop" <<EOF
[Desktop Entry]
Name=HAIO Anti-Sanction
Comment=A cross-platform anti-sanction proxy client with advanced features
Exec=haio-antisanction
Icon=haio-antisanction
Terminal=false
Type=Application
Categories=Network;
EOF
install -Dm644 "$APPDIR_ARM64/haio-antisanction.desktop" "$APPDIR_ARM64/usr/share/applications/haio-antisanction.desktop"

# aarch64 runtime
wget -qO runtime-aarch64 https://github.com/AppImage/AppImageKit/releases/download/continuous/runtime-aarch64
chmod +x runtime-aarch64

# build aarch64 AppImage
ARCH=aarch64 ./appimagetool --runtime-file ./runtime-aarch64 "$APPDIR_ARM64" "haio-antisanction-${OutputArchArm}.AppImage"
file "haio-antisanction-${OutputArchArm}.AppImage" | grep -q 'ARM aarch64'
