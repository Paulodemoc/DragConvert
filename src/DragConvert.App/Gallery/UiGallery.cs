using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DragConvert.App.Overlay;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;

namespace DragConvert.App.Gallery;

/// <summary>
/// Every overlay state with fake data, for iterating on visuals without the drag pipeline.
/// <c>--gallery</c> shows them live in a window; <c>--snapshot [dir]</c> renders them to PNG (default artifacts/ui)
/// so changes can be reviewed as images. Backdrop blur and DWM effects only appear on a real desktop.
/// </summary>
internal static class UiGallery
{
    private static readonly MediaCategory Image = MediaCategory.Image;
    private static readonly MediaCategory Audio = MediaCategory.Audio;
    private static readonly MediaCategory Video = MediaCategory.Video;
    private static readonly MediaCategory Doc = MediaCategory.Document;

    private static readonly (string Name, Action<OverlayView> Setup)[] States =
    [
        ("01-just-shown", v => { }),
        ("02-single-image", v =>
        {
            v.SetBadge(Image, 1);
            v.SetTiles([T("PNG", Image), T("WebP", Image), T("TIFF", Image), T("BMP", Image), T("GIF", Image), T("PDF", Doc)], "IMG_0001.jpg", animate: false);
        }),
        ("03-hover", v =>
        {
            v.SetBadge(Image, 1);
            v.SetTiles([T("PNG", Image), T("WebP", Image), T("TIFF", Image), T("BMP", Image), T("GIF", Image), T("PDF", Doc)], "IMG_0001.jpg", animate: false);
            v.SetHovered(0);
        }),
        ("04-mixed-with-counts", v =>
        {
            v.SetBadge(null, 5);
            v.SetTiles(
            [
                T("GIF", Image), T("JPEG", Image, "3 of 5"), T("PNG", Image, "3 of 5"), T("WebP", Image, "3 of 5"),
                T("PDF", Doc, "3 of 5"), T("MP3", Audio, "2 of 5"), T("M4A", Audio, "2 of 5"), T("WebM", Video, "2 of 5"),
            ],
            "5 files in 1 folder · 6 more formats in the tray window",
            animate: false);
            v.SetHovered(1);
        }),
        ("05-audio", v =>
        {
            v.SetBadge(Audio, 1);
            v.SetTiles([T("MP3", Audio), T("M4A", Audio), T("FLAC", Audio), T("OGG", Audio), T("Opus", Audio)], "interview.wav", animate: false);
        }),
        ("06-video", v =>
        {
            v.SetBadge(Video, 1);
            v.SetTiles([T("WebM", Video), T("GIF", Image), T("MP3", Audio), T("M4A", Audio)], "clip.mov", animate: false);
        }),
        ("07-nothing-to-convert", v =>
        {
            v.SetBadge(null, 1);
            v.SetTiles([], "Nothing here can be converted", animate: false);
        }),
        ("08-working", v =>
        {
            v.SetTiles([T("PNG", Image), T("WebP", Image), T("TIFF", Image), T("BMP", Image), T("GIF", Image), T("PDF", Doc)], animate: false);
            v.ShowWorking(0);
        }),
        ("09-success", v =>
        {
            v.SetTiles([T("PNG", Image), T("WebP", Image), T("TIFF", Image), T("BMP", Image), T("GIF", Image), T("PDF", Doc)], animate: false);
            v.ShowWorking(0);
            v.ShowResult(success: true);
        }),
        ("10-failure", v =>
        {
            v.SetTiles([T("PNG", Image), T("WebP", Image), T("TIFF", Image), T("BMP", Image), T("GIF", Image), T("PDF", Doc)], animate: false);
            v.ShowWorking(0);
            v.ShowResult(success: false);
        }),
    ];

