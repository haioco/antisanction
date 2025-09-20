#!/bin/bash

Arch="$1"
OutputPath="$2"

OutputArch="haio-antisanction-${Arch}"
FileName="haio-antisanction-${Arch}.zip"

# Download and extract v2ray core binaries
wget -nv -O "v2rayN-${Arch}.zip" "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-${Arch}.zip"
7z x "v2rayN-${Arch}.zip"

ZipPath64="./$OutputArch"
mkdir $ZipPath64

# Copy application files
cp -rf $OutputPath "$ZipPath64/$OutputArch"

# Copy core binaries from extracted v2rayN folder to our application folder
cp -rf "v2rayN-${Arch}"/* "$ZipPath64/$OutputArch/"

7z a -tZip $FileName "$ZipPath64/$OutputArch" -mx1