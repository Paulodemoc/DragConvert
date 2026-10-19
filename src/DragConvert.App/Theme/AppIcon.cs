using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragConvert.App.Theme;

/// <summary>
/// The DragConvert mark, drawn as vector geometry (original artwork): a source file, a curved arrow, and the
/// converted file. A coloured tile version for windows and the .ico, and a monochrome glyph for the tray.
/// </summary>
internal static class AppIcon
{
    // 24x24 design grid.
    private const double Grid = 24;

    private static readonly Geometry SourceFile = Geometry.Parse("M4.5,2.5 H9 L12,5.5 V12 A1.5,1.5 0 0 1 10.5,13.5 H4.5 A1.5,1.5 0 0 1 3,12 V4 A1.5,1.5 0 0 1 4.5,2.5 Z");
    private static readonly Geometry TargetFile = Geometry.Parse("M14,10 H18.5 L21.5,13 V20 A1.5,1.5 0 0 1 20,21.5 H14 A1.5,1.5 0 0 1 12.5,20 V11.5 A1.5,1.5 0 0 1 14,10 Z");
    private static readonly Geometry ArrowShaft = Geometry.Parse("M6.5,15.5 C6.5,18.5 7.5,19.2 10.2,19.2");
    private static readonly Geometry ArrowHead = Geometry.Parse("M8.6,17.4 L10.6,19.2 L8.6,21");

    /// <summary>
    /// Monochrome mark for the notification area: white on a dark taskbar, near-black on a light one. A multi-size
    /// icon so Windows picks the right size for the display scaling.
    /// </summary>
    public static System.Drawing.Icon Tray(bool lightTaskbar) =>
        new(new MemoryStream(IcoBytes(colored: false, TrayColor(lightTaskbar), 16, 20, 24, 32, 40, 48, 64)));

    /// <summary>The tray mark as an image, for previews.</summary>
    public static BitmapSource TrayPreview(bool lightTaskbar, int pixels = 32) => Render(pixels, colored: false, TrayColor(lightTaskbar));

    /// <summary>The coloured tile mark for window title bars.</summary>
    public static ImageSource Window() => Render(32, colored: true, Colors.White);

    public static BitmapSource Render(int pixels, bool colored, Color glyphColor)
    {
        var drawing = new DrawingGroup();
        using (DrawingContext dc = drawing.Open())
        {
            double scale = pixels / Grid;
            dc.PushTransform(new ScaleTransform(scale, scale));

            if (colored)
            {
                var tile = new LinearGradientBrush(Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x7C, 0x3A, 0xED), new Point(0, 0), new Point(1, 1));
                dc.DrawRoundedRectangle(tile, null, new Rect(0.5, 0.5, 23, 23), 5.5, 5.5);
                // Inset the mark inside the tile.
                dc.PushTransform(new TranslateTransform(2.4, 2.4));
                dc.PushTransform(new ScaleTransform(0.8, 0.8));
            }

            var glyph = new SolidColorBrush(glyphColor);
            // Heavier strokes for the tray, which shows the mark at 16-24 px.
            var outline = new Pen(glyph, colored ? 1.9 : 2.3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, outline, SourceFile);
            dc.DrawGeometry(glyph, null, TargetFile);
            dc.DrawGeometry(null, outline, ArrowShaft);
            dc.DrawGeometry(null, outline, ArrowHead);

            if (colored)
            {
                dc.Pop();
                dc.Pop();
            }

            dc.Pop();
        }

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawDrawing(drawing);
        }

        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Writes a multi-size .ico of the coloured mark.</summary>
    public static void WriteIco(string path, params int[] sizes) => File.WriteAllBytes(path, IcoBytes(colored: true, Colors.White, sizes));

    /// <summary>A multi-size .ico (PNG-compressed entries).</summary>
    public static byte[] IcoBytes(bool colored, Color glyphColor, params int[] sizes)
    {
        var images = sizes.Select(size => (Size: size, Png: EncodePng(Render(size, colored, glyphColor)))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)images.Count);
        int offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);           // palette
            writer.Write((byte)0);           // reserved
            writer.Write((ushort)1);         // planes
            writer.Write((ushort)32);        // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static Color TrayColor(bool lightTaskbar) => lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;

    public static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}
