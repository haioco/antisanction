#!/bin/bash

Arch="$1"
OutputPath="$2"

OutputArch="haio-antisanction-${Arch}"
FileName="haio-antisanction-${Arch}.zip"

# Download v2ray core binaries but use our own naming
wget -nv -O "v2rayN-${Arch}.zip" "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-${Arch}.zip"

ZipPath64="./$OutputArch"
mkdir $ZipPath64

cp -rf $OutputPath "$ZipPath64/$OutputArch"
7z a -tZip $FileName "$ZipPath64/$OutputArch" -mx1