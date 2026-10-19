using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DragConvert.Native.Windowing;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Win32 window operations the overlay needs. Takes raw HWNDs so callers need no CsWin32 types.</summary>
public static class OverlayWindowInterop
{
    /// <summary>No activation on show or click, and no taskbar or Alt+Tab entry.</summary>
    public static void ApplyNoActivateToolWindow(nint hwnd)
    {
        var handle = new HWND(hwnd);
        int exStyle = PInvoke.GetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (int)(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
        PInvoke.SetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
    }

    public static (int X, int Y) GetCursorPosition()
    {
        PInvoke.GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static ScreenRect GetWindowRect(nint hwnd)
    {
        PInvoke.GetWindowRect(new HWND(hwnd), out RECT rect);
        return new ScreenRect(rect.left, rect.top, rect.right, rect.bottom);
    }

    /// <summary>Work area (screen minus taskbar) of the monitor nearest to a point.</summary>
    public static ScreenRect GetWorkArea(int x, int y)
    {
        HMONITOR monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(monitor, ref info);
        RECT work = info.rcWork;
        return new ScreenRect(work.left, work.top, work.right, work.bottom);
    }

    /// <summary>Centres the window on a screen point, clamped to that monitor's work area. Keeps it topmost, never activates.</summary>
    public static ScreenRect CenterOnPoint(nint hwnd, int x, int y)
    {
        ScreenRect current = GetWindowRect(hwnd);
        ScreenRect work = GetWorkArea(x, y);
        int left = Math.Clamp(x - (current.Width / 2), work.Left, Math.Max(work.Left, work.Right - current.Width));
        int top = Math.Clamp(y - (current.Height / 2), work.Top, Math.Max(work.Top, work.Bottom - current.Height));
        MoveTo(hwnd, left, top);
        return new ScreenRect(left, top, left + current.Width, top + current.Height);
    }

    /// <summary>Moves without clamping, e.g. off-screen for a warm-up render.</summary>
    public static void MoveTo(nint hwnd, int left, int top) =>
        PInvoke.SetWindowPos(
            new HWND(hwnd),
            new HWND(-1), // HWND_TOPMOST
            left,
            top,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>True when the shell's drag-image window is showing, i.e. an OLE drag with a drag image is in progress.</summary>
    public static bool IsDragImageVisible()
    {
        HWND window = PInvoke.FindWindow("SysDragImage", null);
        return !window.IsNull && PInvoke.IsWindowVisible(window);
    }

    /// <summary>
    /// Applies Windows 11 rounded corners, dark mode and a transient (acrylic) system backdrop.
    /// Returns false where the backdrop is unsupported (Windows 10, older Windows 11 builds).
    /// </summary>
    public static unsafe bool TryApplySystemBackdrop(nint hwnd, bool dark)
    {
        var handle = new HWND(hwnd);

        var corner = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        BOOL useDark = dark;
        PInvoke.DwmSetWindowAttribute(handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &useDark, (uint)sizeof(BOOL));

        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        PInvoke.DwmExtendFrameIntoClientArea(handle, in margins);

        var backdrop = DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;
        HRESULT result = PInvoke.DwmSetWindowAttribute(handle, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, &backdrop, sizeof(DWM_SYSTEMBACKDROP_TYPE));
        return result.Succeeded;
    }

    public static bool AllocateConsole() => PInvoke.AllocConsole();
}
