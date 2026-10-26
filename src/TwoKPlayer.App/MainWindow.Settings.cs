using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.App;

/// <summary>
/// The settings page: a category list on the left like the main sidebar, cards on the right like Windows' own settings.
/// It replaces the main view while open, and the video, a native window above all XAML, hides meanwhile.
/// </summary>
public sealed partial class MainWindow
{
    // Same order as the items of SettingsNav.
    private enum SettingsCategory
    {
        General,
        Sources,
        Hidden,
        About,
    }

    private const string SourceCodeUrl = "https://github.com/jvstin2k/2k-player";
    private const string ReleasesUrl = "https://github.com/jvstin2k/2k-player-releases/releases";

    private bool SettingsOpen => SettingsPanel.Visibility == Visibility.Visible;

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings(SettingsCategory.General);

    private void OpenSettings(SettingsCategory category)
    {
        MainGrid.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        _player?.SetVisible(VideoShown);
        if (SettingsNav.SelectedIndex == (int)category)
            ShowSettingsCategory();
        else
            SettingsNav.SelectedIndex = (int)category;
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => CloseSettings();

    private void CloseSettings()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        MainGrid.Visibility = Visibility.Visible;
        _player?.SetVisible(VideoShown);
    }

    private void OnSettingsCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsNav.SelectedIndex >= 0)
            ShowSettingsCategory();
    }

    /// <summary>Builds the selected category anew, also after a change on it, so it always shows the current state.</summary>
    private void ShowSettingsCategory()
    {
        var category = (SettingsCategory)SettingsNav.SelectedIndex;
        SettingsTitle.Text = category switch
        {
            SettingsCategory.Sources => Strings.Sources,
            SettingsCategory.Hidden => Strings.HiddenChannels,
            SettingsCategory.About => Strings.About,
            _ => Strings.General,
        };
        SettingsContent.Children.Clear();
        switch (category)
        {
            case SettingsCategory.Sources:
                ShowSourceSettings();
                break;
            case SettingsCategory.Hidden:
                ShowHiddenSettings();
                break;
            case SettingsCategory.About:
                ShowAbout();
                break;
            default:
                ShowGeneralSettings();
                break;
        }
    }

    // ---------------------------------------------------------------- general

    private void ShowGeneralSettings()
    {
        // The texts are set once at start, so a new language shows after a restart, offered right here.
        var languages = new[] { Strings.English, Strings.German };
        var language = new ComboBox
        {
            ItemsSource = new[] { "English", "Deutsch" },
            SelectedIndex = Array.IndexOf(languages, _state.Language),
            MinWidth = 160,
        };
        var restartButton = new Button { Content = Strings.RestartNow, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        restartButton.Click += (_, _) => Restart();
        var restartBar = new InfoBar
        {
            Message = Strings.RestartForLanguage,
            IsClosable = false,
            IsOpen = _state.Language != Strings.Language,
            ActionButton = restartButton,
        };
        language.SelectionChanged += (_, _) =>
        {
            _state.Language = languages[language.SelectedIndex];
            _state.Save();
            restartBar.IsOpen = _state.Language != Strings.Language;
        };
        SettingsContent.Children.Add(SettingsCard("\uF2B7", Strings.LanguageHeader, Strings.LanguageDescription, language));
        SettingsContent.Children.Add(restartBar);

        SettingsContent.Children.Add(SettingsHeader(Strings.Updates));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Foreground = SecondaryText };
        var check = new Button { Content = Strings.CheckForUpdates, IsEnabled = _updater.IsInstalled };
        var install = new Button { Content = Strings.UpdateNow, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        void ShowState(string text, bool canInstall)
        {
            status.Text = text;
            status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            install.Visibility = canInstall ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!_updater.IsInstalled)
            ShowState(Strings.UpdatesOnlyInstalled, false);
        else if (_updater.AvailableVersion is { } known)
            ShowState(Strings.UpdateAvailable(known), true);
        else
            ShowState("", false);

        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            ShowState(Strings.CheckingForUpdates, false);
            var result = await _updater.CheckAsync();
            check.IsEnabled = true;
            switch (result)
            {
                case UpdateCheck.Available when _updater.AvailableVersion is { } version:
                    ShowState(Strings.UpdateAvailable(version), true);
                    OfferUpdate(version);
                    break;
                case UpdateCheck.Failed:
                    ShowState(Strings.UpdateCheckFailed, false);
                    break;
                default:
                    ShowState(Strings.UpToDate, false);
                    break;
            }
        };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = check.IsEnabled = false;
            ShowState(Strings.UpdateLoading, true);
            await InstallUpdateAsync(); // restarts the app; getting here means it failed
            ShowState(Strings.UpdateFailed, true);
            install.IsEnabled = check.IsEnabled = true;
        };

        SettingsContent.Children.Add(SettingsCard("\uE895", VersionText, null, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { install, check } }, status));
    }

    // ---------------------------------------------------------------- sources

    private void ShowSourceSettings()
    {
        SettingsContent.Children.Add(new TextBlock { Text = Strings.SourcesDescription, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryText, Margin = new Thickness(0, 0, 0, 8) });
        var add = new Button { Content = IconText("\uE710", Strings.AddSource), Style = (Style)Application.Current.Resources["AccentButtonStyle"], Margin = new Thickness(0, 8, 0, 0) };
        add.Click += async (_, _) =>
        {
            await AddSourceAsync();
            ShowSettingsCategory();
        };
        if (_sources.Count == 0)
            SettingsContent.Children.Add(SettingsCard("\uE774", Strings.NoSources, null, null));

        var active = Active;
        // ToList: the buttons change _sources.
        foreach (var source in _sources.ToList())
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (source == active)
            {
                actions.Children.Add(new TextBlock
                {
                    Text = Strings.InUse,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                });
            }
            else
            {
                var use = new Button { Content = Strings.Use };
                use.Click += async (_, _) =>
                {
                    var reload = UseSourceAsync(source);
                    ShowSettingsCategory();
                    await reload;
                };
                actions.Children.Add(use);
            }

            var edit = IconButton("\uE70F", Strings.Edit);
            edit.Click += async (_, _) =>
            {
                await EditSourceAsync(source);
                ShowSettingsCategory();
            };
            actions.Children.Add(edit);

            // Removing deletes the login too, so it asks first.
            var remove = IconButton("\uE74D", Strings.Remove);
            var confirm = new Button { Content = Strings.Remove, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Spacing = 12,
                    MaxWidth = 300,
                    Children = { new TextBlock { Text = Strings.RemoveSourceQuestion(source.Name), TextWrapping = TextWrapping.Wrap }, confirm },
                },
            };
            remove.Flyout = flyout;
            confirm.Click += async (_, _) =>
            {
                flyout.Hide();
                var removal = RemoveSourceAsync(source);
                ShowSettingsCategory();
                await removal;
            };
            actions.Children.Add(remove);

            var kind = source.Kind == SourceKind.Xtream ? "Xtream Codes" : Strings.M3uPlaylist;
            SettingsContent.Children.Add(SettingsCard(source.Kind == SourceKind.Xtream ? "\uE968" : "\uE8FD", source.Name, kind, actions));
        }
        SettingsContent.Children.Add(add);
    }

    // ---------------------------------------------------------------- hidden channels

    private void ShowHiddenSettings()
    {
        SettingsContent.Children.Add(new TextBlock { Text = Strings.HiddenDescription, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryText, Margin = new Thickness(0, 0, 0, 8) });
        var hidden = _state.HiddenGroups
            .Select(key => (Key: key, Label: HiddenLabel(key)))
            .OrderBy(h => h.Label.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(h => h.Label.Group, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (hidden.Count == 0)
        {
            SettingsContent.Children.Add(SettingsCard("\uE7B3", Strings.NoHiddenGroups, null, null));
            return;
        }

        if (hidden.Count > 1)
        {
            var all = new Button { Content = Strings.UnhideAll, HorizontalAlignment = HorizontalAlignment.Right };
            all.Click += (_, _) => Unhide(hidden.Select(h => h.Key));
            SettingsContent.Children.Add(all);
        }
        foreach (var (key, label) in hidden)
        {
            var show = new Button { Content = IconText("\uE7B3", Strings.Unhide) };
            show.Click += (_, _) => Unhide([key]);
            SettingsContent.Children.Add(SettingsCard("\uED1A", label.Group, label.Section, show));
        }
    }

    private void Unhide(IEnumerable<string> keys)
    {
        foreach (var key in keys.ToList())
            _state.HiddenGroups.Remove(key);
        _state.Save();
        RefreshGroups();
        ShowSettingsCategory();
    }

    // ---------------------------------------------------------------- about

    private void ShowAbout()
    {
        var logo = new Image
        {
            Width = 56,
            Height = 56,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"))),
        };
        var title = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "2k-Player", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] },
                new TextBlock { Text = VersionText, IsTextSelectionEnabled = true },
                new TextBlock { Text = Strings.AppDescription, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryText },
            },
        };
        Grid.SetColumn(text, 1);
        title.Children.Add(logo);
        title.Children.Add(text);
        SettingsContent.Children.Add(Card(title));

        var check = new Button { Content = Strings.CheckForUpdates };
        check.Click += (_, _) => OpenSettings(SettingsCategory.General);
        SettingsContent.Children.Add(SettingsCard("\uE895", Strings.Updates, _updater.IsInstalled ? null : Strings.UpdatesOnlyInstalled, check));

        var open = new Button { Content = Strings.Open };
        open.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", AppPaths.Root);
        SettingsContent.Children.Add(SettingsCard("\uE8B7", Strings.DataFolder, Strings.DataFolderDescription, open));

        SettingsContent.Children.Add(new TextBlock { Text = Strings.License, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryText, Margin = new Thickness(4, 16, 0, 0) });
        SettingsContent.Children.Add(new HyperlinkButton { Content = Strings.SourceCode, NavigateUri = new Uri(SourceCodeUrl) });
        SettingsContent.Children.Add(new HyperlinkButton { Content = Strings.AllReleases, NavigateUri = new Uri(ReleasesUrl) });
    }

    // ---------------------------------------------------------------- building blocks

    private string VersionText => _updater.IsInstalled ? Strings.VersionLabel(_updater.CurrentVersion) : Strings.SelfBuilt(_updater.CurrentVersion);

    private static Brush SecondaryText => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static TextBlock SettingsHeader(string text)
        => new() { Text = text, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 16, 0, 4) };

    private static Border Card(UIElement content) => new()
    {
        CornerRadius = new CornerRadius(8),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        BorderThickness = new Thickness(1),
        Child = content,
    };

    /// <summary>One row: icon, title with an optional description, and a control on the right.</summary>
    /// <param name="status">A text that changes later, e.g. the result of an update check, shown under the title.</param>
    private static Border SettingsCard(string glyph, string title, string? description, UIElement? action, TextBlock? status = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (description is not null)
            text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Foreground = SecondaryText });
        if (status is not null)
            text.Children.Add(status);
        Grid.SetColumn(text, 1);
        var row = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 16, 12),
            MinHeight = 68,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { icon, text },
        };
        if (action is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 2);
            row.Children.Add(element);
        }
        return Card(row);
    }

    private static StackPanel IconText(string glyph, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
    };

    private static Button IconButton(string glyph, string tip)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Padding = new Thickness(10, 8, 10, 8) };
        ToolTipService.SetToolTip(button, tip);
        return button;
    }
}
