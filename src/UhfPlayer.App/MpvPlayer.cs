using System.Runtime.InteropServices;

namespace UhfPlayer.App;

/// <summary>
/// libmpv rendering into a Win32 child window that sits on top of the XAML content.
/// shortcut: a child HWND covers any XAML drawn over it (airspace), so controls live beside the video,
/// not on it; switch to a transparent overlay window when UHF-style on-video controls are needed.
/// </summary>
internal sealed partial class MpvPlayer : IDisposable
{
    private const string Mpv = "libmpv-2.dll";
    private readonly nint _ctx;
    private readonly nint _hwnd;
    private readonly Thread _events;
    private volatile bool _disposed;

    /// <summary>Raised on the mpv event thread when a stream cannot be opened or breaks off.</summary>
    public event Action<string>? PlaybackFailed;

    public MpvPlayer(nint parentHwnd)
    {
        _hwnd = CreateWindowExW(0, "Static", null, WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 0, 0, parentHwnd, 0, 0, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");

        _ctx = mpv_create();
        if (_ctx == 0)
            throw new InvalidOperationException("mpv_create failed.");

        Option("wid", _hwnd.ToString());
        Option("vo", "gpu-next");
        Option("hwdec", "auto-safe");
        Option("osc", "no");
        Option("input-default-bindings", "no");
        Option("input-vo-keyboard", "no");
        Option("ytdl", "no");
        Option("network-timeout", "10");
        // Faster channel switching: probe less before the first frame and do not wait to fill the cache.
        Option("demuxer-lavf-analyzeduration", "1");
        Option("cache-pause-initial", "no");
        Option("demuxer-max-bytes", "64MiB");
        var logDir = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UhfPlayer"));
        Option("log-file", Path.Combine(logDir.FullName, "mpv.log"));
        Check(mpv_initialize(_ctx));

        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
        _events.Start();
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
                    PlaybackFailed?.Invoke(Marshal.PtrToStringUTF8(mpv_error_string(end->Error)) ?? "unknown error");
            }
        }
    }

    public void Play(string url, string? userAgent, string? referrer)
    {
        Check(mpv_set_property_string(_ctx, "user-agent", userAgent ?? Core.SourceLoader.DefaultUserAgent));
        Check(mpv_set_property_string(_ctx, "referrer", referrer ?? ""));
        Command("loadfile", url, "replace");
        ShowWindow(_hwnd, SW_SHOW);
    }

    public void TogglePause() => Command("cycle", "pause");

    public void ToggleMute() => Command("cycle", "mute");

    /// <summary>Places the video window over the given client-area rectangle, in physical pixels.</summary>
    public void SetBounds(int x, int y, int width, int height)
        => SetWindowPos(_hwnd, HWND_TOP, x, y, width, height, SWP_NOACTIVATE);

    public void Dispose()
    {
        _disposed = true;
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

    private const int MPV_EVENT_SHUTDOWN = 1, MPV_EVENT_END_FILE = 7, MPV_END_FILE_REASON_ERROR = 4;

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
    private const int SW_SHOW = 5;
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
}
