# TuneLabBridge Submodule Setup Guide

Follow these steps to convert TuneLabBridge into a separate repository and add it as a git submodule.

## Step 1: Create the New Repository on GitHub

1. Go to https://github.com/new
2. Create a new repository named `TuneLabBridge`
3. Do NOT initialize with README, .gitignore, or license (we already have them)
4. Copy the repository URL (e.g., `https://github.com/YourUsername/TuneLabBridge.git`)

## Step 2: Initialize TuneLabBridge as a Separate Repository

Open a terminal and run these commands:

```bash
# Navigate to TuneLabBridge folder
cd C:\Users\Jingang\Documents\GitHub\TuneLab\TuneLabBridge

# Initialize as a new git repository
git init

# Add JUCE as a submodule (required dependency)
git submodule add https://github.com/juce-framework/JUCE.git JUCE

# Add all files
git add .

# Commit
git commit -m "Initial commit: TuneLab Bridge VST3 plugin"

# Add remote (replace with your actual repository URL)
git remote add origin https://github.com/YourUsername/TuneLabBridge.git

# Push to GitHub
git branch -M main
git push -u origin main
```

## Step 3: Remove TuneLabBridge from Main Repository

```bash
# Navigate to main TuneLab repository
cd C:\Users\Jingang\Documents\GitHub\TuneLab

# Remove TuneLabBridge from git tracking (but keep files temporarily)
git rm -r --cached TuneLabBridge

# Commit the removal
git commit -m "Remove TuneLabBridge in preparation for submodule"
```

## Step 4: Remove the TuneLabBridge Folder

```bash
# Delete the TuneLabBridge folder (we'll add it back as submodule)
# On Windows:
rmdir /s /q TuneLabBridge

# Or on PowerShell:
Remove-Item -Recurse -Force TuneLabBridge
```

## Step 5: Add TuneLabBridge as a Submodule

```bash
# Add as submodule (replace with your actual repository URL)
git submodule add https://github.com/YourUsername/TuneLabBridge.git TuneLabBridge

# Commit the submodule addition
git commit -m "Add TuneLabBridge as submodule"

# Push changes
git push
```

## Step 6: Update .gitmodules (if needed)

The `.gitmodules` file will be created automatically. It should look like:

```ini
[submodule "TuneLabBridge"]
    path = TuneLabBridge
    url = https://github.com/YourUsername/TuneLabBridge.git
```

## For Future Clones

When cloning the TuneLab repository with the submodule:

```bash
# Clone with submodules
git clone --recursive https://github.com/LiuYunPlayer/TuneLab.git

# Or if already cloned without submodules:
git submodule update --init --recursive
```

## Updating the Submodule

To update TuneLabBridge to the latest version:

```bash
cd TuneLabBridge
git pull origin main
cd ..
git add TuneLabBridge
git commit -m "Update TuneLabBridge submodule"
```

## Building

After cloning with submodules:

```bash
# Build TuneLab (C#)
dotnet build TuneLab.sln

# Build TuneLabBridge VST3 (C++)
cd TuneLabBridge
mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
```

---

## Quick Reference Commands

| Action | Command |
|--------|---------|
| Clone with submodules | `git clone --recursive <url>` |
| Initialize submodules | `git submodule update --init --recursive` |
| Update submodule | `cd TuneLabBridge && git pull && cd .. && git add TuneLabBridge` |
| Check submodule status | `git submodule status` |
