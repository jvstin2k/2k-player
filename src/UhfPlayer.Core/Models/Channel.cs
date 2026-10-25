namespace UhfPlayer.Core.Models;

public enum MediaKind
{
    Live,
    Movie,
    Series,
    Episode,
}

/// <summary>A live channel, movie, series or episode, independent of the source it came from.</summary>
public sealed record Channel
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Empty for a series; its episodes are loaded on demand.</summary>
    public required string StreamUrl { get; init; }
    public MediaKind Kind { get; init; }

    /// <summary>Xtream series_id, used to load the episodes of a series.</summary>
    public string? SeriesId { get; init; }
    public Guid SourceId { get; init; }
    public string? Group { get; init; }
    public string? LogoUrl { get; init; }

    /// <summary>The XMLTV channel id used to look up programmes (tvg-id / epg_channel_id).</summary>
    public string? EpgId { get; init; }

    public string? UserAgent { get; init; }
    public string? Referrer { get; init; }
}
