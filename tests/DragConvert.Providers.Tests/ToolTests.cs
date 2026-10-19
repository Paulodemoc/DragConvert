using System.Text;
using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using DragConvert.Providers.Tools;
using DragConvert.Providers.Windows;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace DragConvert.Providers.Tests;

public sealed class ToolTests(MediaFixtures fixtures) : IClassFixture<MediaFixtures>
{
    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Work(string name) => fixtures.OutputPath(name);

    /// <summary>A PDF of solid-colour pages (no fonts needed) with author and title metadata.</summary>
    private string MakePdf(int pages, string? password = null)
    {
        string path = Work($"doc{pages}.pdf");
        using var document = new PdfDocument();
        document.Info.Author = "Secret Author";
        document.Info.Title = "Quarterly figures";
        for (int i = 0; i < pages; i++)
        {
            PdfPage page = document.AddPage();
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 40 * i, 120, 200)), 50, 50, 300, 200);
        }

        if (password is not null)
        {
            document.SecuritySettings.UserPassword = password;
        }

        document.Save(path);
        return path;
    }

    /// <summary>A one-page PDF holding a large, high-quality photo: the case compression is for.</summary>
    private string MakePhotoPdf()
    {
        string jpeg = Work("photo.jpg");
        using (var noise = new MagickImage("plasma:fractal", 2600, 1800))
        {
            noise.Quality = 98;
            noise.Write(jpeg, MagickFormat.Jpeg);
        }

        string path = Work("photo.pdf");
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        using (XImage image = XImage.FromFile(jpeg))
        {
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        document.Save(path);
        return path;
    }

    private static async Task<string> RunAsync(IFileTool tool, string output, params string[] inputs)
    {
        if (tool.OutputKind == ToolOutputKind.Folder)
        {
            Directory.CreateDirectory(output);
        }

        await tool.RunAsync(new ToolJob(inputs, output, NoOptions), null, Ct);
        return output;
    }

    [Fact]
    public async Task Merge_keeps_every_page_in_drag_order()
    {
        string output = await RunAsync(new MergePdfTool(), Work("merged.pdf"), MakePdf(3), MakePdf(2));
        using PdfDocument merged = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(5, merged.PageCount);
    }

    [Fact]
    public async Task Split_writes_one_pdf_per_page()
    {
        string folder = await RunAsync(new SplitPdfTool(), Work("pages"), MakePdf(3));

        string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(3, files.Length);
        Assert.EndsWith(" - page 1.pdf", files[0], StringComparison.Ordinal);
        foreach (string file in Directory.GetFiles(folder))
        {
            using PdfDocument page = PdfReader.Open(file, PdfDocumentOpenMode.Import);
            Assert.Equal(1, page.PageCount);
        }
    }

    [Fact]
    public async Task Compress_makes_a_photo_pdf_smaller_and_keeps_its_pages()
    {
        string input = MakePhotoPdf();
        string output = await RunAsync(new CompressPdfTool(), Work("compressed.pdf"), input);

        long before = new FileInfo(input).Length;
        long after = new FileInfo(output).Length;
        Assert.True(after < before * 0.7, $"Expected a real reduction: {before:N0} -> {after:N0} bytes");
        using PdfDocument compressed = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, compressed.PageCount);
    }

    [Fact]
    public async Task Compress_handles_pdfs_written_by_images_to_pdf()
    {
        string photo = Work("big.jpg");
        using (var noise = new MagickImage("plasma:", 2400, 1600))
        {
            noise.Quality = 98;
            noise.Write(photo, MagickFormat.Jpeg);
        }

        string grey = Work("grey.jpg");
        using (var flat = new MagickImage(new MagickColor("#808080"), 2400, 1600))
        {
            flat.Quality = 98;
            flat.Write(grey, MagickFormat.Jpeg);
        }

        string combined = await RunAsync(new ImagesToPdfTool(), Work("photos.pdf"), photo, grey);
        string output = await RunAsync(new CompressPdfTool(), Work("photos-small.pdf"), combined);

        Assert.True(new FileInfo(output).Length < new FileInfo(combined).Length);
        using PdfDocument pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
    }

    [Fact]
    public async Task Pages_to_images_renders_each_page_at_150_dpi()
    {
        string folder = await RunAsync(new PdfToImagesTool(), Work("images"), MakePdf(2));

        string[] pngs = Directory.GetFiles(folder, "*.png");
        Assert.Equal(2, pngs.Length);
        var info = new MagickImageInfo(pngs[0]);
        // PDFsharp's default page is A4 (8.27 x 11.69 in).
        Assert.InRange(info.Width, 1235u, 1245u);
        Assert.InRange(info.Height, 1749u, 1759u);
    }

    [Fact]
    public async Task Images_to_pdf_makes_a_page_per_image()
    {
        string a = Work("a.png");
        string b = Work("b.jpg");
        using (var red = new MagickImage(MagickColors.Red, 300, 200))
        {
            red.Write(a, MagickFormat.Png);
        }

        using (var blue = new MagickImage(MagickColors.Blue, 200, 300))
        {
            blue.Write(b, MagickFormat.Jpeg);
        }

        string output = await RunAsync(new ImagesToPdfTool(), Work("combined.pdf"), a, b);
        using PdfDocument pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
    }

    [Fact]
    public async Task Password_protected_pdf_fails_with_a_clear_reason()
    {
        string locked = MakePdf(1, password: "s3cret");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => RunAsync(new SplitPdfTool(), Work("locked-pages"), locked));
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remove_metadata_clears_pdf_author_and_title()
    {
        string output = await RunAsync(new RemoveMetadataTool(null), Work("clean.pdf"), MakePdf(2));
        using PdfDocument clean = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.True(string.IsNullOrEmpty(clean.Info.Author));
        Assert.True(string.IsNullOrEmpty(clean.Info.Title));
        Assert.Equal(2, clean.PageCount);
    }

    [Fact]
    public async Task Remove_metadata_from_jpeg_is_lossless_and_keeps_orientation()
    {
        string input = Work("camera.jpg");
        using (var photo = new MagickImage("plasma:", 120, 80))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Artist, "Jane Photographer");
            exif.SetValue(ExifTag.Make, "PhoneCo");
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            photo.Orientation = OrientationType.RightTop;
            photo.SetProfile(exif);
            photo.Comment = "private comment";
            photo.Write(input, MagickFormat.Jpeg);
        }

        string output = await RunAsync(new RemoveMetadataTool(null), Work("camera-clean.jpg"), input);

        byte[] before = await File.ReadAllBytesAsync(input, Ct);
        byte[] after = await File.ReadAllBytesAsync(output, Ct);
        string text = Encoding.Latin1.GetString(after);
        Assert.DoesNotContain("Jane Photographer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PhoneCo", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private comment", text, StringComparison.Ordinal);

        // Image data (from the start-of-scan marker on) is byte-for-byte identical.
        Assert.Equal(before.AsSpan(IndexOfScan(before)).ToArray(), after.AsSpan(IndexOfScan(after)).ToArray());

        using var clean = new MagickImage(output);
        Assert.Equal(OrientationType.RightTop, clean.Orientation);
    }

    [Fact]
    public async Task Remove_metadata_from_png_drops_text_chunks()
    {
        string input = Work("notes.png");
        using (var image = new MagickImage(MagickColors.Green, 40, 40))
        {
            image.Comment = "secret note";
            image.Write(input, MagickFormat.Png);
        }

        Assert.Contains("secret note", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(input, Ct)), StringComparison.Ordinal);
        string output = await RunAsync(new RemoveMetadataTool(null), Work("notes-clean.png"), input);

        Assert.DoesNotContain("secret note", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output, Ct)), StringComparison.Ordinal);
        Assert.Equal(40u, new MagickImageInfo(output).Width);
    }

    [Fact]
    public async Task Remove_metadata_from_webp_drops_exif()
    {
        string input = Work("pic.webp");
        using (var image = new MagickImage("plasma:", 64, 64))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Artist, "Jane Photographer");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.WebP);
        }

        string output = await RunAsync(new RemoveMetadataTool(null), Work("pic-clean.webp"), input);

        Assert.DoesNotContain("Jane Photographer", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output, Ct)), StringComparison.Ordinal);
        Assert.Equal(64u, new MagickImageInfo(output).Width);
    }

    [Fact]
    public async Task Remove_metadata_from_audio_copies_streams_without_tags()
    {
        Assert.SkipWhen(fixtures.Ffmpeg is null, "ffmpeg not found; run tools/fetch-ffmpeg.ps1");
        string tagged = Work("tagged.mp3");
        await Processes.ProcessRunner.RunAsync(fixtures.Ffmpeg!, ["-v", "error", "-y", "-i", fixtures.Wav, "-metadata", "title=Secret Song", "-c:a", "libmp3lame", tagged], TimeSpan.FromMinutes(1), cancellationToken: Ct);

        string output = await RunAsync(new RemoveMetadataTool(fixtures.Ffmpeg), Work("tagged-clean.mp3"), tagged);

        Assert.DoesNotContain("Secret Song", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output, Ct)), StringComparison.Ordinal);
        Assert.Equal(["mp3"], await fixtures.CodecsAsync(output));
    }

    [Fact]
    public void Remove_metadata_needs_ffmpeg_for_audio_and_video_only()
    {
        var tool = new RemoveMetadataTool(null);
        Assert.True(tool.Accepts(MediaFormats.Jpeg));
        Assert.True(tool.Accepts(MediaFormats.Pdf));
        Assert.False(tool.Accepts(MediaFormats.Mp3));
        Assert.False(tool.Accepts(MediaFormats.Heic));
    }

    private static int IndexOfScan(byte[] jpeg)
    {
        for (int i = 2; i < jpeg.Length - 1; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA)
            {
                return i;
            }
        }

        throw new InvalidOperationException("No start-of-scan marker.");
    }
}
