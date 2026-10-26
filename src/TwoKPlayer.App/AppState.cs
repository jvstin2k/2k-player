using System.Text.Json;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.App;

internal static class AppPaths
{
    public static readonly string Root = CreateRoot();

    public static readonly string Cache = Directory.CreateDirectory(Path.Combine(Root, "cache")).FullName;

    // Not "2k-Player": that is Velopack's install folder, which an uninstall deletes.
    private static string CreateRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "2kPlayer");
        // Data from versions before the rename, when the app was called UHF Player.
        var old = Path.Combine(local, "UhfPlayer");
        if (!Directory.Exists(root) && Directory.Exists(old))
        {
            try
            {
                Directory.Move(old, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return old; // in use, e.g. by a second instance; move on the next start
            }
        }
        return Directory.CreateDirectory(root).FullName;
    }
}

/// <summary>Appends crashes to crash.log, so they can be reported from a machine nobody here can debug.</summary>
internal static class CrashLog
{
    public static void Write(object exception)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.Root, "crash.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Writes a file so it holds either the old or the new content, never an empty or half-written one.</summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Per-user choices that outlive a session: favorites, hidden groups, resume positions, watched titles, volume, the source in use and the language.</summary>
internal sealed class AppState
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "state.json");

    public HashSet<string> Favorites { get; set; } = [];

    /// <summary>"Section · Group" keys, see MainWindow.HiddenKey.</summary>
    public HashSet<string> HiddenGroups { get; set; } = [];

    /// <summary>Seconds into a movie or episode, keyed by channel id.</summary>
    public Dictionary<string, double> Resume { get; set; } = [];

    /// <summary>Movies and episodes watched to the end, by channel id.</summary>
    public HashSet<string> Watched { get; set; } = [];

    /// <summary>0 to 100, as mpv's volume property.</summary>
    public double Volume { get; set; } = 100;

    /// <summary>The source last chosen, preselected when the app asks which one to use.</summary>
    public Guid? ActiveSource { get; set; }

    /// <summary>"en" or "de", see Strings.</summary>
    public string Language { get; set; } = Strings.English;

    public static AppState Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    public void Save() => AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(this));
}

/// <summary>Last known channel lists and XMLTV files per source, so the app is usable before the provider answers.</summary>
internal static class ChannelCache
{
    public static string EpgPath(Guid sourceId) => Path.Combine(AppPaths.Cache, $"{sourceId:N}.xmltv");

    private static string ChannelsPath(Guid sourceId) => Path.Combine(AppPaths.Cache, $"{sourceId:N}.json");

    public static PlaylistResult? Load(Guid sourceId)
    {
        try
        {
            using var stream = File.OpenRead(ChannelsPath(sourceId));
            return JsonSerializer.Deserialize<PlaylistResult>(stream);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public static void Save(Guid sourceId, PlaylistResult playlist)
    {
        var path = ChannelsPath(sourceId);
        using (var stream = File.Create(path + ".tmp"))
            JsonSerializer.Serialize(stream, playlist);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static void Delete(Guid sourceId)
    {
        File.Delete(ChannelsPath(sourceId));
        File.Delete(EpgPath(sourceId));
        File.Delete(EpgPath(sourceId) + ".url");
    }
}
