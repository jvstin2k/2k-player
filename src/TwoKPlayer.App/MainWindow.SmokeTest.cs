using System.Runtime.InteropServices;
using TwoKPlayer.Core.Models;

namespace TwoKPlayer.App;

/// <summary>
/// A test the release build runs on the Windows runner (TWOKPLAYER_SMOKE_TEST=1): it plays a generated test picture,
/// enters and leaves fullscreen and checks the cursor over the video, the two things that only broke in installed
/// builds. Results go to smoke.log in the data folder; the app exits with 0 when everything passed.
/// </summary>
public sealed partial class MainWindow
{
    private static bool IsSmokeTest => Environment.GetEnvironmentVariable("TWOKPLAYER_SMOKE_TEST") == "1";

    private async Task SmokeTestAsync()
    {
        var log = Path.Combine(AppPaths.Root, "smoke.log");
        File.WriteAllText(log, "");
        void Log(string line) => File.AppendAllText(log, line + Environment.NewLine);
        var failed = false;
        try
        {
            if (_player is null)
                throw new InvalidOperationException(_playerError ?? "no player");
            SectionList.SelectedIndex = (int)Section.Live;
            Play(new Channel { Id = "smoke", Name = "Test", StreamUrl = "av://lavfi:testsrc=size=640x360:rate=25", Kind = MediaKind.Live });
            await Task.Delay(3000);
            failed |= !CheckCursor(Log);

            for (var i = 1; i <= 2; i++)
            {
                SetMode(ViewMode.FullScreen);
                Log($"fullscreen {i}: {_mode}");
                await Task.Delay(2000);
                SetMode(ViewMode.Normal);
                Log($"normal {i}: {_mode}");
                await Task.Delay(2000);
            }
            failed |= !CheckCursor(Log);
        }
        catch (Exception ex)
        {
            Log($"FAILED: {ex}");
            failed = true;
        }
        Log(failed ? "FAILED" : "PASSED");
        Environment.Exit(failed ? 1 : 0);
    }

    private bool CheckCursor(Action<string> log)
    {
        var scale = VideoHost.XamlRoot.RasterizationScale;
        var center = VideoHost.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(VideoHost.ActualWidth / 2, VideoHost.ActualHeight / 2));
        var point = new CursorPoint { X = (int)(center.X * scale), Y = (int)(center.Y * scale) };
        ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(this), ref point);
        SetCursorPos(point.X, point.Y);
        Thread.Sleep(300);
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        GetCursorInfo(ref info);
        var busy = info.Cursor == LoadCursorW(0, IDC_APPSTARTING) || info.Cursor == LoadCursorW(0, IDC_WAIT);
        log($"cursor over video at {point.X},{point.Y}: 0x{info.Cursor:x} (arrow 0x{LoadCursorW(0, IDC_ARROW):x}, busy: {busy})");
        return !busy;
    }

    private const int IDC_ARROW = 32512, IDC_WAIT = 32514, IDC_APPSTARTING = 32650;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public nint Cursor;
        public CursorPoint Position;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CursorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref CursorPoint point);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);
}
