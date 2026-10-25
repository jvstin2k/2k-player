using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using UhfPlayer.Core;
using UhfPlayer.Core.Epg;
using UhfPlayer.Core.Models;
using Windows.Foundation;
using Windows.System;

namespace UhfPlayer.App;

public sealed partial class MainWindow : Window
{
    private enum Section
    {
        Live,
        Movies,
        Series,
        Guide,
    }

    private enum ViewMode
    {
        Normal,
        FullScreen,
        Mini,
    }

    // Label, video-aspect-override, panscan, keepaspect.
    private static readonly (string Label, string Aspect, string Panscan, string KeepAspect)[] PictureFormats =
    [
        ("Original", "no", "0", "yes"),
        ("Bild füllen (Zoom)", "no", "1", "yes"),
        ("16:9", "16:9", "0", "yes"),
        ("4:3", "4:3", "0", "yes"),
        ("Strecken", "no", "0", "no"),
    ];

    private const int MaxReconnects = 5;

    private const string AllGroups = "Alle";
    private const string FavoritesGroup = "★ Favoriten";
    private const string NoGroup = "Ohne Gruppe";
    private static readonly string[] SectionTitles = ["Live TV", "Filme", "Serien", "TV-Guide"];
    private static readonly TimeSpan EpgMaxAge = TimeSpan.FromHours(12);
    private const double GuidePxPerMinute = 4, GuideNameWidth = 220;
    private const int GuideHours = 6;

