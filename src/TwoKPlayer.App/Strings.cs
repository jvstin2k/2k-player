using System.Globalization;

namespace TwoKPlayer.App;

/// <summary>
/// Every text the app shows, in English (the default) and German. The language is read once at start, see
/// MainWindow's constructor, so XAML binds these with one-time x:Bind and a change takes a restart.
/// </summary>
public static class Strings
{
    public const string English = "en", German = "de";

    private static bool _de;

    /// <summary>"en" or "de"; anything else falls back to English.</summary>
    public static string Language
    {
        get => _de ? German : English;
        set => _de = value == German;
    }

    /// <summary>For day names in dates, e.g. in search results.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    private static string T(string en, string de) => _de ? de : en;

    // ---------------------------------------------------------------- sections and groups

    public static string Home => T("Home", "Startseite");
    public static string LiveTv => "Live TV";
    public static string Movies => T("Movies", "Filme");
    public static string Series => T("Series", "Serien");
    public static string Guide => T("TV Guide", "TV-Guide");
    public static string Channels => T("Channels", "Sender");
    public static string Categories => T("CATEGORIES", "KATEGORIEN");
    public static string AllGroups => T("All", "Alle");
    public static string FavoritesGroup => T("★ Favorites", "★ Favoriten");
    public static string NoGroup => T("No group", "Ohne Gruppe");
    public static string HideGroup => T("Hide group", "Gruppe ausblenden");
    public static string RemoveFavorite => T("Remove from favorites", "Aus Favoriten entfernen");
    public static string Favorite => T("Favorite", "Favorit");
    public static string HomeEmpty => T("No favorites yet. Mark channels, movies or series with the star and they show up here.",
        "Noch keine Favoriten. Markiere Sender, Filme oder Serien mit dem Stern, dann erscheinen sie hier.");

    // ---------------------------------------------------------------- sidebar

    public static string SourcePickerTip => T("Which source is used", "Welche Quelle verwendet wird");
    public static string SearchPlaceholder => T("Search channels or shows", "Sender oder Sendung suchen");
    public static string AddSource => T("Add source", "Quelle hinzufügen");
    public static string SettingsTip => T("Edit or remove sources, hidden groups, language", "Quellen bearbeiten oder entfernen, ausgeblendete Gruppen, Sprache");
    public static string UpdateKeepsTip => T("Sources, favorites and settings are kept", "Quellen, Favoriten und Einstellungen bleiben erhalten");

    // ---------------------------------------------------------------- player

    public static string PickSomething => T("Choose a channel, movie or episode", "Wähle einen Sender, Film oder eine Folge");
    public static string NothingSelected => T("Nothing selected", "Nichts ausgewählt");
    public static string LiveTip => T("Resync channel", "Sender neu synchronisieren");
    public static string SeekBackTip => T("Back 10 seconds (←)", "10 Sekunden zurück (←)");
    public static string SeekForwardTip => T("Forward 10 seconds (→)", "10 Sekunden vor (→)");
    public static string PauseTip => T("Pause (Space)", "Pause (Leertaste)");
    public static string MuteTip => T("Mute (M)", "Ton aus (M)");
    public static string VolumeTip => T("Volume (also mouse wheel over the picture)", "Lautstärke (auch Mausrad über dem Bild)");
    public static string TracksTip => T("Audio, subtitles, picture format and stream info", "Ton, Untertitel, Bildformat und Stream-Info");
    public static string MiniTip => T("Mini player, always on top (P)", "Mini-Player, immer im Vordergrund (P)");
    public static string FullScreenTip => T("Fullscreen (F, double-click)", "Vollbild (F, Doppelklick)");

    public static string AudioTrack => T("Audio track", "Tonspur");
    public static string Subtitles => T("Subtitles", "Untertitel");
    public static string PictureFormat => T("Picture format", "Bildformat");
    public static string Original => "Original";
    public static string FillZoom => T("Fill (zoom)", "Bild füllen (Zoom)");
    public static string Stretch => T("Stretch", "Strecken");
    public static string StreamInfo => T("Toggle stream info (I)", "Stream-Info ein/aus (I)");
    public static string Off => T("Off", "Aus");
    public static string None => T("None", "Keine");
    public static string Track(string? id) => T($"Track {id}", $"Spur {id}");

    public static string Next(string time, string title) => T($"Next {time}  {title}", $"Danach {time}  {title}");
    public static string NowOn(string title) => T($"Now: {title}", $"Jetzt: {title}");
    public static string Watched => T("✓ Watched · ", "✓ Gesehen · ");
    public static string Movie => T("Movie", "Film");
    public static string OneSeries => T("Series", "Serie");
    public static string Episode => T("Episode", "Folge");

    // ---------------------------------------------------------------- status messages

