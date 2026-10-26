<img src="docs/logo.png" width="96" alt="2k-Player logo">

# 2k-Player

A fast, modern IPTV player for Windows. It plays M3U playlists and Xtream Codes accounts, and shows a programme guide from XMLTV (also `.gz`).

**Just want to use it?** Download the installer from [2k-player-releases](https://github.com/jvstin2k/2k-player-releases/releases/latest). This repository holds the source code.

2k-Player does not include or sell any channels, playlists or subscriptions. You need your own IPTV source.

<!-- Screenshots: remove this comment line and the closing one once the files are in docs/screenshots (see docs/screenshots/README.md)
## Screenshots

![Live TV with channel list and now/next](docs/screenshots/live.png)

| TV guide | Series |
|----------|--------|
| ![TV guide](docs/screenshots/guide.png) | ![Series with seasons and episodes](docs/screenshots/series.png) |

![Fullscreen with control bar](docs/screenshots/fullscreen.png)
-->

## Features

- Live TV, movies and series (Xtream Codes), M3U playlists with groups and logos
- Programme guide with now/next, search across channels and upcoming shows
- Home page with your favourite channels, movies and series; favourites, hidden categories, resume playback, watched marks, next episode
- Fullscreen with an overlay bar, mini player that stays on top, picture formats, audio and subtitle tracks
- Hardware decoding through libmpv, automatic reconnect when a stream drops
- Passwords and playlist URLs are kept in the Windows Credential Manager, not in plain files
- Update button inside the app when a new release is out, and "Check for updates" in the settings
- Settings page (gear button) with categories: General (language, version, updates), Sources, Hidden channels, About
- English and German interface, English by default

## Build it yourself

You need Windows 10 1809 or later (or Windows 11), the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Git and PowerShell. Visual Studio is optional; with it, install the "WinUI application development" workload.

```powershell
git clone https://github.com/jvstin2k/2k-player.git
cd 2k-player

# Once: downloads libmpv-2.dll (the player engine) into src/TwoKPlayer.App/libmpv
./scripts/fetch-libmpv.ps1               # on ARM64: -Platform ARM64

dotnet test tests/TwoKPlayer.Core.Tests   # optional: parser tests
dotnet run --project src/TwoKPlayer.App -p:Platform=x64
```

If PowerShell refuses to run the script, start it with `powershell -ExecutionPolicy Bypass -File scripts\fetch-libmpv.ps1`.

### Standalone app

To get a folder you can copy anywhere and start with `TwoKPlayer.App.exe`:

```powershell
dotnet publish src/TwoKPlayer.App -c Release -r win-x64 -p:Platform=x64 --self-contained -o publish
```

`scripts\install.ps1` does the same and adds a Start menu entry ("2k-Player"). Run it again after `git pull` to update; `-Uninstall` removes it.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

Self-built copies do not update themselves. Only the installer from the releases repository does.

### Installer with auto-update

The installer is made with [Velopack](https://velopack.io), after the publish step above:

```powershell
dotnet tool install -g vpk --version 1.2.161
vpk pack --packId 2k-Player --packVersion 1.0.0 --packDir publish --mainExe TwoKPlayer.App.exe --packTitle "2k-Player" --icon src/TwoKPlayer.App/Assets/AppIcon.ico
```

This writes `2k-Player-win-Setup.exe` and the update files to `Releases\`. The app looks for updates in the repository named in `src/TwoKPlayer.App/Updater.cs`; change it there if you publish your own releases.

Every merge to `main` does all of this in GitHub Actions (`.github/workflows/release.yml`) and uploads the release to [2k-player-releases](https://github.com/jvstin2k/2k-player-releases). That needs a secret `RELEASES_TOKEN`: a fine-grained token with "Contents: Read and write" on the releases repository. The releases page text comes from `docs/releases-repo/`.

## Where data is stored

- Passwords, playlist and EPG URLs: Windows Credential Manager, under "2kPlayer"
- `%LOCALAPPDATA%\2kPlayer`: `sources.json` (sources without credentials), `state.json` (favourites, hidden groups, resume, watched, volume), `cache\` (channel lists and EPG for a fast start), `crash.log`, `mpv.log`

## Keyboard and mouse

| Input | Action |
|-------|--------|
| Space | Pause |
| ↑ / ↓ | Previous / next channel |
| ← / → | Back / forward 10 s (movies and episodes) |
| F, double-click the picture | Fullscreen on/off |
| Esc | Leave fullscreen or mini player |
| M | Mute |
| P | Mini player (small, always on top); double-click to return |
| I | Stream info (resolution, codec, bitrate) |
| Mouse wheel over the picture, slider next to the speaker | Volume (remembered) |
| Star in the list | Favourite |
| Search | Finds channels and shows in the guide |
| Tracks button | Audio, subtitles, picture format, stream info |
| Right-click a category | Hide it (show it again from the gear button) |

## Project layout

| Path | Content |
|------|---------|
| `src/TwoKPlayer.Core/M3u` | M3U parser (tvg-*, group-title, `#EXTGRP`, `#EXTVLCOPT`, `url-tvg`) |
| `src/TwoKPlayer.Core/Xtream` | Xtream Codes live TV, movies and series via `player_api.php`, EPG via `xmltv.php` |
| `src/TwoKPlayer.Core/Epg` | Streaming XMLTV parser, now/next lookup, search |
| `src/TwoKPlayer.App` | WinUI 3 interface, `MpvPlayer` (libmpv in a child window), updater |
| `tests/TwoKPlayer.Core.Tests` | Tests for the parsers |

Built with WinUI 3 (Windows App SDK 1.8) and .NET 10. `TwoKPlayer.Core` has no Windows dependency.

## License

2k-Player is free software under the [GNU General Public License v3.0 or later](LICENSE). It bundles libmpv (GPL) and other libraries; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