    private static readonly HttpClient Http = new(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
    {
        DefaultRequestHeaders = { { "User-Agent", SourceLoader.DefaultUserAgent } },
    };
    private readonly SourceLoader _loader = new(Http);
    private readonly List<SourceConfig> _sources = SourceStore.Load();
    private readonly AppState _state = AppState.Load();
    private readonly List<Channel> _channels = [];
    private readonly Dictionary<Guid, EpgGuide> _guides = [];
    private readonly MpvPlayer? _player;
    private readonly Updater _updater = new();
    private readonly string? _playerError;
    private Channel? _playing;
    private bool _paused, _muted;
    private int _pictureFormat;

    /// <summary>The list a movie or episode was started from, for playing the next episode.</summary>
    private List<Channel> _playQueue = [];

    private int _reconnects;
    private DateTime _lastStart;

    // Mouse moves over the video reach mpv's window, not XAML, so fullscreen idleness is detected by polling the cursor.
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CursorPoint _lastCursor;
    private DateTime _lastCursorMove;
    private FullscreenBar? _bar;
    private ViewMode _mode;

    /// <summary>Window size and position before the mini player, restored when it closes.</summary>
    private Windows.Graphics.RectInt32? _restoreBounds;

    private Section _section;

    /// <summary>Episodes of the series being browsed; null while the normal list is shown.</summary>
    private IReadOnlyList<Channel>? _episodes;

    /// <summary>Set while the group list is rebuilt, so its selection events do not reset the view.</summary>
    private bool _refreshingGroups;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 920));

        try
        {
            _player = new MpvPlayer(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch (DllNotFoundException)
        {
            _playerError = "libmpv-2.dll fehlt neben UhfPlayer.App.exe. Bitte scripts/fetch-libmpv.ps1 ausführen und neu bauen.";
        }
        catch (Exception ex) when (ex is BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
        {
            _playerError = $"Player konnte nicht starten: {ex.Message}";
        }

        if (_player is not null)
        {
            // Both events come from background threads, which must not touch the window itself, not even its
            // DispatcherQueue property; capture the queue here on the UI thread.
            var ui = DispatcherQueue;
            _player.PlaybackEnded += error => ui.TryEnqueue(() => OnPlaybackEnded(error));
            _player.PlaybackStarted += () => ui.TryEnqueue(OnPlaybackStarted);
            _player.MessageReceived += (name, arg) => ui.TryEnqueue(() => OnPlayerMessage(name, arg));
        }

        VolumeSlider.Value = _state.Volume;
        VolumeSlider.ValueChanged += OnVolumeChanged; // only now, so setting the saved value is not taken as a change
        _player?.SetProperty("volume", _state.Volume.ToString(CultureInfo.InvariantCulture));

        _idleTimer.Tick += OnIdleTick;
        Closed += (_, _) =>
        {
            SaveResume();
            _state.Save();
            _updater.ApplyOnExit();
            _idleTimer.Stop();
            _player?.Dispose();
        };

        SectionList.SelectedIndex = 0;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        await ReloadAsync();
        // After loading, so the update note is not replaced by the loading status.
        if (await _updater.DownloadAsync() is { } version)
        {
            ShowStatus(InfoBarSeverity.Success, $"Update {version} ist geladen und wird beim nächsten Start installiert.");
            var restart = new Button { Content = "Jetzt neu starten" };
            restart.Click += (_, _) =>
            {
                SaveResume();
                _state.Save();
                _updater.RestartNow();
            };
            StatusBar.ActionButton = restart;
        }
    }

    // ---------------------------------------------------------------- loading

    private async Task ReloadAsync()
    {
        _channels.Clear();
        _guides.Clear();
        if (_sources.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Informational, "Noch keine Quelle. Füge eine M3U-Playlist oder einen Xtream-Zugang hinzu.");
            RefreshGroups();
            return;
        }

        // 1. Last known data first, so the app is usable before any provider answers.
        var cached = await Task.Run(() => _sources.Select(s => ChannelCache.Load(s.Id)).ToList());
        foreach (var playlist in cached)
            _channels.AddRange(playlist?.Channels ?? []);
        if (_channels.Count > 0)
        {
            RefreshGroups();
            foreach (var source in _sources)
                await LoadCachedEpgAsync(source);
            RefreshChannels();
        }
        ShowStatus(InfoBarSeverity.Informational, "Aktualisiere Sender …");

        // 2. Fresh lists from the providers. A source that fails keeps its cached list.
        var results = await Task.WhenAll(_sources.Select(LoadSafeAsync));
        var errors = new List<string>();
        _channels.Clear();
        for (var i = 0; i < results.Length; i++)
        {
            var (source, playlist, error) = results[i];
            if (error is null)
            {
                _channels.AddRange(playlist.Channels);
                _ = Task.Run(() => ChannelCache.Save(source.Id, playlist));
            }
            else
            {
                _channels.AddRange(cached[i]?.Channels ?? []);
                errors.Add($"{source.Name}: {error}");
            }
        }
        RefreshGroups();
        ShowStatus(errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational,
            errors.Count > 0 ? string.Join("\n", errors)
                : $"{Count(MediaKind.Live)} Sender, {Count(MediaKind.Movie)} Filme, {Count(MediaKind.Series)} Serien. EPG wird aktualisiert …");

        // 3. EPG: download only when the cached file is missing or older than EpgMaxAge.
        foreach (var (source, playlist, error) in results)
        {
            if (error is not null)
                continue;
            try
            {
                var path = ChannelCache.EpgPath(source.Id);
                if (await Task.Run(() => _loader.UpdateEpgCacheAsync(source, playlist, path, EpgMaxAge)) || !_guides.ContainsKey(source.Id))
                    await LoadCachedEpgAsync(source);
            }
            catch (Exception ex)
            {
                errors.Add($"EPG {source.Name}: {ex.Message}");
            }
        }

        RefreshChannels();
        if (errors.Count > 0)
            ShowStatus(InfoBarSeverity.Warning, string.Join("\n", errors));
        else
            StatusBar.IsOpen = false;
    }

    private async Task LoadCachedEpgAsync(SourceConfig source)
    {
        var path = ChannelCache.EpgPath(source.Id);
        if (!File.Exists(path))
            return;
        try
        {
            _guides[source.Id] = await Task.Run(() => SourceLoader.LoadEpgFileAsync(path));
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException)
        {
            File.Delete(path); // broken cache file; the next refresh downloads a new one
        }
    }

    private async Task<(SourceConfig Source, PlaylistResult Playlist, string? Error)> LoadSafeAsync(SourceConfig source)
    {
        try
        {
            return (source, await Task.Run(() => _loader.LoadChannelsAsync(source)), null);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is 401 or 403 or 884)
        {
            // 884 is a panel-specific code (Xtream UI/XUI) for blocked or over-limit accounts.
            return (source, new PlaylistResult([], []),
                $"Zugang abgelehnt (HTTP {(int)ex.StatusCode!}). Prüfe die Zugangsdaten oder ob der Anbieter gerade eine weitere Verbindung erlaubt.");
        }
        catch (Exception ex)
        {
            return (source, new PlaylistResult([], []), ex.Message);
        }
    }

    private int Count(MediaKind kind) => _channels.Count(c => c.Kind == kind);

    // ---------------------------------------------------------------- lists

    // M3U playlists list series episodes one by one, so they show up under Serien too.
    private bool InSection(Channel c) => _section switch
    {
        Section.Movies => c.Kind == MediaKind.Movie,
        Section.Series => c.Kind is MediaKind.Series or MediaKind.Episode,
        _ => c.Kind == MediaKind.Live,
    };

    private static string GroupOf(Channel c) => c.Group ?? NoGroup;

    // The guide shows live channels, so it shares Live TV's hidden groups.
    private string HiddenKey(string group) => $"{SectionTitles[_section == Section.Guide ? 0 : (int)_section]} · {group}";

    private bool IsHidden(string group) => _state.HiddenGroups.Contains(HiddenKey(group));

    private bool InGroup(Channel c, string group) => group switch
    {
        AllGroups => !IsHidden(GroupOf(c)),
        FavoritesGroup => _state.Favorites.Contains(c.Id),
        _ => GroupOf(c) == group,
    };

    private EpgGuide GuideFor(Channel c)
        => c.Kind == MediaKind.Live ? _guides.GetValueOrDefault(c.SourceId) ?? EpgGuide.Empty : EpgGuide.Empty;

    private void RefreshGroups()
    {
        var previous = GroupList.SelectedItem as string;
        var items = new List<string> { AllGroups };
        if (_channels.Any(c => InSection(c) && _state.Favorites.Contains(c.Id)))
            items.Add(FavoritesGroup);
        items.AddRange(_channels.Where(InSection).Select(GroupOf).Distinct().Where(g => !IsHidden(g)).Order(StringComparer.CurrentCultureIgnoreCase));

        _refreshingGroups = true;
        GroupList.ItemsSource = items;
        GroupList.SelectedIndex = previous is null ? 0 : Math.Max(0, items.IndexOf(previous));
        _refreshingGroups = false;
        RefreshChannels();
    }

    private void RefreshChannels()
    {
        var group = GroupList.SelectedItem as string ?? AllGroups;
        var query = SearchBox.Text.Trim();
        var now = DateTimeOffset.Now;
        BackButton.Visibility = _episodes is null ? Visibility.Collapsed : Visibility.Visible;
        SectionTitle.Text = SectionTitles[(int)_section];

        // The search matches channel names and, for live channels, programmes that are on now or later.
        var items = new List<(Channel Channel, EpgProgramme? Match)>();
        foreach (var c in _episodes ?? _channels.Where(c => InSection(c) && InGroup(c, group)))
        {
            if (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                items.Add((c, null));
            else if (GuideFor(c).FindUpcoming(c, query, now) is { } programme)
                items.Add((c, programme));
        }
        CountText.Text = items.Count.ToString();

        if (_section == Section.Guide)
            ShowGuide(items.Select(i => i.Channel).ToList(), now);
        else
            ChannelList.ItemsSource = items
                .Select(i => new ChannelRow(i.Channel, GuideFor(i.Channel), now, _state.Favorites.Contains(i.Channel.Id), _state.Watched.Contains(i.Channel.Id), i.Match))
                .ToList();
    }

    private void ShowGuide(List<Channel> channels, DateTimeOffset now)
    {
        // Start half an hour back, on a half-hour boundary, so what is on now is fully visible.
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute < 30 ? 0 : 30, 0, now.Offset).AddMinutes(-30);
        var end = start.AddHours(GuideHours);

        GuideContent.Width = GuideNameWidth + GuideHours * 60 * GuidePxPerMinute;
        GuideTimeline.Children.Clear();
        for (var t = start; t < end; t = t.AddMinutes(30))
        {
            var label = new TextBlock { Text = t.ToString("HH:mm"), FontSize = 12, Opacity = 0.7 };
            Canvas.SetLeft(label, GuideNameWidth + (t - start).TotalMinutes * GuidePxPerMinute + 6);
            Canvas.SetTop(label, 10);
            GuideTimeline.Children.Add(label);
        }
        NowLine.Margin = new Thickness(GuideNameWidth + (now - start).TotalMinutes * GuidePxPerMinute, 0, 0, 0);
        GuideList.ItemsSource = channels.Select(c => new GuideRow(c, GuideFor(c), start, end, GuidePxPerMinute, now)).ToList();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedIndex < 0)
            return;
        _section = (Section)SectionList.SelectedIndex;
        _episodes = null;

        var guide = _section == Section.Guide;
        GuidePanel.Visibility = guide ? Visibility.Visible : Visibility.Collapsed;
        ChannelPane.Visibility = PlayerGrid.Visibility = guide ? Visibility.Collapsed : Visibility.Visible;
        // The video is a native window above all XAML; it has to hide while the guide covers the player.
        _player?.SetVisible(!guide && _playing is not null);
        RefreshGroups();
    }

    private void OnGroupSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingGroups)
            return;
        _episodes = null;
        RefreshChannels();
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e) => RefreshChannels();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _episodes = null;
        RefreshChannels();
    }

    private async Task ShowEpisodesAsync(Channel series)
    {
        if (_sources.Find(s => s.Id == series.SourceId) is not { } source || series.SeriesId is null)
            return;
        try
        {
            _episodes = await Task.Run(() => _loader.LoadEpisodesAsync(source, series.SeriesId));
            BackLabel.Text = series.Name;
            SearchBox.Text = "";
            RefreshChannels();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            ShowStatus(InfoBarSeverity.Error, $"Episoden von {series.Name}: {ex.Message}");
        }
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChannelRow row)
            return;
        row.IsFavorite = !row.IsFavorite;
        if (row.IsFavorite)
            _state.Favorites.Add(row.Channel.Id);
        else
            _state.Favorites.Remove(row.Channel.Id);
        _state.Save();

        // Only rebuild the lists when the Favoriten group has to appear or disappear.
        var hasGroup = GroupList.ItemsSource is List<string> groups && groups.Contains(FavoritesGroup);
        if (hasGroup != _channels.Any(c => InSection(c) && _state.Favorites.Contains(c.Id)))
            RefreshGroups();
    }

    private void OnHideGroup(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string group || group is AllGroups or FavoritesGroup)
            return;
        _state.HiddenGroups.Add(HiddenKey(group));
        _state.Save();
        RefreshGroups();
    }

    // ---------------------------------------------------------------- playback

    private void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelRow row)
            return;
        if (row.Channel.Kind == MediaKind.Series)
        {
            _ = ShowEpisodesAsync(row.Channel);
            return;
        }
        if (ChannelList.ItemsSource is List<ChannelRow> rows)
            _playQueue = rows.ConvertAll(r => r.Channel);
        Play(row.Channel);
    }

    private void Play(Channel channel)
    {
        if (_player is null)
        {
            // Shown on click because the startup message is replaced by the loading status.
            ShowStatus(InfoBarSeverity.Error, _playerError ?? "Player nicht verfügbar.");
            return;
        }

        SaveResume();
        var now = DateTimeOffset.Now;
        var (current, next) = GuideFor(channel).NowNext(channel, now);
        NowChannel.Text = channel.Name;
        NowLogo.Source = Images.FromUrl(channel.LogoUrl);
        NowTime.Text = current is null ? "" : $"{current.Start.ToLocalTime():HH:mm} – {current.Stop.ToLocalTime():HH:mm}";
        NowProgramme.Text = current?.Title ?? "";
        NowProgress.Value = current?.Progress(now) ?? 0;
        NowProgress.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        NextProgramme.Text = next is null ? "" : $"Danach {next.Start.ToLocalTime():HH:mm}  {next.Title}";

        try
        {
            var live = channel.Kind == MediaKind.Live;
            var resume = live ? 0 : _state.Resume.GetValueOrDefault(channel.Id);
            _player.Play(channel.StreamUrl, channel.UserAgent, channel.Referrer, resume);
            _playing = channel;
            _reconnects = 0;
            SetPaused(false);
            if (resume > 0)
                ShowStatus(InfoBarSeverity.Informational, $"Weiter ab {TimeSpan.FromSeconds(resume):h\\:mm\\:ss}");

            LiveButton.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            SeekBackButton.Visibility = SeekForwardButton.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
            SyncBar();
            UpdateVideoBounds();
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    /// <summary>Remembers where a movie or episode was left; skips the first minute and the credits.</summary>
    private void SaveResume()
    {
        if (_player is null || _playing is null || _playing.Kind == MediaKind.Live)
            return;
        var position = _player.GetDouble("time-pos");
        var duration = _player.GetDouble("duration");
        if (position is null)
            return;

        if (position > 60 && (duration is null || position < duration * 0.95))
            _state.Resume[_playing.Id] = position.Value;
        else
            _state.Resume.Remove(_playing.Id);
        if (position >= duration * 0.95)
            _state.Watched.Add(_playing.Id);
        _state.Save();
    }

    private void OnPlaybackStarted()
    {
        _lastStart = DateTime.UtcNow;
        if (_reconnects > 0)
            StatusBar.IsOpen = false; // the "verbinde neu" message
    }

    private async void OnPlaybackEnded(string? error)
    {
        if (_playing is not { } channel)
            return;

        if (channel.Kind == MediaKind.Live)
        {
            // A live stream never ends on purpose. A stream that played for a while starts with a fresh set of tries.
            if (DateTime.UtcNow - _lastStart > TimeSpan.FromMinutes(1))
            {
                _reconnects = 0;
                _lastStart = DateTime.UtcNow; // failed tries come within seconds and count up from here
            }
            if (_reconnects >= MaxReconnects)
            {
                ShowStatus(InfoBarSeverity.Error, $"{channel.Name}: Verbindung verloren{(error is null ? "" : $" ({error})")}. Drück LIVE für einen neuen Versuch.");
                return;
            }
            _reconnects++;
            ShowStatus(InfoBarSeverity.Warning, $"Verbindung unterbrochen, verbinde neu ({_reconnects}/{MaxReconnects}) …");
            await Task.Delay(TimeSpan.FromSeconds(2 * _reconnects));
            if (_playing == channel)
                _player?.Play(channel.StreamUrl, channel.UserAgent, channel.Referrer);
            return;
        }

        if (error is not null)
        {
            ShowStatus(InfoBarSeverity.Error, $"Wiedergabe fehlgeschlagen: {error}");
            return;
        }

        _state.Resume.Remove(channel.Id);
        _state.Watched.Add(channel.Id);
        _state.Save();
        var index = _playQueue.IndexOf(channel);
        if (channel.Kind == MediaKind.Episode && index >= 0 && index + 1 < _playQueue.Count && _playQueue[index + 1].Kind == MediaKind.Episode)
        {
            var next = _playQueue[index + 1];
            ShowStatus(InfoBarSeverity.Informational, $"Nächste Folge: {next.Name}");
            Play(next);
        }
        RefreshChannels(); // shows the watched mark
    }

    private bool IsVod => _playing is { Kind: not MediaKind.Live };

    private void StepChannel(int delta)
    {
        if (_section == Section.Guide || ChannelList.Items.Count == 0)
            return;
        ChannelList.SelectedIndex = Math.Clamp(ChannelList.SelectedIndex + delta, 0, ChannelList.Items.Count - 1);
        ChannelList.ScrollIntoView(ChannelList.SelectedItem);
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        PlayPauseIcon.Glyph = paused ? "" : "";
        SyncBar();
    }

    private void TogglePause()
    {
        _player?.TogglePause();
        SetPaused(!_paused);
    }

    private void ToggleMute() => SetMuted(!_muted);

    private void SetMuted(bool muted)
    {
        _muted = muted;
        _player?.SetProperty("mute", muted ? "yes" : "no");
        MuteIcon.Glyph = muted ? "" : "";
        SyncBar();
    }

    private void SetVolume(double volume)
    {
        volume = Math.Clamp(Math.Round(volume), 0, 100);
        _state.Volume = volume;
        _player?.SetProperty("volume", volume.ToString(CultureInfo.InvariantCulture));
        if (_muted)
            SetMuted(false);
        if (VolumeSlider.Value != volume)
            VolumeSlider.Value = volume; // raises OnVolumeChanged once more, which finds nothing to change
        SyncBar();
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue != _state.Volume)
            SetVolume(e.NewValue);
    }

    private void OnPause(object sender, RoutedEventArgs e) => TogglePause();

    private void OnMute(object sender, RoutedEventArgs e) => ToggleMute();

    private void OnSeekBack(object sender, RoutedEventArgs e) => _player?.Seek(-10);

    private void OnSeekForward(object sender, RoutedEventArgs e) => _player?.Seek(10);

    /// <summary>Reopens the live stream, which jumps back to the live edge after a stall.</summary>
    private void OnLive(object sender, RoutedEventArgs e)
    {
        if (_playing is { } c)
            _player?.Play(c.StreamUrl, c.UserAgent, c.Referrer);
        _reconnects = 0;
        SetPaused(false);
    }

    // Tracks, picture format and stream info. Shared by the bar's flyout and the fullscreen bar's copy of it.
    private void OnTracksOpening(object? sender, object e)
    {
        var flyout = (MenuFlyout)sender!;
        flyout.Items.Clear();
        var tracks = _player?.Tracks() ?? [];
        AddTracks(flyout, "Tonspur", "audio", "aid", tracks, allowOff: false);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddTracks(flyout, "Untertitel", "sub", "sid", tracks, allowOff: true);

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(new MenuFlyoutItem { Text = "Bildformat", IsEnabled = false });
        for (var i = 0; i < PictureFormats.Length; i++)
        {
            var format = i;
            var item = new ToggleMenuFlyoutItem { Text = PictureFormats[i].Label, IsChecked = i == _pictureFormat };
            item.Click += (_, _) => SetPictureFormat(format);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var stats = new MenuFlyoutItem { Text = "Stream-Info ein/aus (I)" };
        stats.Click += (_, _) => _player?.ToggleStats();
        flyout.Items.Add(stats);
    }

    // mpv keeps these properties across files, so the format stays when switching channels.
    private void SetPictureFormat(int index)
    {
        _pictureFormat = index;
        var (_, aspect, panscan, keepAspect) = PictureFormats[index];
        _player?.SetProperty("video-aspect-override", aspect);
        _player?.SetProperty("panscan", panscan);
        _player?.SetProperty("keepaspect", keepAspect);
    }

    private void AddTracks(MenuFlyout flyout, string header, string type, string property, IReadOnlyList<MpvTrack> tracks, bool allowOff)
    {
        flyout.Items.Add(new MenuFlyoutItem { Text = header, IsEnabled = false });
        var ofType = tracks.Where(t => t.Type == type).ToList();
        if (allowOff)
            AddTrackItem(flyout, "Aus", !ofType.Any(t => t.Selected), property, "no");
        foreach (var track in ofType)
            AddTrackItem(flyout, track.Label, track.Selected, property, track.Id);
        if (ofType.Count == 0 && !allowOff)
            flyout.Items.Add(new MenuFlyoutItem { Text = "Keine", IsEnabled = false });
    }

    private void AddTrackItem(MenuFlyout flyout, string text, bool selected, string property, string value)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = selected };
        item.Click += (_, _) => _player?.SetProperty(property, value);
        flyout.Items.Add(item);
    }

    // ---------------------------------------------------------------- guide

    private void OnGuideBlockClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuideBlock block)
            PlayFromGuide(block.Channel);
    }

    private void OnGuideChannelClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuideRow row)
            PlayFromGuide(row.Channel);
    }

    private void PlayFromGuide(Channel channel)
    {
        SectionList.SelectedIndex = (int)Section.Live; // keeps the selected group, see RefreshGroups
        if (ChannelList.ItemsSource is List<ChannelRow> rows && rows.Find(r => r.Channel.Id == channel.Id) is { } row)
        {
            ChannelList.SelectedItem = row;
            ChannelList.ScrollIntoView(row);
        }
        else
        {
            Play(channel);
        }
    }

    // ---------------------------------------------------------------- input

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or PasswordBox)
            return;
        // Lists keep their own arrow navigation; selecting a channel there plays it anyway.
        var inList = e.OriginalSource is ListViewItem or ListView;

        switch (e.Key)
        {
            case VirtualKey.Space:
                TogglePause();
                break;
            case VirtualKey.F:
                ToggleFullScreen();
                break;
            case VirtualKey.Escape when _mode != ViewMode.Normal:
                SetMode(ViewMode.Normal);
                break;
            case VirtualKey.P:
                ToggleMini();
                break;
            case VirtualKey.I:
                _player?.ToggleStats();
                break;
            case VirtualKey.M:
                ToggleMute();
                break;
            case VirtualKey.Left when IsVod:
                _player?.Seek(-10);
                break;
            case VirtualKey.Right when IsVod:
                _player?.Seek(10);
                break;
            case VirtualKey.Up when !inList:
                StepChannel(-1);
                break;
            case VirtualKey.Down when !inList:
                StepChannel(1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Input that arrived on the video window, forwarded by mpv (see MpvPlayer.InputConf).</summary>
    private void OnPlayerMessage(string name, string? arg)
    {
        switch (name)
        {
            // Double-click: a mini player goes back to the normal window rather than to fullscreen.
            case "fullscreen" when _mode == ViewMode.Mini:
            case "exit-fullscreen":
                SetMode(ViewMode.Normal);
                break;
            case "fullscreen":
                ToggleFullScreen();
                break;
            case "mini":
                ToggleMini();
                break;
            case "channel" when int.TryParse(arg, out var delta):
                StepChannel(delta);
                break;
            case "volume" when int.TryParse(arg, out var step):
                SetVolume(_state.Volume + step);
                break;
            case "mute":
                ToggleMute();
                break;
        }
    }

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OnPlayerMessage("fullscreen", null);

    // ---------------------------------------------------------------- video window and fullscreen

    private void OnVideoHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateVideoBounds();

    private void UpdateVideoBounds()
    {
        if (_player is null || VideoHost.XamlRoot is null)
            return;
        var scale = VideoHost.XamlRoot.RasterizationScale;
        var origin = VideoHost.TransformToVisual(null).TransformPoint(new Point(0, 0));
        _player.SetBounds((int)(origin.X * scale), (int)(origin.Y * scale),
            (int)(VideoHost.ActualWidth * scale), (int)(VideoHost.ActualHeight * scale));
    }

    private bool IsFullScreen => _mode == ViewMode.FullScreen;

    private void OnFullScreen(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void OnMini(object sender, RoutedEventArgs e) => ToggleMini();

    private void ToggleFullScreen() => SetMode(IsFullScreen ? ViewMode.Normal : ViewMode.FullScreen);

    private void ToggleMini() => SetMode(_mode == ViewMode.Mini ? ViewMode.Normal : ViewMode.Mini);

    /// <summary>Normal window, fullscreen, or the mini player: a small always-on-top window with only the video.</summary>
    private void SetMode(ViewMode mode)
    {
        if (mode == _mode || (_section == Section.Guide && mode != ViewMode.Normal))
            return;
        if (_mode == ViewMode.Normal && mode == ViewMode.Mini)
            _restoreBounds = new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        _mode = mode;

        _idleTimer.Stop();
        _bar?.Window.AppWindow.Hide();
        AppWindow.SetPresenter(mode switch
        {
            ViewMode.FullScreen => AppWindowPresenterKind.FullScreen,
            ViewMode.Mini => AppWindowPresenterKind.CompactOverlay,
            _ => AppWindowPresenterKind.Default,
        });
        FullScreenIcon.Glyph = IsFullScreen ? "\uE73F" : "\uE740";

        var normal = mode == ViewMode.Normal;
        // The mini player keeps the title bar, so it can be dragged and closed.
        TitleBarRow.Height = new GridLength(IsFullScreen ? 0 : 44);
        SidebarColumn.Width = new GridLength(normal ? 248 : 0);
        ChannelsColumn.Width = new GridLength(normal ? 400 : 0);
        PlayerGrid.Margin = normal ? new Thickness(0, 0, 12, 12) : new Thickness(0);
        // Outside the normal window the controls are in FullscreenBar's own window (fullscreen) or keys only (mini).
        InfoPanel.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
        if (!normal)
            StatusBar.IsOpen = false;

        if (mode == ViewMode.FullScreen)
        {
            _bar ??= CreateBar();
            SyncBar();
            PositionBar();
            _bar.Window.AppWindow.Show(false);
            GetCursorPos(out _lastCursor);
            _lastCursorMove = DateTime.UtcNow;
            _idleTimer.Start();
        }
        else if (mode == ViewMode.Mini)
        {
            var scale = Content.XamlRoot?.RasterizationScale ?? 1;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(480 * scale), (int)((270 + 44) * scale)));
        }
        else if (_restoreBounds is { } bounds)
        {
            AppWindow.MoveAndResize(bounds);
            _restoreBounds = null;
        }
    }

    private FullscreenBar CreateBar()
    {
        var bar = new FullscreenBar(this);
        ((UIElement)bar.Window.Content).PreviewKeyDown += OnPreviewKeyDown;
        bar.Live.Click += OnLive;
        bar.SeekBack.Click += OnSeekBack;
        bar.SeekForward.Click += OnSeekForward;
        bar.Pause.Click += OnPause;
        bar.Mute.Click += OnMute;
        bar.Exit.Click += OnFullScreen;
        bar.Tracks.Opening += OnTracksOpening;
        bar.Volume.Value = _state.Volume;
        bar.Volume.ValueChanged += OnVolumeChanged;
        return bar;
    }

    /// <summary>Copies the player state shown in InfoPanel to the fullscreen bar.</summary>
    private void SyncBar()
    {
        if (_bar is null)
            return;
        _bar.Channel.Text = NowChannel.Text;
        _bar.Programme.Text = string.IsNullOrEmpty(NowProgramme.Text) ? "" : $"{NowTime.Text}  {NowProgramme.Text}";
        _bar.Live.Visibility = LiveButton.Visibility;
        _bar.SeekBack.Visibility = _bar.SeekForward.Visibility = SeekBackButton.Visibility;
        _bar.PauseIcon.Glyph = PlayPauseIcon.Glyph;
        _bar.MuteIcon.Glyph = MuteIcon.Glyph;
        if (_bar.Volume.Value != _state.Volume)
            _bar.Volume.Value = _state.Volume;
    }

    private void PositionBar()
    {
        var height = (int)(FullscreenBar.HeightDip * (Content.XamlRoot?.RasterizationScale ?? 1));
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        _bar!.Window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(pos.X, pos.Y + size.Height - height, size.Width, height));
    }

    private void OnIdleTick(object? sender, object e)
    {
        GetCursorPos(out var cursor);
        // Sensor jitter and touchpads report tiny moves without anyone touching the mouse; ignore those.
        var moved = Math.Abs(cursor.X - _lastCursor.X) + Math.Abs(cursor.Y - _lastCursor.Y) > CursorMoveThreshold;
        if (moved)
            _lastCursor = cursor;
        // Only moves on the fullscreen monitor count; working on a second screen must not bring up the bar.
        if (moved && IsOnWindow(cursor))
        {
            _lastCursorMove = DateTime.UtcNow;
            if (_bar is { Window.AppWindow.IsVisible: false })
            {
                PositionBar();
                _bar.Window.AppWindow.Show(false);
            }
        }
        else if (DateTime.UtcNow - _lastCursorMove > TimeSpan.FromSeconds(1.5))
        {
            _bar?.Window.AppWindow.Hide();
        }
    }

    private const int CursorMoveThreshold = 12; // physical pixels

    private bool IsOnWindow(CursorPoint cursor)
    {
        // Both AppWindow and GetCursorPos use physical screen pixels (per-monitor DPI aware).
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        return cursor.X >= pos.X && cursor.X < pos.X + size.Width && cursor.Y >= pos.Y && cursor.Y < pos.Y + size.Height;
    }

    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    // ---------------------------------------------------------------- sources and settings

    private async void OnAddSource(object sender, RoutedEventArgs e)
    {
        var kind = new ComboBox { Header = "Typ", ItemsSource = new[] { "M3U-Playlist", "Xtream Codes" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = "Name" };
        var location = new TextBox { Header = "Playlist-URL oder Datei / Xtream-Server" };
        var user = new TextBox { Header = "Benutzername (Xtream)" };
        var pass = new PasswordBox { Header = "Passwort (Xtream)" };
        var epg = new TextBox { Header = "EPG-URL (optional, XMLTV)" };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Quelle hinzufügen",
            PrimaryButtonText = "Hinzufügen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel { Spacing = 8, Width = 400, Children = { kind, name, location, user, pass, epg } },
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(location.Text))
            return;

        var xtream = kind.SelectedIndex == 1;
        // Adding the same playlist again replaces it; duplicates would hit the provider in parallel.
        var username = xtream ? user.Text.Trim() : null;
        _sources.RemoveAll(s => string.Equals(s.M3uLocation ?? s.Server, location.Text.Trim(), StringComparison.OrdinalIgnoreCase) && s.Username == username);
        _sources.Add(new SourceConfig
        {
            Name = string.IsNullOrWhiteSpace(name.Text) ? location.Text.Trim() : name.Text.Trim(),
            Kind = xtream ? SourceKind.Xtream : SourceKind.M3u,
            M3uLocation = xtream ? null : location.Text.Trim(),
            Server = xtream ? location.Text.Trim() : null,
            Username = username,
            Password = xtream ? pass.Password : null,
            EpgUrl = string.IsNullOrWhiteSpace(epg.Text) ? null : epg.Text.Trim(),
        });
        SourceStore.Save(_sources);
        await ReloadAsync();
    }

    private async void OnManageSources(object sender, RoutedEventArgs e)
    {
        var sourceList = new ListView { SelectionMode = ListViewSelectionMode.Multiple, ItemsSource = _sources.Select(s => s.Name).ToList() };
        var hidden = _state.HiddenGroups.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var hiddenList = new ListView { SelectionMode = ListViewSelectionMode.Multiple, ItemsSource = hidden };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Einstellungen",
            PrimaryButtonText = "Markierte entfernen bzw. einblenden",
            CloseButtonText = "Schließen",
            Content = new StackPanel
            {
                Spacing = 8,
                Width = 420,
                Children =
                {
                    new TextBlock { Text = "Quellen", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    _sources.Count == 0 ? new TextBlock { Text = "Keine Quellen vorhanden." } : sourceList,
                    new TextBlock { Text = "Ausgeblendete Gruppen", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) },
                    hidden.Count == 0 ? new TextBlock { Text = "Keine. Rechtsklick auf eine Gruppe blendet sie aus." } : hiddenList,
                },
            },
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var removedSources = Selected(sourceList).Select(i => _sources[i]).ToList();
        foreach (var i in Selected(hiddenList))
            _state.HiddenGroups.Remove(hidden[i]);
        _state.Save();

        if (removedSources.Count == 0)
        {
            RefreshGroups();
            return;
        }
        foreach (var source in removedSources)
            ChannelCache.Delete(source.Id);
        _sources.RemoveAll(removedSources.Contains);
        SourceStore.Save(_sources);
        await ReloadAsync();
    }

    private static IEnumerable<int> Selected(ListView list)
        => list.SelectedRanges.SelectMany(r => Enumerable.Range(r.FirstIndex, (int)r.Length));

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.ActionButton = null;
        StatusBar.IsOpen = _mode == ViewMode.Normal;
    }
}
