# UHF Player für Windows

IPTV-Player für Windows im Stil von UHF (iOS/macOS). Quellen: M3U-Playlists, Xtream Codes, EPG (XMLTV, auch `.gz`).

## Technik

- **WinUI 3 (Windows App SDK 1.8) + .NET 10**: native Windows-Oberfläche mit Mica, GPU-Composition, ReadyToRun beim Publish für schnellen Start.
- **libmpv** als Player-Engine: FFmpeg-basiert, Hardware-Decoding (D3D11VA), robust bei HLS/MPEG-TS, schnelles Umschalten.
- `UhfPlayer.Core` ist plattformneutral (Parser, Xtream-API, EPG) und hat Tests.

## Starten (Windows 10 1809+ / 11)

Voraussetzung: .NET 10 SDK, optional Visual Studio 2026 mit Workload „WinUI-Anwendungsentwicklung“.

```powershell
./scripts/fetch-libmpv.ps1            # einmalig, holt libmpv-2.dll (ARM64: -Platform ARM64)
dotnet test tests/UhfPlayer.Core.Tests
dotnet run --project src/UhfPlayer.App -p:Platform=x64
```

In `%LOCALAPPDATA%\UhfPlayer` liegen `sources.json` (Quellen), `state.json` (Favoriten, ausgeblendete Gruppen, Weiterschauen), `cache\` (Senderlisten und EPG für den Schnellstart) und `mpv.log`.

## Bedienung

| Eingabe | Aktion |
|---------|--------|
| Leertaste | Pause |
| ↑ / ↓ | Vorheriger / nächster Sender |
| ← / → | 10 s zurück / vor (Filme und Folgen) |
| F, Doppelklick aufs Bild | Vollbild an/aus |
| Esc | Vollbild verlassen |
| M | Ton aus |
| Mausrad über dem Bild, Regler neben dem Lautsprecher | Lautstärke (wird gespeichert) |
| Stern in der Liste | Favorit |
| Rechtsklick auf Kategorie | Kategorie ausblenden (wieder einblenden über das Zahnrad) |

## Aufbau

| Pfad | Inhalt |
|------|--------|
| `src/UhfPlayer.Core/M3u` | M3U-Parser (tvg-*, group-title, `#EXTGRP`, `#EXTVLCOPT`, `url-tvg`) |
| `src/UhfPlayer.Core/Xtream` | Xtream Codes Live-TV, Filme und Serien über `player_api.php`, EPG über `xmltv.php` |
| `src/UhfPlayer.Core/Epg` | Streaming-XMLTV-Parser, Jetzt/Danach-Suche |
| `src/UhfPlayer.App` | WinUI-Oberfläche, `MpvPlayer` (libmpv in einem Child-Fenster) |

## Bewusst noch nicht drin

- Catch-up, zuletzt gesehener Sender beim Start.
- Passwörter liegen im Klartext in `sources.json` (später Windows Credential Locker).
