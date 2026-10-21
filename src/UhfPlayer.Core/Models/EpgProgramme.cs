namespace UhfPlayer.Core.Models;

public sealed record EpgProgramme(
    string ChannelId,
    DateTimeOffset Start,
    DateTimeOffset Stop,
    string Title,
    string? Description = null)
{
    public bool IsAiring(DateTimeOffset at) => Start <= at && at < Stop;

    public double Progress(DateTimeOffset at)
    {
        var total = (Stop - Start).TotalSeconds;
        return total <= 0 ? 0 : Math.Clamp((at - Start).TotalSeconds / total, 0, 1);
    }
}
