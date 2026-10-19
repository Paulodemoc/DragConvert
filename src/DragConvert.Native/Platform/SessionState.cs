using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DragConvert.Native.Platform;

/// <summary>What the user's session is doing right now, for deciding when the drag overlay should stay out of the way.</summary>
public static class SessionState
{
    /// <summary>True in a Remote Desktop session.</summary>
    public static bool IsRemoteSession => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_REMOTESESSION) != 0;

    /// <summary>
    /// True while a fullscreen app (game, video, presentation) is in front: the same signal Windows uses to hold back
    /// notifications.
    /// </summary>
    public static bool IsFullscreenAppActive
    {
        get
        {
            if (PInvoke.SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state).Failed)
            {
                return false;
            }

            return state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
        }
    }

    /// <summary>Environment.TickCount64-compatible time of the last keyboard or mouse input anywhere in the session.</summary>
    public static long LastInputTick
    {
        get
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!PInvoke.GetLastInputInfo(ref info))
            {
                return 0;
            }

            // dwTime is a 32-bit tick count; rebuild the 64-bit value relative to now.
            long now = Environment.TickCount64;
            uint elapsed = unchecked((uint)now - info.dwTime);
            return now - elapsed;
        }
    }

    /// <summary>Effective DPI scale (1.0 = 100%) of the monitor nearest a screen point.</summary>
    public static double DpiScaleAt(int x, int y)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8, 1))
        {
            return 1.0;
        }

        HMONITOR monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded ? dpiX / 96.0 : 1.0;
    }

    /// <summary>
    /// Hands this process's unused memory pages back to Windows. Purely an idle-time courtesy: pages come back on use.
    /// </summary>
    public static void TrimWorkingSet()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: true);
        PInvoke.EmptyWorkingSet(PInvoke.GetCurrentProcess());
    }
}
