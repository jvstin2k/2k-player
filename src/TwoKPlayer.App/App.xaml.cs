using Microsoft.UI.Xaml;

namespace TwoKPlayer.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        // e.Message carries WinUI's own error text, which the exception alone often lacks.
        UnhandledException += (_, e) => CrashLog.Write($"{e.Message}{Environment.NewLine}{e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
