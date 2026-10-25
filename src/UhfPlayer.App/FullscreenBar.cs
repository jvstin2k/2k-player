using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UhfPlayer.App;

/// <summary>
/// The control bar in fullscreen. The video is a native child window that covers any XAML, so the bar lives in its
/// own borderless window on top and the video never resizes when it shows or hides. It is a separate copy of the
/// controls rather than InfoPanel moved over: WinUI fails fast (c000027b) when an element moves between windows.
/// MainWindow wires the events and copies its state in with MainWindow.SyncBar.
/// </summary>
internal sealed partial class FullscreenBar
{
    public const int HeightDip = 96;

    public Window Window { get; }
    public TextBlock Channel { get; } = new() { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    public TextBlock Programme { get; } = new() { Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
    public Button Live { get; }
    public Button SeekBack { get; } = new() { Content = "−10 s", CornerRadius = new CornerRadius(16) };
    public Button SeekForward { get; } = new() { Content = "+10 s", CornerRadius = new CornerRadius(16) };
    public Button Pause { get; }
    public FontIcon PauseIcon { get; } = Icon("");
    public Button Mute { get; }
    public FontIcon MuteIcon { get; } = Icon("");
    public Slider Volume { get; } = new() { Width = 110, Minimum = 0, Maximum = 100, StepFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
    public MenuFlyout Tracks { get; } = new();
    public Button Exit { get; }

    public FullscreenBar(Window owner)
    {
        Live = new Button
        {
            Content = "● LIVE",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE5, 0x48, 0x4D)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
        };
        Pause = Round(PauseIcon, 44);
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent))
            Pause.Style = (Style)accent;
        Mute = Round(MuteIcon, 40);
        var tracks = Round(Icon(""), 40);
        tracks.Flyout = Tracks;
        Exit = Round(Icon(""), 40);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var element in new FrameworkElement[] { Live, SeekBack, Pause, SeekForward, Mute, Volume, tracks, Exit })
            buttons.Children.Add(element);
        Grid.SetColumn(buttons, 1);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(Channel);
        text.Children.Add(Programme);

        var content = new Grid { Padding = new Thickness(32, 0, 32, 8), ColumnSpacing = 24 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(text);
        content.Children.Add(buttons);

        Window = new Window { Content = content, SystemBackdrop = new DesktopAcrylicBackdrop() };
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = presenter.IsMinimizable = false;
        Window.AppWindow.SetPresenter(presenter);
        Window.AppWindow.IsShownInSwitchers = false;
        // Owned by the main window: always above it, never in the taskbar.
        SetWindowLongPtrW(WinRT.Interop.WindowNative.GetWindowHandle(Window), GWLP_HWNDPARENT, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        owner.Closed += (_, _) => Window.Close();
    }

    private static FontIcon Icon(string glyph) => new() { Glyph = glyph, FontSize = 16 };

    private static Button Round(FontIcon icon, double size) => new()
    {
        Content = icon,
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Padding = new Thickness(0),
    };

    private const int GWLP_HWNDPARENT = -8;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}
