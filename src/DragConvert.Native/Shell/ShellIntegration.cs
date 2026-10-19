using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace DragConvert.Native.Shell;

/// <summary>Per-user Windows integration for the unpackaged build: app identity, Send To shortcut, autostart.</summary>
public static class ShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Gives the process an AppUserModelID and registers its display name, so Windows shows notifications (with
    /// buttons) under "DragConvert". MSIX builds get this from the package instead.
    /// </summary>
    public static void RegisterAppIdentity(string appUserModelId, string displayName, string? iconPath)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appUserModelId}"))
        {
            key.SetValue("DisplayName", displayName);
            if (iconPath is not null)
            {
                key.SetValue("IconUri", iconPath);
            }
        }

        PInvoke.SetCurrentProcessExplicitAppUserModelID(appUserModelId).ThrowOnFailure();
    }

    public static string SendToFolder => Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

    /// <summary>Creates or replaces a .lnk file.</summary>
    public static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string description, string? iconPath = null)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetPath);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
            if (iconPath is not null)
            {
                link.SetIconLocation(iconPath, 0);
            }

            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public static void SetStartWithWindows(string valueName, string? executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (executablePath is null)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(valueName, $"\"{executablePath}\"");
        }
    }
}
