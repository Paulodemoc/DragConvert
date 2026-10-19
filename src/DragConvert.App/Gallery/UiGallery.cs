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

        // Glass over a bright and a dark stand-in wallpaper, blurred by the same code the app uses on the real screen.
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            foreach (var (name, setup) in States)
            {
                FrameworkElement frame = Frame(variant, setup, glass: true);
                string path = Path.Combine(directory, $"glass-{variant.ToString().ToLowerInvariant()}-{name}.png");
                File.WriteAllBytes(path, AppIcon.EncodePng(RenderElement(frame, scale: 2)));
                written.Add(path);
            }
        }

        foreach (var (tray, size) in new[] { (false, 16), (false, 32), (false, 256), (true, 16), (true, 32) })
        {
            string path = Path.Combine(directory, $"icon-{(tray ? "tray" : "app")}-{size}.png");
            File.WriteAllBytes(path, AppIcon.EncodePng(AppIcon.Frame(tray, size)));
            written.Add(path);
        }

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
            foreach (var (label, variant, glass) in new[] { ("glass", ThemeVariant.Dark, true), ("ring, light", ThemeVariant.Light, false), ("ring, dark", ThemeVariant.Dark, false) })
            {
                var stack = new StackPanel { Margin = new Thickness(6) };
                stack.Children.Add(new TextBlock { Text = $"{name} ({label})", Margin = new Thickness(0, 0, 0, 4) });
                stack.Children.Add(Frame(variant, setup, glass));
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

    /// <summary>Shows the real overlay window (with its renderer and DWM effects) at screen centre with sample tiles.</summary>
    public static void PreviewOverlay(OverlayRenderer renderer, TimeSpan duration, Action done)
    {
        // Same sequence as the tray app: create hidden, warm up, then show.
        var window = new OverlayWindow(renderer);
        window.Prepare();
        window.WarmUp(() =>
        {
            var (x, y) = DragConvert.Native.Windowing.OverlayWindowInterop.GetCursorPosition();
            DragConvert.Native.Windowing.ScreenRect work = DragConvert.Native.Windowing.OverlayWindowInterop.GetWorkArea(x, y);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            window.ShowAt(work.Left + (work.Width / 2), work.Top + (work.Height / 2));
            States.First(s => s.Name == "04-mixed-with-counts").Setup(window.View);
            Serilog.Log.Information("Preview shown in {Ms:F1} ms after warm-up", System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        });
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            window.Close();
            done();
        };
        timer.Start();
    }

    private static OverlayTile T(string label, MediaCategory category, string? detail = null) => new(label, detail, category);

    /// <summary>The overlay on a stand-in desktop background for its theme.</summary>
    private static Border Frame(ThemeVariant variant, Action<OverlayView> setup, bool glass = false)
    {
        OverlayView view = glass ? new GlassOverlayView() : new RingOverlayView(OverlayTheme.For(variant));
        setup(view);
        Brush desktop = Wallpaper(variant, glass);
        if (view.WantsBackdrop)
        {
            // What the real overlay would capture: the wallpaper under the view, blurred.
            var under = new Border { Width = view.Width + 32, Height = view.Height + 32, Background = desktop };
            BitmapSource shot = RenderElement(under, scale: 1);
            byte[] pixels = new byte[shot.PixelWidth * shot.PixelHeight * 4];
            shot.CopyPixels(pixels, shot.PixelWidth * 4, 0);
            var (blurred, brightness) = FrostedBackdrop.Create(pixels, shot.PixelWidth, shot.PixelHeight, 16, OverlayTheme.Number("Glass.BlurSigma"), 96);
            view.SetBackdrop(blurred, brightness);
        }

        return new Border { Background = desktop, Padding = new Thickness(16), Child = view };
    }

    /// <summary>A stand-in desktop; for glass, a busy one so the frosting shows.</summary>
    private static Brush Wallpaper(ThemeVariant variant, bool busy)
    {
        Brush gradient = variant switch
        {
            ThemeVariant.Dark => new LinearGradientBrush(Color.FromRgb(0x1B, 0x26, 0x38), Color.FromRgb(0x3A, 0x24, 0x4A), 45),
            ThemeVariant.Light => new LinearGradientBrush(Color.FromRgb(0xC9, 0xD8, 0xEA), Color.FromRgb(0xEA, 0xD9, 0xE8), 45),
            _ => SystemColors.DesktopBrush,
        };
        if (!busy)
        {
            return gradient;
        }

        var drawing = new DrawingGroup();
        using (DrawingContext dc = drawing.Open())
        {
            dc.DrawRectangle(gradient, null, new Rect(0, 0, 420, 420));
            bool dark = variant == ThemeVariant.Dark;
            dc.DrawEllipse(new SolidColorBrush(dark ? Color.FromRgb(0x2E, 0x6B, 0xD9) : Color.FromRgb(0xFF, 0xB3, 0x47)), null, new Point(90, 110), 70, 70);
            dc.DrawEllipse(new SolidColorBrush(dark ? Color.FromRgb(0xD9, 0x4E, 0x8F) : Color.FromRgb(0x4F, 0xA8, 0xFF)), null, new Point(320, 300), 90, 90);
            dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x3F, 0xB5, 0x8A) : Color.FromRgb(0x6A, 0xD1, 0x9A)), null, new Rect(230, 40, 150, 70));
            var text = new FormattedText("Documents  Photos  Music", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 26, dark ? Brushes.White : Brushes.Black, 1);
            dc.DrawText(text, new Point(30, 200));
        }

        return new DrawingBrush(drawing) { Stretch = Stretch.UniformToFill, TileMode = TileMode.None };
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
