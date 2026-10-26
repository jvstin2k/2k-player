using TwoKPlayer.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace TwoKPlayer.App;

internal enum UpdateCheck
{
    NotInstalled,
    UpToDate,
    Available,
    Failed,
}

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

    /// <summary>Whether this copy was installed with Setup.exe; only those update themselves.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The running version, as the release it came from or, for self-built copies, the build's own number.</summary>
    public string CurrentVersion
    {
        get
        {
            if (_manager.IsInstalled && _manager.CurrentVersion is { } installed)
                return installed.ToString();
            var build = typeof(Updater).Assembly.GetName().Version;
            return build is null ? "?" : $"{build.Major}.{build.Minor}.{build.Build}";
        }
    }

    /// <summary>The newer version found by the last CheckAsync, or null.</summary>
    public string? AvailableVersion => _available?.TargetFullRelease.Version.ToString();

    /// <summary>Looks for a newer release. Run at every start and from the settings.</summary>
    public async Task<UpdateCheck> CheckAsync()
    {
        if (!_manager.IsInstalled)
            return UpdateCheck.NotInstalled;
        try
        {
            _available = await _manager.CheckForUpdatesAsync();
            return _available is null ? UpdateCheck.UpToDate : UpdateCheck.Available;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex); // offline or GitHub unreachable: try again next start
            return UpdateCheck.Failed;
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
