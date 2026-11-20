# WinTracer

English | [Русский](README.ru.md)

A tool for keeping a Windows install in shape so you don't have to reinstall it every six months. It shows where your disk space went, what runs at startup, cleans up junk and dead registry entries, and has a bunch of tweaks.

WPF on .NET 10, UI built with [WPF-UI](https://github.com/lepoco/wpfui).

## Features

### Disk space
- Scans a drive or any folder, shows a folder tree sorted by size and the contents of the selected folder.
- Shows both the logical size and what's actually used on disk (compression and OneDrive files included).
- Right-click a file or folder: open, show in Explorer, copy path, move, send to Recycle Bin or delete permanently.
- Del sends to Recycle Bin, Shift+Del deletes permanently, Backspace goes up a level.
- Won't let you delete or move C:\Windows, Program Files, your profile and other system stuff.
- C: with ~1M files takes about 7–10 seconds.

### Startup
- Everything in one place: registry Run keys, Startup folders, scheduled tasks (at logon/boot) and third-party services.
- Disables things the same way Task Manager does — the entry stays and the same switch turns it back on.
- Services get switched to manual start instead of being disabled completely.
- Broken entries (file missing) can be removed; a .reg file or a copy of the shortcut is saved first.

### Cleanup
- Temp, Windows Update cache, Delivery Optimization, crash dumps and error reports, thumbnail cache, logs, Recycle Bin, WinSxS via DISM.
- Browser caches (Chrome, Edge, Yandex, Firefox, Opera, Brave) — cookies, history and passwords are left alone.
- Discord, Steam, VS Code, NuGet/npm/pip/uv caches.
- AI tools: Claude Code, Claude Desktop, Codex, OpenCode, Antigravity. Cleans caches, logs, telemetry, downloaded updates and old versions. Login, settings, memory and history are not touched. Old session history can be removed separately, off by default.
- Shows how much will be freed before cleaning. Files in use are skipped.

### Registry
- Only looks for stuff that's definitely junk — entries pointing to files and folders that don't exist:
  - programs in Installed apps that left nothing behind;
  - App Paths and Open with entries for deleted exes;
  - firewall rules for removed programs;
  - shared DLLs and installer folders that are gone;
  - MUICache.
- Everything is saved to one .reg file before removal — double-click it and it's all back.
- Can also create a restore point (needs admin).

### Tweaks
Based on the list from [object5/Tweaker](https://github.com/object5/Tweaker), but every tweak knows its current state and can be reverted.

- Privacy: telemetry, advertising ID, input data collection, activity history, Copilot, Recall, web search in Start, Spotlight.
- Interface: ads and suggestions, widgets, This PC in Explorer, file extensions, hidden files, End task on the taskbar.
- Performance: startup delay, Game Mode, Game DVR, HAGS, Ultimate Performance plan, power throttling, network throttling, mouse acceleration, dynamic tick.
- Updates: turn off P2P update sharing.
- Removing built-in apps (News, Weather, Xbox stuff etc.) for the current user.

## Language

English and Russian. Uses the Windows display language by default, can be changed in Settings (needs a restart).

## Admin rights

Starts without them. System folders, HKLM, services and some tweaks need a restart as admin — there's a button in the header of most pages and in Settings.

## Backups

`%LOCALAPPDATA%\WinTracer\Backups` — .reg files and shortcuts removed from the registry and startup.
`%LOCALAPPDATA%\WinTracer\tweaks-backup.json` — values that were there before tweaks were applied.

## Building

Needs the .NET 10 SDK.

```
dotnet build SpaceTracer/WinTracer.csproj -c Release
```

Tests:

```
dotnet test WinTracer.sln
```

GitHub builds it automatically (`.github/workflows/build.yml`): every push and PR builds the project, runs the tests and puts the exe in the artifacts. Push a `v*` tag (like `v0.2.0`) and a zip with the exe gets attached to the release.

You can open a page directly: `WinTracer.exe --page=cleanup` (also `startup`, `registry`, `tweaks`, `settings`).

Strings live in `SpaceTracer/Resources/Strings.resx` (English) and `Strings.ru.resx` (Russian). The tests check that both files have the same keys and that every key used in the code exists.

## Not there yet

- No mapping of folders to installed programs, no search for leftover folders.
- Hard links are counted for every copy, so C:\Windows looks bigger than it is.
- No fast MFT-based scanning.
