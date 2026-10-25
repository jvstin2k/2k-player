using System.Text.Json;
using UhfPlayer.Core.Models;

namespace UhfPlayer.App;

internal static class AppPaths
{
    public static readonly string Root = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UhfPlayer")).FullName;

    public static readonly string Cache = Directory.CreateDirectory(Path.Combine(Root, "cache")).FullName;
}

/// <summary>Per-user choices that outlive a session: favorites, hidden groups and resume positions.</summary>
internal sealed class AppState
{
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "state.json");

    public HashSet<string> Favorites { get; set; } = [];

    /// <summary>"Section · Group" keys, see MainWindow.HiddenKey.</summary>
    public HashSet<string> HiddenGroups { get; set; } = [];

    /// <summary>Seconds into a movie or episode, keyed by channel id.</summary>
    public Dictionary<string, double> Resume { get; set; } = [];

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

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
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
        using var stream = File.Create(ChannelsPath(sourceId));
        JsonSerializer.Serialize(stream, playlist);
    }

    public static void Delete(Guid sourceId)
    {
        File.Delete(ChannelsPath(sourceId));
        File.Delete(EpgPath(sourceId));
    }
}
