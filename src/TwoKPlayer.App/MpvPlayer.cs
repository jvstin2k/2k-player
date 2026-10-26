using System.Globalization;
using System.Runtime.InteropServices;

namespace TwoKPlayer.App;

public sealed record MpvTrack(string Type, string Id, string Label, bool Selected);

/// <summary>
/// libmpv rendering into a Win32 child window that sits on top of the XAML content. That window covers any XAML
/// drawn over it, so in fullscreen MainWindow puts its controls into a separate overlay window.
/// </summary>
internal sealed partial class MpvPlayer : IDisposable
{
    private const string Mpv = "libmpv-2.dll";
    private readonly nint _ctx;
    private readonly nint _hwnd;
    private readonly Thread _events;
    private volatile bool _disposed;
    private uint _mouseThreadId;
    private uint _lastClickTime;
    private Point _lastClickPoint;
    private static MpvPlayer? s_hookTarget;
    private static nint s_staticProc;

    /// <summary>
    /// Raised on the mpv event thread when a file stops by itself: with an error message when it could not be opened
    /// or broke off, with null at its regular end. Not raised when another file replaces it.
    /// </summary>
    public event Action<string?>? PlaybackEnded;

    /// <summary>Raised on the mpv event thread once a file is open and about to play.</summary>
    public event Action? PlaybackStarted;

    /// <summary>
    /// Raised on the mpv event thread for "script-message app &lt;name&gt; [arg]" bindings in InputConf, and on the
    /// UI thread for mouse input over the video ("fullscreen" on double-click, "volume" with +5/-5 on the wheel).
    /// </summary>
    public event Action<string, string?>? MessageReceived;

    // Keys over the video go to mpv's window, not to XAML. These bindings handle them in mpv and forward what
    // only the app can do (fullscreen, switching channels) as client messages. Mouse input is handled by MouseHook.
    private const string InputConf = """
        SPACE cycle pause
        m script-message app mute
        LEFT seek -10
        RIGHT seek 10
        f script-message app fullscreen
        ESC script-message app exit-fullscreen
        UP script-message app channel -1
        DOWN script-message app channel 1
        p script-message app mini
        i script-binding stats/display-stats-toggle
        """;

    public MpvPlayer(nint parentHwnd)
    {
        _hwnd = CreateWindowExW(0, "Static", null, WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 0, 0, parentHwnd, 0, 0, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        // Nobody in the window chain above mpv's window sets a cursor, so it kept the busy cursor from app start.
        unsafe
        {
            s_staticProc = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&VideoWindowProc);
        }

        _ctx = mpv_create();
        if (_ctx == 0)
            throw new InvalidOperationException("mpv_create failed.");

        Option("wid", _hwnd.ToString());
        Option("vo", "gpu-next");
        Option("hwdec", "auto-safe");
        Option("osc", "no");
        Option("input-default-bindings", "no");
        Option("input-vo-keyboard", "yes");
        Option("ytdl", "no");
        Option("network-timeout", "10");
        // Faster channel switching: probe less before the first frame and do not wait to fill the cache.
        Option("demuxer-lavf-analyzeduration", "1");
        Option("cache-pause-initial", "no");
        Option("demuxer-max-bytes", "64MiB");
        // Let FFmpeg reconnect short HTTP drops itself; MainWindow reopens the stream when that is not enough.
        Option("stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5");
        Option("log-file", Path.Combine(AppPaths.Root, "mpv.log"));
        var inputConf = Path.Combine(AppPaths.Root, "input.conf");
        File.WriteAllText(inputConf, InputConf);
        Option("input-conf", inputConf);
        Check(mpv_initialize(_ctx));

        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _events.Start();

        // mpv's own window only sees the wheel while it has keyboard focus, which XAML keeps, and its double-click
        // detection did not fire for embedded windows. A low-level hook sees every click and wheel turn regardless.
        // It gets its own thread: Windows silently drops a low-level hook whose thread is busy for too long once,
        // and the UI thread is busy while big playlists load.
        s_hookTarget = this;
        using var ready = new ManualResetEventSlim();
        new Thread(() => MouseLoop(ready)) { IsBackground = true, Name = "mouse hook" }.Start();
        ready.Wait();
    }

    private unsafe void MouseLoop(ManualResetEventSlim ready)
    {
        _mouseThreadId = GetCurrentThreadId();
        var hook = SetWindowsHookExW(WH_MOUSE_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&MouseHook, GetModuleHandleW(null), 0);
        ready.Set();
        // The hook is called from inside GetMessage; WM_QUIT from Dispose ends the loop.
        while (GetMessageW(out _, 0, 0, 0) > 0)
        {
        }
        UnhookWindowsHookEx(hook);
    }

