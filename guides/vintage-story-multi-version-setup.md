# Vintage Story Multi-Version Setup

This repo can build against different Vintage Story installs by selecting an explicit game version at build time.

## Recommended layout

Keep each game version in its own install folder, for example:

- `C:\Games\VintageStory\1.21.6`
- `C:\Games\VintageStory\1.22.0`

Avoid relying on `C:\Users\<you>\AppData\Roaming\Vintagestory` as the only reference path when you need back-compat builds, because whichever version is installed there becomes your compile target.

## One-time setup

Register your install folders:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Set-VintageStoryEnv.ps1 -Version 1.21 -InstallPath "C:\Games\VintageStory\1.21.6"
powershell -ExecutionPolicy Bypass -File .\tools\Set-VintageStoryEnv.ps1 -Version 1.22 -InstallPath "C:\Games\VintageStory\1.22.0"
```

This stores:

- `VINTAGE_STORY_121`
- `VINTAGE_STORY_122`

as user environment variables.

Open a new terminal after setting them so PowerShell picks them up.

## Build commands

Build against 1.21:

```powershell
.\tools\Build-VintageStory.ps1 -GameVersion 1.21
```

Build against 1.22:

```powershell
.\tools\Build-VintageStory.ps1 -GameVersion 1.22
```

You can also call `dotnet` directly:

```powershell
dotnet build -p:GameVersion=1.21
dotnet build -p:GameVersion=1.22
```

## Notes

- The project prefers `GameVersion`-specific paths first, then falls back to `VINTAGE_STORY`, then the roaming install.
- If you need exact patch-level compatibility, point `1.21` at the exact client version you want to support, such as `1.21.6`.
- Vintage Story's official versions list shows both the `1.22.x` and `1.21.x` lines are maintained as separate version families: https://wiki.vintagestory.at/Versions
