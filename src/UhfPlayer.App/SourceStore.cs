using System.Text.Json;
using UhfPlayer.Core.Models;

namespace UhfPlayer.App;

/// <summary>Persists configured sources as JSON in %LOCALAPPDATA%\UhfPlayer.</summary>
internal static class SourceStore
{
    // shortcut: credentials are stored in plain text; move passwords to Windows Credential Locker before a public release.
    private static readonly string FilePath = Path.Combine(AppPaths.Root, "sources.json");

    public static List<SourceConfig> Load()
        => File.Exists(FilePath) ? JsonSerializer.Deserialize<List<SourceConfig>>(File.ReadAllText(FilePath)) ?? [] : [];

    public static void Save(List<SourceConfig> sources)
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(sources, new JsonSerializerOptions { WriteIndented = true }));
    }
}