    public static string LibmpvMissing => T("libmpv-2.dll is missing next to TwoKPlayer.App.exe. Run scripts/fetch-libmpv.ps1 and build again.",
        "libmpv-2.dll fehlt neben TwoKPlayer.App.exe. Bitte scripts/fetch-libmpv.ps1 ausführen und neu bauen.");
    public static string PlayerFailed(string error) => T($"The player could not start: {error}", $"Player konnte nicht starten: {error}");
    public static string PlayerUnavailable => T("Player not available.", "Player nicht verfügbar.");
    public static string NoSourceYet => T("No source yet. Add an M3U playlist or an Xtream account.", "Noch keine Quelle. Füge eine M3U-Playlist oder einen Xtream-Zugang hinzu.");
    public static string Refreshing => T("Refreshing channels …", "Aktualisiere Sender …");
    public static string Loaded(int live, int movies, int series)
        => T($"{live} channels, {movies} movies, {series} series. Updating EPG …", $"{live} Sender, {movies} Filme, {series} Serien. EPG wird aktualisiert …");
    public static string AccessDenied(int status)
        => T($"Access denied (HTTP {status}). Check the credentials, or whether the provider allows another connection right now.",
            $"Zugang abgelehnt (HTTP {status}). Prüfe die Zugangsdaten oder ob der Anbieter gerade eine weitere Verbindung erlaubt.");
    public static string EpisodesFailed(string series, string error) => T($"Episodes of {series}: {error}", $"Episoden von {series}: {error}");
    public static string ResumingAt(string time) => T($"Resuming at {time}", $"Weiter ab {time}");
    public static string ConnectionLost(string channel, string? error)
    {
        var detail = error is null ? "" : $" ({error})";
        return T($"{channel}: Connection lost{detail}. Press LIVE to try again.", $"{channel}: Verbindung verloren{detail}. Drück LIVE für einen neuen Versuch.");
    }
    public static string Reconnecting(int attempt, int max)
        => T($"Connection interrupted, reconnecting ({attempt}/{max}) …", $"Verbindung unterbrochen, verbinde neu ({attempt}/{max}) …");
    public static string PlaybackFailed(string error) => T($"Playback failed: {error}", $"Wiedergabe fehlgeschlagen: {error}");
    public static string NextEpisode(string name) => T($"Next episode: {name}", $"Nächste Folge: {name}");

    // ---------------------------------------------------------------- updates

    public static string InstallUpdate(string version) => T($"Install update {version}", $"Update {version} installieren");
    public static string UpdateAvailable(string version) => T($"Update {version} available", $"Update {version} verfügbar");
    public static string UpdateNow => T("Update now", "Jetzt updaten");
    public static string Later => T("Later", "Später");
    public static string NewInVersion => T("New in this version", "Neu in dieser Version");
    public static string NoPatchNotes => T("No patch notes available.", "Keine Patchnotes verfügbar.");
    public static string UpdateKeeps => T("Sources, favorites and settings are kept. The player restarts afterwards.",
        "Quellen, Favoriten und Einstellungen bleiben erhalten. Der Player startet danach neu.");
    public static string UpdateLoading => T("Downloading update …", "Update wird geladen …");
    public static string UpdateFailed => T("The update could not be downloaded. Please try again later.", "Update konnte nicht geladen werden. Bitte später erneut versuchen.");
    public static string UpdateRetry => T("Retry update", "Update erneut versuchen");

    // ---------------------------------------------------------------- dialogs

    public static string SourceType => T("Type", "Typ");
    public static string M3uPlaylist => T("M3U playlist", "M3U-Playlist");
    public static string NameHeader => "Name";
    public static string LocationHeader => T("Playlist URL or file / Xtream server", "Playlist-URL oder Datei / Xtream-Server");
    public static string UsernameHeader => T("Username (Xtream)", "Benutzername (Xtream)");
    public static string PasswordHeader => T("Password (Xtream)", "Passwort (Xtream)");
    public static string EpgHeader => T("EPG URL (optional, XMLTV)", "EPG-URL (optional, XMLTV)");
    public static string EditSource => T("Edit source", "Quelle bearbeiten");
    public static string Add => T("Add", "Hinzufügen");
    public static string Save => T("Save", "Speichern");
    public static string Cancel => T("Cancel", "Abbrechen");
    public static string Edit => T("Edit", "Bearbeiten");
    public static string Close => T("Close", "Schließen");

    public static string WhichSource => T("Which source do you want to use?", "Welche Quelle verwenden?");
    public static string Use => T("Use", "Verwenden");
    public static string SwitchHint => T("You can switch any time at the top of the sidebar.", "Wechseln geht jederzeit oben in der Seitenleiste.");

    public static string Settings => T("Settings", "Einstellungen");
    public static string RemoveOrShow => T("Remove or unhide selected", "Markierte entfernen bzw. einblenden");
    public static string Sources => T("Sources", "Quellen");
    public static string NoSources => T("No sources.", "Keine Quellen vorhanden.");
    public static string HiddenGroups => T("Hidden groups", "Ausgeblendete Gruppen");
    public static string NoHiddenGroups => T("None. Right-click a group to hide it.", "Keine. Rechtsklick auf eine Gruppe blendet sie aus.");
    public static string LanguageHeader => T("Language", "Sprache");
    public static string RestartForLanguage => T("The new language shows after a restart.", "Die neue Sprache erscheint nach einem Neustart.");
    public static string RestartNow => T("Restart now", "Jetzt neu starten");
}