    /// <summary>Renders every state in light and dark (and high contrast when it's on), plus the icons, as PNG files.</summary>
    public static IReadOnlyList<string> Snapshot(string directory)
    {
        Directory.CreateDirectory(directory);
        OverlayView.AnimationsOverride = false;
        var written = new List<string>();
        var variants = new List<ThemeVariant> { ThemeVariant.Light, ThemeVariant.Dark };
        if (SystemParameters.HighContrast)
        {
            variants.Add(ThemeVariant.HighContrast);
        }

        foreach (ThemeVariant variant in variants)
        {
            foreach (var (name, setup) in States)
            {
                FrameworkElement frame = Frame(variant, setup);
                string path = Path.Combine(directory, $"overlay-{variant.ToString().ToLowerInvariant()}-{name}.png");
                File.WriteAllBytes(path, AppIcon.EncodePng(RenderElement(frame, scale: 2)));
                written.Add(path);
            }
        }

        foreach (int size in new[] { 16, 32, 256 })
        {
            string path = Path.Combine(directory, $"icon-app-{size}.png");
            File.WriteAllBytes(path, AppIcon.EncodePng(AppIcon.Render(size, colored: true, Colors.White)));
            written.Add(path);
        }

        foreach (bool light in new[] { true, false })
        {
            string path = Path.Combine(directory, $"icon-tray-{(light ? "light" : "dark")}-taskbar.png");
            var tray = new Border { Width = 64, Height = 64, Background = new SolidColorBrush(light ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x20, 0x20, 0x20)), Child = new System.Windows.Controls.Image { Source = AppIcon.TrayPreview(light), Width = 32, Height = 32 } };
            File.WriteAllBytes(path, AppIcon.EncodePng(RenderElement(tray, scale: 1)));
            written.Add(path);
        }

        string ico = Path.Combine(directory, "DragConvert.ico");
        AppIcon.WriteIco(ico, 16, 20, 24, 32, 40, 48, 64, 256);
        written.Add(ico);

        OverlayView.AnimationsOverride = null;
        return written;
    }

    /// <summary>A scrollable window with every state live, light and dark side by side.</summary>
    public static Window CreateWindow()
    {
        OverlayView.AnimationsOverride = false;
        var panel = new WrapPanel { Margin = new Thickness(12) };
        foreach (var (name, setup) in States)
        {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var stack = new StackPanel { Margin = new Thickness(6) };
                stack.Children.Add(new TextBlock { Text = $"{name} ({variant})", Margin = new Thickness(0, 0, 0, 4) });
                stack.Children.Add(Frame(variant, setup));
                panel.Children.Add(stack);
            }
        }

        var window = new Window
        {
            Title = "DragConvert UI gallery",
            Width = 1200,
            Height = 900,
            Content = new ScrollViewer { Content = panel },
            Icon = AppIcon.Window(),
        };
        window.Closed += (_, _) => OverlayView.AnimationsOverride = null;
        return window;
    }

    private static OverlayTile T(string label, MediaCategory category, string? detail = null) => new(label, detail, category);

    /// <summary>The overlay on a stand-in desktop background for its theme.</summary>
    private static Border Frame(ThemeVariant variant, Action<OverlayView> setup)
    {
        var view = new OverlayView(OverlayTheme.For(variant));
        setup(view);
        Brush desktop = variant switch
        {
            ThemeVariant.Dark => new LinearGradientBrush(Color.FromRgb(0x1B, 0x26, 0x38), Color.FromRgb(0x3A, 0x24, 0x4A), 45),
            ThemeVariant.Light => new LinearGradientBrush(Color.FromRgb(0xC9, 0xD8, 0xEA), Color.FromRgb(0xEA, 0xD9, 0xE8), 45),
            _ => SystemColors.DesktopBrush,
        };
        return new Border { Background = desktop, Padding = new Thickness(16), Child = view };
    }

    private static BitmapSource RenderElement(FrameworkElement element, double scale)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * scale),
            (int)Math.Ceiling(element.ActualHeight * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }
}
