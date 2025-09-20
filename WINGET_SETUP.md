# WinGet Publishing Setup

## Overview
The `winget-publish.yml` workflow has been updated to work with the HAIO Anti-Sanction repository, but requires setup to function properly.

## Required Setup Steps

### 1. Create GitHub Personal Access Token
1. Go to GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Generate a new token with `public_repo` scope
3. Copy the token value

### 2. Add Repository Secret
1. Go to your repository: https://github.com/haioco/antisanction
2. Navigate to Settings → Secrets and variables → Actions
3. Click "New repository secret"
4. Name: `WINGET_TOKEN`
5. Value: Paste the personal access token from step 1

### 3. Package Registration
Before the workflow can update a winget package, the package must first be registered in the Windows Package Manager Community Repository.

**Option A: Manual Registration**
1. Create an initial manifest for `haioco.HAIOAntiSanction`
2. Submit a PR to: https://github.com/microsoft/winget-pkgs

**Option B: Use wingetcreate for initial submission**
```powershell
wingetcreate.exe new haioco.HAIOAntiSanction --version 1.0.0 --installer-url "https://github.com/haioco/antisanction/releases/download/v1.0.0/haio-antisanction-windows-x64.zip"
```

## Usage
Once setup is complete, the workflow will automatically:
- Trigger when a new release is published
- Can be manually triggered with a specific release tag
- Update the winget package with new installer URLs
- Submit the update as a PR to winget-pkgs repository

## Current Configuration
- **Package ID**: `haioco.HAIOAntiSanction`
- **Supported Architectures**: x64, arm64 (if available)
- **Installer Type**: ZIP archives from GitHub releases
- **Trigger**: Manual dispatch or release publication

## Notes
- The workflow will gracefully skip if `WINGET_TOKEN` is not configured
- It will handle cases where only x64 installers are available
- The package must exist in winget-pkgs before updates can be submitted