using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UhfPlayer.Core;
using UhfPlayer.Core.Epg;
using UhfPlayer.Core.Models;
using Windows.Foundation;

namespace UhfPlayer.App;

public sealed partial class MainWindow : Window
{
    private const string AllGroups = "Alle";
    private static readonly MediaKind[] Sections = [MediaKind.Live, MediaKind.Movie, MediaKind.Series];

    private static readonly HttpClient Http = new(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
    {
        DefaultRequestHeaders = { { "User-Agent", SourceLoader.DefaultUserAgent } },
    };
    private readonly SourceLoader _loader = new(Http);
    private readonly List<SourceConfig> _sources = SourceStore.Load();
    private readonly List<Channel> _channels = [];
    private readonly Dictionary<Guid, EpgGuide> _guides = [];
    private readonly MpvPlayer? _player;
    private readonly string? _playerError;
    private Channel? _playing;

    // Mouse moves over the video reach mpv's window, not XAML, so fullscreen idleness is detected by polling the cursor.
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CursorPoint _lastCursor;
    private DateTime _lastCursorMove;
    private Window? _overlay;
    private MediaKind _section = MediaKind.Live;

    /// <summary>Episodes of the series being browsed; null while the normal list is shown.</summary>
    private IReadOnlyList<Channel>? _episodes;

    public MainWindow()
    {
        InitializeComponent();
        SectionBar.SelectedItem = SectionBar.Items[0];
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 900));

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
            _player.PlaybackFailed += error => DispatcherQueue.TryEnqueue(() => ShowStatus(InfoBarSeverity.Error, $"Wiedergabe fehlgeschlagen: {error}"));

