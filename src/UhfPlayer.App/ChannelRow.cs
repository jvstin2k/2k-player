using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using UhfPlayer.Core.Epg;
using UhfPlayer.Core.Models;

namespace UhfPlayer.App;

/// <summary>One line in the channel list: channel plus what is on right now.</summary>
public sealed class ChannelRow : INotifyPropertyChanged
{
    private ImageSource? _logo;
    private bool _isFavorite;

    public ChannelRow(Channel channel, EpgGuide guide, DateTimeOffset now, bool isFavorite)
    {
        Channel = channel;
        Now = guide.NowNext(channel, now).Now;
        Progress = Now?.Progress(now) ?? 0;
        _isFavorite = isFavorite;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Channel Channel { get; }
    public EpgProgramme? Now { get; }

    // Created on first access, i.e. only for rows the list actually shows; VOD lists run into tens of thousands.
    public ImageSource? Logo => _logo ??= Images.FromUrl(Channel.LogoUrl);

    public double Progress { get; }
    public Visibility ProgressVisibility => Now is null ? Visibility.Collapsed : Visibility.Visible;

    public string Subtitle => Now?.Title ?? Channel.Kind switch
    {
        MediaKind.Movie => "Film",
        MediaKind.Series => "Serie",
        MediaKind.Episode => Channel.Group ?? "Folge",
        _ => "",
    };

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarGlyph)));
        }
    }

    public string StarGlyph => _isFavorite ? "" : "";
}

/// <summary>A channel's line in the TV guide, with its programmes laid out on the time axis.</summary>
public sealed class GuideRow(Channel channel, EpgGuide guide, DateTimeOffset from, DateTimeOffset to, double pxPerMinute, DateTimeOffset now)
{
    private ImageSource? _logo;
    private IReadOnlyList<GuideBlock>? _blocks;

    public Channel Channel => channel;
    public ImageSource? Logo => _logo ??= Images.FromUrl(channel.LogoUrl);

    public IReadOnlyList<GuideBlock> Blocks => _blocks ??= guide.For(channel)
        .Where(p => p.Stop > from && p.Start < to)
        .Select(p =>
        {
            var start = p.Start < from ? from : p.Start;
            var stop = p.Stop > to ? to : p.Stop;
            return new GuideBlock(channel, p, (start - from).TotalMinutes * pxPerMinute, (stop - start).TotalMinutes * pxPerMinute, p.IsAiring(now));
        })
        .ToList();
}

public sealed record GuideBlock(Channel Channel, EpgProgramme Programme, double Left, double Width, bool IsNow)
{
    public Thickness Margin => new(Left, 3, 0, 3);
    public double BlockWidth => Math.Max(0, Width - 4); // leaves a gap to the next programme
    public string Title => Programme.Title;
    public string Time => $"{Programme.Start.ToLocalTime():HH:mm} – {Programme.Stop.ToLocalTime():HH:mm}";
    public Visibility NowVisibility => IsNow ? Visibility.Visible : Visibility.Collapsed;
}

internal static class Images
{
    public static ImageSource? FromUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? new BitmapImage(uri) { DecodePixelWidth = 96 } : null;
}
