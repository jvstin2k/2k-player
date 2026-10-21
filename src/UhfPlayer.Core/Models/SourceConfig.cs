namespace UhfPlayer.Core.Models;

public enum SourceKind
{
    M3u,
    Xtream,
}

/// <summary>A user-configured playlist source, persisted in the settings file.</summary>
public sealed record SourceConfig
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public SourceKind Kind { get; init; }

    /// <summary>M3U: http(s) URL or local file path.</summary>
    public string? M3uLocation { get; init; }

    /// <summary>Xtream: server base URL, e.g. http://example.com:8080.</summary>
    public string? Server { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    /// <summary>Optional XMLTV URL; overrides whatever the source advertises.</summary>
    public string? EpgUrl { get; init; }
}
