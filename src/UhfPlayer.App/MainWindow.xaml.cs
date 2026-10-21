using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UhfPlayer.Core;
using UhfPlayer.Core.Epg;
using UhfPlayer.Core.Models;
using Windows.Foundation;

namespace UhfPlayer.App;

public sealed partial class MainWindow : Window
{
    private const string AllGroups = "Alle Sender";

    private static readonly HttpClient Http = new(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
    private readonly SourceLoader _loader = new(Http);
    private readonly List<SourceConfig> _sources = SourceStore.Load();
    private readonly List<Channel> _channels = [];
    private readonly Dictionary<Guid, EpgGuide> _guides = [];
    private readonly MpvPlayer? _player;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 900));

        try
        {
            _player = new MpvPlayer(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch (DllNotFoundException)
        {
            ShowStatus(InfoBarSeverity.Error, "libmpv-2.dll fehlt. Bitte scripts/fetch-libmpv.ps1 ausführen.");
        }

        Closed += (_, _) => _player?.Dispose();
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
            errors.Count > 0 ? string.Join("\n", errors) : $"{_channels.Count} Sender geladen, EPG wird geladen …");

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
        catch (Exception ex)
        {
            return (source, new PlaylistResult([], []), ex.Message);
        }
    }

    private void RefreshGroups()
    {
        var groups = _channels.Select(c => c.Group ?? "Ohne Gruppe").Distinct().Order(StringComparer.CurrentCultureIgnoreCase);
        GroupList.ItemsSource = new[] { AllGroups }.Concat(groups).ToList();
        GroupList.SelectedIndex = 0;
        RefreshChannels();
    }

    private void RefreshChannels()
    {
        var group = GroupList.SelectedItem as string;
        var query = SearchBox.Text.Trim();
        var now = DateTimeOffset.Now;

        ChannelList.ItemsSource = _channels
            .Where(c => group is null or AllGroups || (c.Group ?? "Ohne Gruppe") == group)
            .Where(c => query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(c => new ChannelRow(c, _guides.GetValueOrDefault(c.SourceId) ?? EpgGuide.Empty, now))
            .ToList();
    }

    private void OnGroupSelected(object sender, SelectionChangedEventArgs e) => RefreshChannels();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshChannels();

    private void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelList.SelectedItem is not ChannelRow row)
            return;

        NowChannel.Text = row.Channel.Name;
        NowProgramme.Text = row.Now is { } now ? $"{now.Start:HH:mm} – {now.Stop:HH:mm}  {now.Title}" : "";
        NextProgramme.Text = row.Next is { } next ? $"Danach {next.Start:HH:mm}  {next.Title}" : "";

        try
        {
            _player?.Play(row.Channel.StreamUrl, row.Channel.UserAgent, row.Channel.Referrer);
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

    private void OnFullScreen(object sender, RoutedEventArgs e)
    {
        var full = AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(full ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        GroupsColumn.Width = ChannelsColumn.Width = new GridLength(0);
        if (!full)
        {
            GroupsColumn.Width = new GridLength(220);
            ChannelsColumn.Width = new GridLength(380);
        }
    }

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
        _sources.Add(new SourceConfig
        {
            Name = string.IsNullOrWhiteSpace(name.Text) ? location.Text.Trim() : name.Text.Trim(),
            Kind = xtream ? SourceKind.Xtream : SourceKind.M3u,
            M3uLocation = xtream ? null : location.Text.Trim(),
            Server = xtream ? location.Text.Trim() : null,
            Username = xtream ? user.Text.Trim() : null,
            Password = xtream ? pass.Password : null,
            EpgUrl = string.IsNullOrWhiteSpace(epg.Text) ? null : epg.Text.Trim(),
        });
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
