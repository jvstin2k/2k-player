using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace TwoKPlayer.App;

// Replaces the XAML-generated Main (DISABLE_XAML_GENERATED_MAIN in the csproj): Velopack has to run first, because
// the installer and updater start the exe with their own arguments and expect it to exit right away.
public static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
