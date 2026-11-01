using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;

namespace TwoKPlayer.App;

/// <summary>
/// The timeline under the video for movies and episodes: position, length, and a slider to jump around. Live
/// streams have none, they only have "now". There are two copies of the slider, InfoPanel's and the fullscreen
/// bar's; both use the handlers here and are kept in step by <see cref="ShowTimeline"/>.
/// </summary>
public sealed partial class MainWindow
{
    // mpv tells nothing when the position moves, so it is polled while a movie or episode plays.
    private readonly DispatcherTimer _timelineTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>Set while the code moves the sliders, so that is not taken as the user seeking.</summary>
    private bool _updatingTimeline;

    /// <summary>Set while the user drags a slider: the position is shown but only sought on release.</summary>
    private bool _scrubbing;

    private double _lastSeek = double.NaN;

    private void InitTimeline()
    {
        _timelineTimer.Tick += (_, _) => UpdateTimeline();
        WireTimeline(TimelineSlider);
    }

    private void WireTimeline(Slider slider)
    {
        slider.ThumbToolTipValueConverter = new TimeConverter();
        slider.ValueChanged += OnTimelineValueChanged;
        // The slider handles the pointer itself, so these only arrive with handledEventsToo.
        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _scrubbing = true), true);
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, _) => EndScrub((Slider)s)), true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((s, _) => EndScrub((Slider)s)), true);
    }

    /// <summary>Called by Play: a live channel hides the timeline, a movie or episode starts it at its resume point.</summary>
    private void ResetTimeline(bool live, double start)
    {
        _scrubbing = false;
        _lastSeek = double.NaN;
        if (live)
        {
            _timelineTimer.Stop();
            TimelinePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Visible right away, so the panel does not jump when the length comes in a moment later.
            TimelinePanel.Visibility = Visibility.Visible;
            ShowTimeline(start, null);
            _timelineTimer.Start();
        }
        SyncBar();
    }

    private void UpdateTimeline()
    {
        if (_player is null || !IsVod)
        {
            _timelineTimer.Stop();
            return;
        }
        if (_scrubbing)
            return;
        var position = _player.GetDouble("time-pos");
        if (position is null)
            return; // between files or while opening; keep what is shown
        ShowTimeline(position.Value, _player.GetDouble("duration"));
    }

    /// <param name="duration">Null or 0 while unknown; some providers' streams never tell, then the slider stays off.</param>
    private void ShowTimeline(double position, double? duration)
    {
        var known = duration > 0;
        var max = known ? duration!.Value : 1;
        position = Math.Clamp(position, 0, max);
        _updatingTimeline = true;
        try
        {
            foreach (var slider in TimelineSliders())
            {
                slider.IsEnabled = known;
                slider.Maximum = max;
                slider.Value = known ? position : 0;
            }
        }
        finally
        {
            _updatingTimeline = false;
        }
        SetTimeTexts(position, known ? FormatTime(max) : "--:--");
    }

    private void SetTimeTexts(double position, string? duration = null)
    {
        ElapsedText.Text = FormatTime(position);
        if (duration is not null)
            DurationText.Text = duration;
        if (_bar is not null)
        {
            _bar.Elapsed.Text = ElapsedText.Text;
            _bar.Duration.Text = DurationText.Text;
        }
    }

    private IEnumerable<Slider> TimelineSliders()
    {
        yield return TimelineSlider;
        if (_bar is not null)
            yield return _bar.Position;
    }

    private void OnTimelineValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingTimeline)
            return;
        // Mirror the other copy, so InfoPanel and the fullscreen bar agree when the mode changes.
        _updatingTimeline = true;
        try
        {
            foreach (var slider in TimelineSliders())
            {
                if (slider != sender)
                    slider.Value = e.NewValue;
            }
        }
        finally
        {
            _updatingTimeline = false;
        }
        SetTimeTexts(e.NewValue);
        // Dragging would seek the stream dozens of times; that waits for the release. Keys and clicks seek now.
        if (!_scrubbing)
            SeekTo(e.NewValue);
    }

    private void EndScrub(Slider slider)
    {
        if (!_scrubbing)
            return;
        _scrubbing = false;
        SeekTo(slider.Value);
    }

    private void SeekTo(double seconds)
    {
        // A click on the track arrives both as a value change and as a release.
        if (seconds == _lastSeek || !IsVod)
            return;
        _lastSeek = seconds;
        try
        {
            _player?.SeekTo(seconds);
        }
        catch (InvalidOperationException)
        {
            // Not seekable yet (still opening); the next tick moves the slider back to where playback is.
        }
    }

    private static string FormatTime(double seconds) => TimeConverter.Format(seconds);
}

/// <summary>Shows the time in the timeline slider's thumb tooltip while dragging, instead of a number of seconds.</summary>
internal sealed partial class TimeConverter : IValueConverter
{
    /// <summary>1:23:45 for an hour and longer, else 12:34.</summary>
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : $"{t.Minutes}:{t:ss}";
    }

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double seconds ? Format(seconds) : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
