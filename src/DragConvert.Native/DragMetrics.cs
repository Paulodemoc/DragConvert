using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DragConvert.Native;

/// <summary>System drag thresholds: how far the mouse must move with the button down before a drag starts.</summary>
public static class DragMetrics
{
    public static int ThresholdX => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG);

    public static int ThresholdY => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG);
}
