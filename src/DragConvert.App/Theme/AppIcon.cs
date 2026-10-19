using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DragConvert.Native.Windowing;

namespace DragConvert.App.Theme;

/// <summary>
/// The app and tray icons. Both are multi-size .ico files in Assets/, generated from the source artwork in
/// assets/icon-source/ (Lanczos downscale, light sharpening at 32 px and below).
/// </summary>
internal static class AppIcon
{
    private static readonly Uri AppIconUri = new("pack://application:,,,/Assets/DragConvert.ico");
    private static readonly Uri TrayIconUri = new("pack://application:,,,/Assets/Tray.ico");

    /// <summary>The notification-area icon at the size Windows uses for small icons at the current scaling.</summary>
    public static System.Drawing.Icon Tray()
    {
        int size = OverlayWindowInterop.SmallIconSize;
        using Stream stream = Application.GetResourceStream(TrayIconUri).Stream;
        return new System.Drawing.Icon(stream, new System.Drawing.Size(size, size));
    }

    /// <summary>The app icon for window title bars and the taskbar.</summary>
    public static ImageSource Window()
    {
        var decoder = BitmapDecoder.Create(AppIconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
    }

    /// <summary>One size of an icon resource, for the snapshot previews.</summary>
    public static BitmapSource Frame(bool tray, int pixels)
    {
        var decoder = BitmapDecoder.Create(tray ? TrayIconUri : AppIconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - pixels)).First();
    }

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}
