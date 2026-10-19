using System.IO;

namespace DragConvert.App;

internal static class AppPaths
{
    public const string AppUserModelId = "DragConvert.App";

    public static string DataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragConvert");

    public static string LogFolder { get; } = Path.Combine(DataFolder, "logs");

    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    public static string ExecutablePath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DragConvert.exe");

    public static string SendToShortcut => Path.Combine(Native.Shell.ShellIntegration.SendToFolder, "DragConvert.lnk");

    /// <summary>Per-user, per-session names for the single-instance mutex and the hand-off pipe.</summary>
    public static string InstanceName { get; } = $"DragConvert-{Environment.UserName}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
}