    // libmpv disables its embedded window, so the mouse goes to this one instead. As a "Static" window it answered
    // WM_NCHITTEST with HTTRANSPARENT, passing the mouse on to the main window below, which apparently sets no
    // cursor: the busy cursor from app start stayed over the video. Claiming the hit test brings WM_SETCURSOR here.
    [UnmanagedCallersOnly]
    private static nint VideoWindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WM_NCHITTEST)
            return HTCLIENT;
        if (message == WM_SETCURSOR)
        {
            SetCursor(LoadCursorW(0, IDC_ARROW));
            return 1;
        }
        return CallWindowProcW(s_staticProc, hwnd, message, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static unsafe nint MouseHook(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && s_hookTarget is { } player && player.OnMouse((int)message, (MouseHookData*)data))
                return 1;
        }
        catch (Exception ex)
        {
            // An exception escaping a native callback kills the process without any handler seeing it.
            CrashLog.Write(ex);
        }
        return CallNextHookEx(0, code, message, data);
    }

    /// <returns>true to swallow the event, so the wheel does not also scroll the focused list.</returns>
    private unsafe bool OnMouse(int message, MouseHookData* data)
    {
        if (message is not (WM_MOUSEWHEEL or WM_LBUTTONDOWN))
            return false;
        // Over the visible video, and not under anything else on top (other apps, the fullscreen bar window).
        if (!IsWindowVisible(_hwnd) || !GetWindowRect(_hwnd, out var video)
            || data->Point.X < video.Left || data->Point.X >= video.Right || data->Point.Y < video.Top || data->Point.Y >= video.Bottom
            || GetAncestor(WindowFromPoint(data->Point), GA_ROOT) != GetAncestor(_hwnd, GA_ROOT))
            return false;

        if (message == WM_MOUSEWHEEL)
        {
            MessageReceived?.Invoke("volume", (short)(data->MouseData >> 16) > 0 ? "5" : "-5");
            return true;
        }

        if (data->Time - _lastClickTime <= GetDoubleClickTime()
            && Math.Abs(data->Point.X - _lastClickPoint.X) <= GetSystemMetrics(SM_CXDOUBLECLK)
            && Math.Abs(data->Point.Y - _lastClickPoint.Y) <= GetSystemMetrics(SM_CYDOUBLECLK))
        {
            _lastClickTime = 0;
            MessageReceived?.Invoke("fullscreen", null);
        }
        else
        {
            _lastClickTime = data->Time;
            _lastClickPoint = data->Point;
        }
        return false;
    }

    // Without this loop, failures after loadfile (bad URL, 401 on the stream, unsupported codec) would go unnoticed.
    private unsafe void EventLoop()
    {
        while (!_disposed)
        {
            var ev = (MpvEvent*)mpv_wait_event(_ctx, -1);
            if (ev->EventId == MPV_EVENT_SHUTDOWN)
                return;
            if (ev->EventId == MPV_EVENT_END_FILE && ev->Data != 0)
            {
                var end = (MpvEventEndFile*)ev->Data;
                if (end->Reason == MPV_END_FILE_REASON_ERROR)
                    PlaybackEnded?.Invoke(Marshal.PtrToStringUTF8(mpv_error_string(end->Error)) ?? "unknown error");
                else if (end->Reason == MPV_END_FILE_REASON_EOF)
                    PlaybackEnded?.Invoke(null);
            }
            else if (ev->EventId == MPV_EVENT_FILE_LOADED)
            {
                PlaybackStarted?.Invoke();
            }
            else if (ev->EventId == MPV_EVENT_CLIENT_MESSAGE && ev->Data != 0)
            {
                var msg = (MpvEventClientMessage*)ev->Data;
                var args = new string?[msg->NumArgs];
                for (var i = 0; i < args.Length; i++)
                    args[i] = Marshal.PtrToStringUTF8(msg->Args[i]);
                if (args is ["app", { } name, ..])
                    MessageReceived?.Invoke(name, args.Length > 2 ? args[2] : null);
            }
        }
    }

    /// <param name="startSeconds">Resume position for movies and episodes; 0 starts from the beginning.</param>
    public void Play(string url, string? userAgent, string? referrer, double startSeconds = 0)
    {
        Check(mpv_set_property_string(_ctx, "start", startSeconds > 0 ? startSeconds.ToString(CultureInfo.InvariantCulture) : "none"));
        Check(mpv_set_property_string(_ctx, "user-agent", userAgent ?? Core.SourceLoader.DefaultUserAgent));
        Check(mpv_set_property_string(_ctx, "referrer", referrer ?? ""));
        Check(mpv_set_property_string(_ctx, "pause", "no"));
        Command("loadfile", url, "replace");
        ShowWindow(_hwnd, SW_SHOW);
    }

    public void TogglePause() => Command("cycle", "pause");

    public void Seek(int seconds) => Command("seek", seconds.ToString(), "relative");

    /// <summary>mpv's built-in stats overlay: resolution, codecs, bitrate, dropped frames.</summary>
    public void ToggleStats() => Command("script-binding", "stats/display-stats-toggle");

    public void SetVisible(bool visible) => ShowWindow(_hwnd, visible ? SW_SHOW : SW_HIDE);

    public void SetProperty(string name, string value) => Check(mpv_set_property_string(_ctx, name, value));

    public string? GetProperty(string name)
    {
        var p = mpv_get_property_string(_ctx, name);
        if (p == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(p);
        }
        finally
        {
            mpv_free(p);
        }
    }

    public double? GetDouble(string name)
        => double.TryParse(GetProperty(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    public IReadOnlyList<MpvTrack> Tracks()
    {
        var tracks = new List<MpvTrack>();
        var count = int.TryParse(GetProperty("track-list/count"), out var n) ? n : 0;
        for (var i = 0; i < count; i++)
        {
            var p = $"track-list/{i}";
            var lang = GetProperty($"{p}/lang");
            var title = GetProperty($"{p}/title");
            var label = string.Join(" · ", new[] { lang?.ToUpperInvariant(), title }.Where(s => !string.IsNullOrEmpty(s)));
            tracks.Add(new MpvTrack(
                GetProperty($"{p}/type") ?? "",
                GetProperty($"{p}/id") ?? "",
                label.Length > 0 ? label : Strings.Track(GetProperty($"{p}/id")),
                GetProperty($"{p}/selected") == "yes"));
        }
        return tracks;
    }

    /// <summary>Places the video window over the given client-area rectangle, in physical pixels.</summary>
    public void SetBounds(int x, int y, int width, int height)
        => SetWindowPos(_hwnd, HWND_TOP, x, y, width, height, SWP_NOACTIVATE);

    public void Dispose()
    {
        _disposed = true;
        s_hookTarget = null;
        PostThreadMessageW(_mouseThreadId, WM_QUIT, 0, 0);
        mpv_wakeup(_ctx);
        _events.Join();
        mpv_terminate_destroy(_ctx);
        DestroyWindow(_hwnd);
    }

    private void Option(string name, string value) => Check(mpv_set_option_string(_ctx, name, value));

    private unsafe void Command(params string[] args)
    {
        var ptrs = new nint[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            fixed (nint* p = ptrs)
                Check(mpv_command(_ctx, p));
        }
        finally
        {
            foreach (var p in ptrs)
                Marshal.FreeCoTaskMem(p);
        }
    }

    private static void Check(int error)
    {
        if (error < 0)
            throw new InvalidOperationException($"mpv: {Marshal.PtrToStringUTF8(mpv_error_string(error))}");
    }

    [LibraryImport(Mpv)] private static partial nint mpv_create();
    [LibraryImport(Mpv)] private static partial int mpv_initialize(nint ctx);
    [LibraryImport(Mpv)] private static partial void mpv_terminate_destroy(nint ctx);
    [LibraryImport(Mpv)] private static partial nint mpv_error_string(int error);
    [LibraryImport(Mpv)] private static partial nint mpv_wait_event(nint ctx, double timeout);
    [LibraryImport(Mpv)] private static partial void mpv_wakeup(nint ctx);
    [LibraryImport(Mpv)] private static partial void mpv_free(nint data);
    [LibraryImport(Mpv, StringMarshalling = StringMarshalling.Utf8)] private static partial nint mpv_get_property_string(nint ctx, string name);

    private const int MPV_EVENT_SHUTDOWN = 1, MPV_EVENT_END_FILE = 7, MPV_EVENT_FILE_LOADED = 8, MPV_EVENT_CLIENT_MESSAGE = 16,
        MPV_END_FILE_REASON_EOF = 0, MPV_END_FILE_REASON_ERROR = 4;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MpvEventClientMessage
    {
        public int NumArgs;
        public nint* Args;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MpvEventEndFile
    {
        public int Reason;
        public int Error;
    }
    [LibraryImport(Mpv)] private static unsafe partial int mpv_command(nint ctx, nint* args);
    [LibraryImport(Mpv, StringMarshalling = StringMarshalling.Utf8)] private static partial int mpv_set_option_string(nint ctx, string name, string data);
    [LibraryImport(Mpv, StringMarshalling = StringMarshalling.Utf8)] private static partial int mpv_set_property_string(nint ctx, string name, string data);

    private const uint WS_CHILD = 0x40000000, WS_CLIPSIBLINGS = 0x04000000, SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOW = 5, SW_HIDE = 0;
    private static readonly nint HWND_TOP = 0;

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    private const int GWLP_WNDPROC = -4, IDC_ARROW = 32512;
    private const uint WM_SETCURSOR = 0x0020, WM_NCHITTEST = 0x0084;
    private const nint HTCLIENT = 1;

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint CallWindowProcW(nint previous, nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    private const int WH_MOUSE_LL = 14, GA_ROOT = 2, WM_QUIT = 0x0012, WM_LBUTTONDOWN = 0x0201, WM_MOUSEWHEEL = 0x020A, SM_CXDOUBLECLK = 36, SM_CYDOUBLECLK = 37;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowsHookExW(int idHook, nint proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
