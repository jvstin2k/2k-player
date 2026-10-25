using Velopack;
using Velopack.Sources;

namespace UhfPlayer.App;

/// <summary>
/// Self-update from the public release repo, which the GitHub Actions workflow fills on every merge to main.
/// Only installs made with Setup.exe update themselves; dotnet run and scripts/install.ps1 builds skip it.
/// </summary>
internal sealed class Updater
{
    private const string ReleasesRepo = "https://github.com/jvstin2k/uhf-player-releases";

    private readonly UpdateManager _manager = new(new GithubSource(ReleasesRepo, null, false));
    private UpdateInfo? _pending;

    /// <summary>Downloads a newer version if there is one and returns its version, or null.</summary>
    public async Task<string?> DownloadAsync()
    {
        if (!_manager.IsInstalled)
            return null;
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
                return null;
            await _manager.DownloadUpdatesAsync(update);
            _pending = update;
            return update.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // offline or GitHub unreachable: try again next start
            return null;
        }
    }

    public void RestartNow()
    {
        if (_pending is not null)
            _manager.ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>Installs a downloaded update after the app has closed, without a window.</summary>
    public void ApplyOnExit()
    {
        if (_pending is not null)
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
    }
}
