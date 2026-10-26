using TwoKPlayer.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace TwoKPlayer.App;

/// <summary>
/// Self-update from the public release repo, which the GitHub Actions workflow fills on every merge to main.
/// Nothing installs without a click: CheckAsync only looks, InstallAsync downloads and restarts.
/// Only installs made with Setup.exe update themselves; dotnet run and scripts/install.ps1 builds skip it.
/// User data is safe across updates: it lives in %LOCALAPPDATA%\2kPlayer and the Credential Locker,
/// not in the install folder that Velopack replaces.
/// </summary>
internal sealed class Updater
{
    private const string ReleasesRepo = "https://github.com/jvstin2k/2k-player-releases";

    private readonly UpdateManager _manager = new(new GithubSource(ReleasesRepo, null, false));
    private UpdateInfo? _available;

    /// <summary>The newer version's number, or null when up to date, offline or not installed.</summary>
    public async Task<string?> CheckAsync()
    {
        if (!_manager.IsInstalled)
            return null;
        try
        {
            _available = await _manager.CheckForUpdatesAsync();
            return _available?.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // offline or GitHub unreachable: try again next start
            return null;
        }
    }

    /// <summary>
    /// What changed since the installed version, newest first. Skipped versions are included when the feed
    /// lists them as deltas; otherwise only the newest version's changes are known.
    /// </summary>
    public IReadOnlyList<string> Changes()
    {
        if (_available is null)
            return [];
        return new[] { _available.TargetFullRelease }
            .Concat(Enumerable.Reverse(_available.DeltasToTarget ?? []))
            .SelectMany(asset => ReleaseNotes.Changes(asset.NotesMarkdown))
            .Distinct()
            .ToList();
    }

    /// <summary>Downloads the update found by CheckAsync and restarts into it; false if that failed.</summary>
    public async Task<bool> InstallAsync()
    {
        if (_available is null)
            return false;
        try
        {
            await _manager.DownloadUpdatesAsync(_available);
            _manager.ApplyUpdatesAndRestart(_available);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return false;
        }
    }
}
