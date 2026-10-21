namespace UhfPlayer.Core.Models;

/// <summary>A playable live channel, independent of the source it came from.</summary>
public sealed record Channel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
    public Guid SourceId { get; init; }
    public string? Group { get; init; }
    public string? LogoUrl { get; init; }

    /// <summary>The XMLTV channel id used to look up programmes (tvg-id / epg_channel_id).</summary>
    public string? EpgId { get; init; }

    public string? UserAgent { get; init; }
    public string? Referrer { get; init; }
}