        _idleTimer.Tick += OnIdleTick;
        Closed += (_, _) =>
        {
            _idleTimer.Stop();
            _player?.Dispose();
        };
        _ = ReloadAsync();
    }

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

        ShowStatus(InfoBarSeverity.Informational, "Lade Sender …");
        var playlists = await Task.WhenAll(_sources.Select(LoadSafeAsync));
        foreach (var (_, playlist, _) in playlists)
            _channels.AddRange(playlist.Channels);
        RefreshGroups();

        var errors = playlists.Where(p => p.Error is not null).Select(p => $"{p.Source.Name}: {p.Error}").ToList();
        ShowStatus(errors.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational,
            errors.Count > 0 ? string.Join("\n", errors)
                : $"{Count(MediaKind.Live)} Sender, {Count(MediaKind.Movie)} Filme, {Count(MediaKind.Series)} Serien geladen, EPG wird geladen …");

        // EPG files are large; load them after the channel list is already usable.
        foreach (var (source, playlist, _) in playlists)
        {
            try
            {
                _guides[source.Id] = await Task.Run(() => _loader.LoadEpgAsync(source, playlist));
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

    // M3U playlists list series episodes one by one, so they show up under Serien too.
    private bool InSection(Channel c) => c.Kind == _section || (_section == MediaKind.Series && c.Kind == MediaKind.Episode);

    private void RefreshGroups()
    {
        _episodes = null;
        var groups = _channels.Where(InSection).Select(c => c.Group ?? "Ohne Gruppe").Distinct().Order(StringComparer.CurrentCultureIgnoreCase);
        GroupList.ItemsSource = new[] { AllGroups }.Concat(groups).ToList();
        GroupList.SelectedIndex = 0;
        RefreshChannels();
    }

    private void RefreshChannels()
    {
        var group = GroupList.SelectedItem as string;
        var query = SearchBox.Text.Trim();
        var now = DateTimeOffset.Now;
        BackButton.Visibility = _episodes is null ? Visibility.Collapsed : Visibility.Visible;

        var items = _episodes ?? _channels.Where(c => InSection(c) && (group is null or AllGroups || (c.Group ?? "Ohne Gruppe") == group));
        ChannelList.ItemsSource = items
            .Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(c => new ChannelRow(c, c.Kind == MediaKind.Live ? _guides.GetValueOrDefault(c.SourceId) ?? EpgGuide.Empty : EpgGuide.Empty, now))
            .ToList();
    }

    private void OnSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        _section = Sections[Math.Max(0, sender.Items.IndexOf(sender.SelectedItem))];
        RefreshGroups();
    }

    private void OnGroupSelected(object sender, SelectionChangedEventArgs e)
    {
        _episodes = null;
        RefreshChannels();
    }

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

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshChannels();

    private void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelRow row)
            return;
        if (row.Channel.Kind == MediaKind.Series)
        {
            _ = ShowEpisodesAsync(row.Channel);
            return;
        }

        NowChannel.Text = row.Channel.Name;
        NowProgramme.Text = row.Now is { } now ? $"{now.Start:HH:mm} – {now.Stop:HH:mm}  {now.Title}" : "";
        NextProgramme.Text = row.Next is { } next ? $"Danach {next.Start:HH:mm}  {next.Title}" : "";

        try
        {
            if (_player is null)
            {
                // Shown on click because the startup message is replaced by the loading status.
                ShowStatus(InfoBarSeverity.Error, _playerError ?? "Player nicht verfügbar.");
                return;
            }
            _player.Play(row.Channel.StreamUrl, row.Channel.UserAgent, row.Channel.Referrer);
            _playing = row.Channel;
            var live = row.Channel.Kind == MediaKind.Live;
            LiveButton.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            SeekBackButton.Visibility = SeekForwardButton.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
            UpdateVideoBounds();
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

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

    private void OnPause(object sender, RoutedEventArgs e) => _player?.TogglePause();

    private void OnMute(object sender, RoutedEventArgs e) => _player?.ToggleMute();

    private void OnSeekBack(object sender, RoutedEventArgs e) => _player?.Seek(-10);

    private void OnSeekForward(object sender, RoutedEventArgs e) => _player?.Seek(10);

    /// <summary>Reopens the live stream, which jumps back to the live edge after a stall.</summary>
    private void OnLive(object sender, RoutedEventArgs e)
    {
        if (_playing is { } c)
            _player?.Play(c.StreamUrl, c.UserAgent, c.Referrer);
    }

    private void OnFullScreen(object sender, RoutedEventArgs e)
    {
        var full = AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(full ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        if (full)
        {
            GroupsColumn.Width = ChannelsColumn.Width = new GridLength(0);
            PlayerGrid.Padding = new Thickness(0);
            // The video is a native child window that covers any XAML, so in fullscreen the bar moves into its
            // own window on top. The video keeps the whole screen and never resizes when the bar shows or hides.
            PlayerGrid.Children.Remove(InfoPanel);
            _overlay ??= CreateOverlay();
            ((Grid)_overlay.Content).Children.Add(InfoPanel);
            PositionOverlay();
            _overlay.AppWindow.Show(false);
            GetCursorPos(out _lastCursor);
            _lastCursorMove = DateTime.UtcNow;
            _idleTimer.Start();
        }
        else
        {
            _idleTimer.Stop();
            _overlay?.AppWindow.Hide();
            ((Grid?)_overlay?.Content)?.Children.Remove(InfoPanel);
            PlayerGrid.Children.Add(InfoPanel);
            PlayerGrid.Padding = new Thickness(6, 12, 12, 12);
            GroupsColumn.Width = new GridLength(220);
            ChannelsColumn.Width = new GridLength(380);
        }
    }

    private Window CreateOverlay()
    {
        var overlay = new Window { Content = new Grid { Padding = new Thickness(24, 12, 24, 16) }, SystemBackdrop = new DesktopAcrylicBackdrop() };
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = presenter.IsMinimizable = false;
        overlay.AppWindow.SetPresenter(presenter);
        overlay.AppWindow.IsShownInSwitchers = false;
        // Owned by the main window: always above it, never in the taskbar, closes with it.
        SetWindowLongPtrW(WinRT.Interop.WindowNative.GetWindowHandle(overlay), GWLP_HWNDPARENT, WinRT.Interop.WindowNative.GetWindowHandle(this));
        Closed += (_, _) => overlay.Close();
        return overlay;
    }

    private void PositionOverlay()
    {
        var height = (int)(130 * (Content.XamlRoot?.RasterizationScale ?? 1));
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        _overlay!.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(pos.X, pos.Y + size.Height - height, size.Width, height));
    }

    private void OnIdleTick(object? sender, object e)
    {
        GetCursorPos(out var cursor);
        // Sensor jitter and touchpads report tiny moves without anyone touching the mouse; ignore those.
        if (Math.Abs(cursor.X - _lastCursor.X) + Math.Abs(cursor.Y - _lastCursor.Y) > CursorMoveThreshold)
        {
            _lastCursor = cursor;
            _lastCursorMove = DateTime.UtcNow;
            if (_overlay is { AppWindow.IsVisible: false })
            {
                PositionOverlay();
                _overlay.AppWindow.Show(false);
            }
        }
        else if (DateTime.UtcNow - _lastCursorMove > TimeSpan.FromSeconds(1.5))
        {
            _overlay?.AppWindow.Hide();
        }
    }

    private const int CursorMoveThreshold = 12; // physical pixels

    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    private const int GWLP_HWNDPARENT = -8;

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

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
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, ItemsSource = _sources.Select(s => s.Name).ToList() };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Quellen",
            PrimaryButtonText = "Markierte entfernen",
            CloseButtonText = "Schließen",
            Content = _sources.Count == 0 ? new TextBlock { Text = "Keine Quellen vorhanden." } : list,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedRanges.Count == 0)
            return;

        var remove = list.SelectedRanges.SelectMany(r => Enumerable.Range(r.FirstIndex, (int)r.Length)).Select(i => _sources[i]).ToList();
        _sources.RemoveAll(remove.Contains);
        SourceStore.Save(_sources);
        await ReloadAsync();
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
