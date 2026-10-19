using System.Windows;
using System.Windows.Media;
using DragConvert.Core.Conversion;
using Microsoft.Win32;

namespace DragConvert.App.Theme;

public enum ThemeVariant
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// Resolved look for the overlay: brushes for the current theme plus the geometry and motion tokens from
/// Theme/Tokens.xaml. High contrast uses opaque system colours instead of the token palette.
/// </summary>
internal sealed class OverlayTheme
{
    private OverlayTheme(ThemeVariant variant)
    {
        Variant = variant;
        if (variant == ThemeVariant.HighContrast)
        {
            Plate = SystemColors.WindowBrush;
            PlateBorder = SystemColors.WindowTextBrush;
            Tile = SystemColors.ControlBrush;
            TileBorder = SystemColors.ControlTextBrush;
            Text = SystemColors.ControlTextBrush;
            TextSecondary = SystemColors.ControlTextBrush;
            Caption = SystemColors.WindowBrush;
            CaptionText = SystemColors.WindowTextBrush;
            Accent = SystemColors.HighlightBrush;
            AccentText = SystemColors.HighlightTextBrush;
            Success = SystemColors.HighlightBrush;
            Failure = SystemColors.HighlightBrush;
        }
        else
        {
            string p = variant == ThemeVariant.Dark ? "Dark." : "Light.";
            Plate = Brush(p + "Plate");
            PlateBorder = Brush(p + "PlateBorder");
            Tile = Brush(p + "Tile");
            TileBorder = Brush(p + "TileBorder");
            Text = Brush(p + "Text");
            TextSecondary = Brush(p + "TextSecondary");
            Caption = Brush(p + "Caption");
            CaptionText = Text;
            Color accent = AccentColor(variant);
            Accent = Freeze(new SolidColorBrush(accent));
            AccentText = Freeze(new SolidColorBrush(Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Colors.Black : Colors.White));
            Success = Brush(p + "Success");
            Failure = Brush(p + "Failure");
        }
    }

    public ThemeVariant Variant { get; }

    public Brush Plate { get; }

    public Brush PlateBorder { get; }

    public Brush Tile { get; }

    public Brush TileBorder { get; }

    public Brush Text { get; }

    public Brush TextSecondary { get; }

    public Brush Caption { get; }

    public Brush CaptionText { get; }

    public Brush Accent { get; }

    public Brush AccentText { get; }

    public Brush Success { get; }

    public Brush Failure { get; }

    public static FontFamily TextFont => Token<FontFamily>("Font.Text");

    public static FontFamily SmallFont => Token<FontFamily>("Font.Small");

    public static FontFamily IconFont => Token<FontFamily>("Font.Icons");

    /// <summary>The theme Windows is using right now (apps light/dark setting, or high contrast).</summary>
    public static OverlayTheme Current() => For(SystemParameters.HighContrast ? ThemeVariant.HighContrast : IsAppsDark() ? ThemeVariant.Dark : ThemeVariant.Light);

    public static OverlayTheme For(ThemeVariant variant) => new(variant);

    public static double Number(string key) => Token<double>(key);

    public static TimeSpan Duration(string key) => TimeSpan.FromMilliseconds(Number(key));

    /// <summary>True when the taskbar uses the light theme (tray icon should be dark).</summary>
    public static bool IsTaskbarLight() => ReadPersonalize("SystemUsesLightTheme") == 1;

    /// <summary>Segoe Fluent Icons / MDL2 glyph for a format's category.</summary>
    public static string Glyph(MediaCategory category) => category switch
    {
        MediaCategory.Image => "",    // Photo
        MediaCategory.Audio => "",    // Audio
        MediaCategory.Video => "",    // Video
        MediaCategory.Document => "", // Document
        _ => "",                      // Page
    };

    public static class Glyphs
    {
        public const string Files = "";     // Folder
        public const string Check = "";
        public const string Failure = "";   // Cancel
        public const string Unknown = "";   // Unknown
        public const string Settings = "";
        public const string Open = "";      // OpenFile
        public const string Reveal = "";    // FolderOpen
    }

    private static bool IsAppsDark() => ReadPersonalize("AppsUseLightTheme") == 0;

    private static int? ReadPersonalize(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(name) as int?;
    }

    /// <summary>
    /// The accent shade Windows 11 itself uses for filled controls: a light shade on dark backgrounds, a dark shade
    /// on light ones. Falls back to the DWM colour if the WinRT settings aren't available.
    /// </summary>
    private static Color AccentColor(ThemeVariant variant)
    {
        try
        {
            var settings = new global::Windows.UI.ViewManagement.UISettings();
            global::Windows.UI.Color c = settings.GetColorValue(variant == ThemeVariant.Dark
                ? global::Windows.UI.ViewManagement.UIColorType.AccentLight2
                : global::Windows.UI.ViewManagement.UIColorType.AccentDark1);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or TypeLoadException)
        {
            Color fallback = SystemParameters.WindowGlassColor;
            fallback.A = 255;
            return fallback;
        }
    }

    /// <summary>WCAG 2 contrast ratio between two colours (1 to 21).</summary>
    private static double Contrast(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

        static double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }

    private static T Token<T>(string key) => (T)Application.Current.FindResource(key);

    private static SolidColorBrush Brush(string key) => Freeze(new SolidColorBrush(Token<Color>(key)));

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
