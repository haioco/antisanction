#!/bin/bash

Arch="$1"
OutputPath="$2"

OutputArch="haio-antisanction-${Arch}"
FileName="haio-antisanction-${Arch}.zip"

# Download and extract v2ray core binaries
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
    wget -nv -O "v2rayN-${Arch}.zip" "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-${Arch}.zip"
    7z x "v2rayN-${Arch}.zip"
    
    # Replace xray binary with the newer version
    chmod +x xray-temp/xray
    cp xray-temp/xray "v2rayN-${Arch}/bin/xray/xray"
    rm -rf xray-temp/ xray.zip
else
    # Use original source for non-Linux platforms
    wget -nv -O "v2rayN-${Arch}.zip" "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-${Arch}.zip"
    7z x "v2rayN-${Arch}.zip"
fi

ZipPath64="./$OutputArch"
mkdir $ZipPath64

# Copy application files
cp -rf $OutputPath "$ZipPath64/$OutputArch"

# Copy core binaries from extracted v2rayN folder to our application folder
cp -rf "v2rayN-${Arch}"/* "$ZipPath64/$OutputArch/"

7z a -tZip $FileName "$ZipPath64/$OutputArch" -mx1