#!/bin/bash

Arch="$1"
OutputPath="$2"
Version="$3"

# Strip 'v' prefix from version for Debian packaging (Debian versions must start with digit)
DebianVersion="${Version#v}"

# Download v2ray core binaries with latest xray for Linux
FileName="v2rayN-${Arch}.zip"
if [[ "$Arch" == "linux-64" || "$Arch" == "linux-arm64" ]]; then
    # Use latest official Xray release for Linux to fix compatibility issues
    XRAY_VERSION="v25.9.11"
    if [[ "$Arch" == "linux-64" ]]; then
        XRAY_ARCH="Xray-linux-64.zip"
    else
        XRAY_ARCH="Xray-linux-arm64-v8a.zip" 
    fi
    
    # Download official xray binary
    wget -nv -O "xray.zip" "https://github.com/XTLS/Xray-core/releases/download/${XRAY_VERSION}/${XRAY_ARCH}"
    7z x "xray.zip" -o"xray-temp/"
    
    # Download geo files from 2dust (these are still current)
    wget -nv -O $FileName "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$FileName"
    7z x $FileName
    
    # Replace xray binary with the newer version
    chmod +x xray-temp/xray
    cp xray-temp/xray "v2rayN-${Arch}/bin/xray/xray"
    rm -rf xray-temp/ xray.zip
else
    # Use original source for non-Linux platforms
    wget -nv -O $FileName "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$FileName"
    7z x $FileName
fi
cp -rf v2rayN-${Arch}/* $OutputPath

PackagePath="haio-antisanction-Package-${Arch}"
mkdir -p "${PackagePath}/DEBIAN"
mkdir -p "${PackagePath}/opt"
cp -rf $OutputPath "${PackagePath}/opt/haio-antisanction"
echo "When this file exists, app will not store configs under this folder" > "${PackagePath}/opt/haio-antisanction/NotStoreConfigHere.txt"

if [ $Arch = "linux-64" ]; then
    Arch2="amd64" 
else
    Arch2="arm64"
fi
echo $Arch2

# basic
cat >"${PackagePath}/DEBIAN/control" <<-EOF
Package: haio-antisanction
Version: $DebianVersion
Architecture: $Arch2
Maintainer: https://github.com/haioco/antisanction
Depends: desktop-file-utils, xdg-utils
Description: HAIO Anti-Sanction - Cross-platform proxy client with advanced anti-sanction features
EOF

cat >"${PackagePath}/DEBIAN/postinst" <<-EOF
if [ ! -s /usr/share/applications/haio-antisanction.desktop ]; then
    cat >/usr/share/applications/haio-antisanction.desktop<<-END
[Desktop Entry]
Name=HAIO Anti-Sanction
Comment=A cross-platform anti-sanction proxy client with advanced features
Exec=/opt/haio-antisanction/haio-antisanction
Icon=/opt/haio-antisanction/haio-antisanction.png
Terminal=false
Type=Application
Categories=Network;Application;
END
fi

update-desktop-database
EOF

sudo chmod 0755 "${PackagePath}/DEBIAN/postinst"
sudo chmod 0755 "${PackagePath}/opt/haio-antisanction/haio-antisanction"
sudo chmod 0755 "${PackagePath}/opt/haio-antisanction/AmazTool" 2>/dev/null || true

# Patch
# set owner to root:root
sudo chown -R root:root "${PackagePath}"
# set all directories to 755 (readable & traversable by all users)
sudo find "${PackagePath}/opt/haio-antisanction" -type d -exec chmod 755 {} +
# set all regular files to 644 (readable by all users)
sudo find "${PackagePath}/opt/haio-antisanction" -type f -exec chmod 644 {} +
# ensure main binaries are 755 (executable by all users)
sudo chmod 755 "${PackagePath}/opt/haio-antisanction/haio-antisanction" 2>/dev/null || true
sudo chmod 755 "${PackagePath}/opt/haio-antisanction/AmazTool" 2>/dev/null || true

# build deb package
sudo dpkg-deb -Zxz --build $PackagePath
sudo mv "${PackagePath}.deb" "haio-antisanction-${Arch}.deb"
