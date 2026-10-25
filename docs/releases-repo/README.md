<p align="center">
  <img src="logo.png" width="128" alt="2k-Player logo">
</p>

<h1 align="center">2k-Player</h1>

<p align="center">A fast, modern IPTV player for Windows.</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download the latest version</b></a>
</p>

---

## Features

- **All common sources:** M3U playlists, Xtream Codes logins and XMLTV EPG (also gzipped)
- **Live TV, movies and series** with the provider's categories, episodes sorted by season
- **TV guide** as a timeline across all channels, plus now/next with progress in the channel list
- **Search** for channels and for shows in the EPG
- **Favorites**, hidden categories, resume where you left off, watched marks
- **Next episode** starts automatically
- **Fullscreen** with auto-hiding controls, **mini player** that stays on top
- **Audio tracks, subtitles, picture format** (zoom, 16:9, 4:3, stretch) and stream info
- **Auto-reconnect** when a live stream drops, LIVE button to jump back to the live edge
- **Hardware decoding** via libmpv, quick channel switching, cached channel lists for an instant start
- **Logins stay private:** passwords and playlist URLs are kept in the Windows Credential Manager

## Install

1. Download **`2k-Player-win-Setup.exe`** from the [latest release](../../releases/latest).
2. Run it. The player installs and starts by itself, no admin rights needed.
3. Windows may show *"Windows protected your PC"* because the installer is not code-signed. Click **More info → Run anyway**.
4. Add your source with **Quelle hinzufügen** (M3U link or Xtream login).

Requires Windows 10 (1809 or later) or Windows 11, 64-bit.

## Updates

When a new version is available, a button **Update … installieren** appears at the bottom left of the player. One click downloads it and restarts. Your sources, logins, favorites and settings are kept.

## Uninstall

Windows Settings → Apps → Installed apps → **2k-Player** → Uninstall.

## Keyboard shortcuts

| Key | Action |
|-----|--------|
| Space | Play / pause |
| ↑ / ↓ | Previous / next channel |
| ← / → | 10 s back / forward (movies and episodes) |
| F, double-click | Fullscreen |
| Esc | Leave fullscreen or mini player |
| M | Mute |
| P | Mini player |
| I | Stream info |
| Mouse wheel over the video | Volume |

## Source code and license

2k-Player is free software under the [GNU GPL v3.0 or later](https://github.com/jvstin2k/2k-player/blob/main/LICENSE). The source code and instructions to build it yourself are at **[github.com/jvstin2k/2k-player](https://github.com/jvstin2k/2k-player)**. Bundled third-party components are listed in [THIRD-PARTY-NOTICES](https://github.com/jvstin2k/2k-player/blob/main/THIRD-PARTY-NOTICES.md).

2k-Player does not include or provide any TV channels or content. You need your own playlist or login from an IPTV provider.

## Files in this release

| File | What it is |
|------|------------|
| `2k-Player-win-Setup.exe` | Installer, the only file you need |
| `2k-Player-win-Portable.zip` | Portable version without installer (does not update itself) |
| `2k-Player-*-full.nupkg`, `releases.win.json`, `RELEASES` | Used by the built-in updater, no need to download |
