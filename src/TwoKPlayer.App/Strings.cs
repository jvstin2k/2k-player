using System.Globalization;
using TwoKPlayer.Core.Epg;

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
    public static string EpgLoading => T("Loading the TV guide …", "TV-Guide wird geladen …");
    public static string NoEpg => T("No EPG for this source. Add an XMLTV link under Settings › Sources › Edit.",
        "Kein EPG für diese Quelle. Trag unter Einstellungen › Quellen › Bearbeiten einen XMLTV-Link ein.");
    public static string EpgFailed(string error) => T($"The EPG could not be loaded: {error}", $"EPG konnte nicht geladen werden: {error}");
    public static string EpgFormat(EpgFormatError error) => error switch
    {
        EpgFormatError.Html => T("The EPG link returns a web page instead of an XMLTV file. Use the direct link to the .xml or .xml.gz file.",
            "Der EPG-Link liefert eine Webseite statt einer XMLTV-Datei. Nimm den direkten Link zur .xml- oder .xml.gz-Datei."),
        EpgFormatError.Zip => T("The EPG link returns a ZIP file. Only .xml and .xml.gz are supported.",
            "Der EPG-Link liefert eine ZIP-Datei. Unterstützt werden nur .xml und .xml.gz."),
        EpgFormatError.Xz => T("The EPG link returns an .xz file. Only .xml and .xml.gz are supported.",
            "Der EPG-Link liefert eine .xz-Datei. Unterstützt werden nur .xml und .xml.gz."),
        _ => T("The EPG link returns XML, but not in XMLTV format.", "Der EPG-Link liefert XML, aber nicht im XMLTV-Format."),
    };
    public static string? EpgProblem(EpgDiagnosis d) => d.Problem switch
    {
        Core.Epg.EpgProblem.NoProgrammes => T("The EPG loaded, but contains no programmes.", "Der EPG wurde geladen, enthält aber keine Sendungen."),
        Core.Epg.EpgProblem.NoMatchingChannels => T(
            $"The EPG has programmes for {d.EpgChannels} channels, but none matches the channels shown here. Your channels use IDs like {Join(d.ChannelIdSamples)}, the EPG uses {Join(d.EpgIdSamples)}. The EPG is probably for another country or provider.",
            $"Der EPG hat Sendungen für {d.EpgChannels} Sender, aber keiner passt zu den Sendern hier. Deine Sender haben IDs wie {Join(d.ChannelIdSamples)}, der EPG nutzt {Join(d.EpgIdSamples)}. Der EPG ist vermutlich für ein anderes Land oder einen anderen Anbieter."),
        Core.Epg.EpgProblem.OutOfRange => T(
            $"The EPG has no programmes for now. Its data runs from {d.FirstStart?.ToLocalTime():g} to {d.LastStop?.ToLocalTime():g}, so the file is probably outdated.",
            $"Der EPG hat keine Sendungen für jetzt. Seine Daten reichen von {d.FirstStart?.ToLocalTime():g} bis {d.LastStop?.ToLocalTime():g}, die Datei ist also vermutlich veraltet."),
        _ => null,
    };

    private static string Join(IReadOnlyList<string> ids) => string.Join(", ", ids.Select(id => $"\"{id}\""));

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

    // ---------------------------------------------------------------- settings

    public static string Settings => T("Settings", "Einstellungen");
    public static string BackEsc => T("Back (Esc)", "Zurück (Esc)");
    public static string General => T("General", "Allgemein");
    public static string Sources => T("Sources", "Quellen");
    public static string HiddenChannels => T("Hidden channels", "Ausgeblendete Sender");
    public static string About => T("About", "Info");

    public static string LanguageHeader => T("Language", "Sprache");
    public static string LanguageDescription => T("Language of menus and messages", "Sprache von Menüs und Meldungen");
    public static string RestartForLanguage => T("The new language shows after a restart.", "Die neue Sprache erscheint nach einem Neustart.");
    public static string RestartNow => T("Restart now", "Jetzt neu starten");

    public static string Updates => "Updates";
    public static string VersionLabel(string version) => $"Version {version}";
    public static string SelfBuilt(string version) => T($"Version {version} (self-built)", $"Version {version} (selbst gebaut)");
    public static string CheckForUpdates => T("Check for updates", "Auf Update prüfen");
    public static string CheckingForUpdates => T("Checking for updates …", "Suche nach Updates …");
    public static string UpToDate => T("You have the latest version.", "Du hast die neueste Version.");
    public static string UpdateCheckFailed => T("Could not check for updates. Check the internet connection and try again.",
        "Updates konnten nicht geprüft werden. Prüfe die Internetverbindung und versuche es erneut.");
    public static string UpdatesOnlyInstalled => T("Only the version installed with Setup.exe updates itself.",
        "Nur die mit Setup.exe installierte Version aktualisiert sich selbst.");

    public static string SourcesDescription => T("M3U playlists and Xtream accounts. One source is used at a time.",
        "M3U-Playlists und Xtream-Zugänge. Es wird immer eine Quelle verwendet.");
    public static string NoSources => T("No sources yet.", "Noch keine Quellen.");
    public static string InUse => T("In use", "In Verwendung");
    public static string Remove => T("Remove", "Entfernen");
    public static string RemoveSourceQuestion(string name) => T($"Remove \"{name}\"? Its login and saved channel list are deleted.",
        $"\"{name}\" entfernen? Zugangsdaten und gespeicherte Senderliste werden gelöscht.");

    public static string HiddenDescription => T("Categories hidden with a right-click in the sidebar. Their channels are left out of \"All\" and the TV Guide.",
        "Kategorien, die per Rechtsklick in der Seitenleiste ausgeblendet wurden. Ihre Sender fehlen unter \"Alle\" und im TV-Guide.");
    public static string NoHiddenGroups => T("Nothing hidden. Right-click a category in the sidebar to hide it.",
        "Nichts ausgeblendet. Rechtsklick auf eine Kategorie in der Seitenleiste blendet sie aus.");
    public static string Unhide => T("Show again", "Wieder einblenden");
    public static string UnhideAll => T("Show all again", "Alle wieder einblenden");

    public static string AppDescription => T("IPTV player for M3U playlists, Xtream Codes and XMLTV guides.",
        "IPTV-Player für M3U-Playlists, Xtream Codes und XMLTV-Programmführer.");
    public static string License => T("Free software under GPL-3.0-or-later. 2k-Player does not include any channels.",
        "Freie Software unter GPL-3.0-or-later. 2k-Player enthält keine Sender.");
    public static string SourceCode => T("Source code", "Quellcode");
    public static string AllReleases => T("All versions and patch notes", "Alle Versionen und Patchnotes");
    public static string DataFolder => T("Data folder", "Datenordner");
    public static string DataFolderDescription => T("Settings, cache and crash.log", "Einstellungen, Cache und crash.log");
    public static string Open => T("Open", "Öffnen");
}
