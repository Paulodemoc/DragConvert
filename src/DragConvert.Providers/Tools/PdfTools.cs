using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace DragConvert.Providers.Tools;

/// <summary>Shared helpers for the PDFsharp-based tools: opening with friendly errors, metadata removal.</summary>
internal static class PdfFiles
{
    public static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string reason = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("protected", StringComparison.OrdinalIgnoreCase)
                ? "is password-protected"
                : "isn't a readable PDF";
            throw new ConversionException($"{Path.GetFileName(path)} {reason}.", ex);
        }
    }

    public static void Save(PdfDocument document, string path)
    {
        try
        {
            document.Save(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
        {
            throw new ConversionException($"Couldn't write the PDF: {ex.Message.Split('\n')[0]}", ex);
        }
    }

    /// <summary>Removes the document information dictionary entries and the XMP metadata stream.</summary>
    public static void StripMetadata(PdfDocument document)
    {
        document.Info.Elements.Clear();
        document.Internals.Catalog.Elements.Remove("/Metadata");
    }
}

/// <summary>Merges two or more PDFs, in the order they were dragged, into "&lt;first&gt; (merged).pdf".</summary>
public sealed class MergePdfTool : IFileTool
{
    public string Id => "merge-pdf";

    public string DisplayName => "Merge PDFs";

    public string Glyph => ""; // Copy: stacked pages

    public bool IsAvailable => true;

    public bool CombinesInputs => true;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public string NameSuffix => " (merged)";

    public bool Accepts(MediaFormat input) => input == MediaFormats.Pdf;

    public string OutputExtension(MediaFormat input) => ".pdf";

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            using var output = new PdfDocument();
            for (int i = 0; i < job.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using PdfDocument input = PdfFiles.Open(job.Inputs[i], PdfDocumentOpenMode.Import);
                foreach (PdfPage page in input.Pages)
                {
                    output.AddPage(page);
                }

                progress?.Report((i + 1.0) / job.Inputs.Count * 0.9);
            }

            PdfFiles.Save(output, job.OutputPath);
        },
        cancellationToken);
}

/// <summary>Splits a PDF into a folder "&lt;name&gt; pages" with one PDF per page.</summary>
public sealed class SplitPdfTool : IFileTool
{
    public string Id => "split-pdf";

    public string DisplayName => "Split pages";

    public string Glyph => ""; // Cut

    public bool IsAvailable => true;

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.Folder;

    public string NameSuffix => " pages";

    public bool Accepts(MediaFormat input) => input == MediaFormats.Pdf;

    public string OutputExtension(MediaFormat input) => ".pdf";

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            string name = Path.GetFileNameWithoutExtension(job.Inputs[0]);
            using PdfDocument input = PdfFiles.Open(job.Inputs[0], PdfDocumentOpenMode.Import);
            int count = input.PageCount;
            string digits = new('0', count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var single = new PdfDocument();
                single.AddPage(input.Pages[i]);
                PdfFiles.Save(single, Path.Combine(job.OutputPath, $"{name} - page {(i + 1).ToString(digits, System.Globalization.CultureInfo.InvariantCulture)}.pdf"));
                progress?.Report((i + 1.0) / count);
            }
        },
        cancellationToken);
}

/// <summary>
/// Makes a PDF smaller: recompresses embedded JPEG photos (quality 70, at most 2000 px on the long side), drops page
/// thumbnails, and stores page content with maximum Flate compression. Images with masks or unusual colour spaces are left untouched.
/// </summary>
public sealed class CompressPdfTool : IFileTool
{
    private const int MaxImageSide = 2000;
    private const int JpegQuality = 70;

    public string Id => "compress-pdf";

    public string DisplayName => "Compress";

    public string Glyph => ""; // BackToWindow: arrows pointing in

    public bool IsAvailable => true;

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public string NameSuffix => " (compressed)";

    public bool Accepts(MediaFormat input) => input == MediaFormats.Pdf;

    public string OutputExtension(MediaFormat input) => ".pdf";

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            string input = job.Inputs[0];
            using PdfDocument document = PdfFiles.Open(input, PdfDocumentOpenMode.Modify);
            var seen = new HashSet<PdfDictionary>();
            for (int i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Page thumbnails (some writers embed one image per page) are redundant: viewers render their own.
                document.Pages[i].Elements.Remove("/Thumb");
                PdfDictionary? xObjects = document.Pages[i].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
                foreach (PdfItem? item in xObjects?.Elements.Values ?? [])
                {
                    if ((item as PdfReference)?.Value is PdfDictionary image && seen.Add(image))
                    {
                        RecompressJpeg(image);
                    }
                }

                progress?.Report((i + 1.0) / document.PageCount * 0.8);
            }

            document.Options.NoCompression = false;
            document.Options.CompressContentStreams = true;
            document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            PdfFiles.Save(document, job.OutputPath);

            if (new FileInfo(job.OutputPath).Length >= new FileInfo(input).Length)
            {
                File.Delete(job.OutputPath);
                throw new ConversionException($"{Path.GetFileName(input)} is already as small as this tool can make it.");
            }
        },
        cancellationToken);

    private static void RecompressJpeg(PdfDictionary image)
    {
        PdfDictionary.DictionaryElements e = image.Elements;
        bool isJpeg = Name(e["/Subtype"]) == "/Image" && Name(e["/Filter"]) == "/DCTDecode";
        int? components = Components(e["/ColorSpace"]);
        if (!isJpeg || image.Stream is null || components is null
            || e.ContainsKey("/SMask") || e.ContainsKey("/Mask") || e.ContainsKey("/Decode"))
        {
            return;
        }

        byte[] original = image.Stream.Value;
        try
        {
            using var picture = new MagickImage(original);
            if (Math.Max(picture.Width, picture.Height) > MaxImageSide)
            {
                picture.Resize(new MagickGeometry(MaxImageSide, MaxImageSide) { Greater = true });
            }

            picture.Strip();
            picture.Quality = JpegQuality;

            // The PDF colour space fixes the channel count; never let an all-grey photo become a 1-channel JPEG.
            picture.ColorType = components == 1 ? ColorType.Grayscale : ColorType.TrueColor;
            byte[] smaller = picture.ToByteArray(MagickFormat.Jpeg);
            if (smaller.Length < original.Length * 0.9)
            {
                image.Stream.Value = smaller;
                e.SetInteger("/Width", (int)picture.Width);
                e.SetInteger("/Height", (int)picture.Height);
                e.SetInteger("/Length", smaller.Length);
                e.Remove("/DecodeParms");
            }
        }
        catch (MagickException)
        {
            // Unusual JPEG: keep the original bytes.
        }
    }

    /// <summary>A PDF name's value, also when wrapped in a one-element array (e.g. /Filter [/DCTDecode]); else null.</summary>
    private static string? Name(PdfItem? item) => item switch
    {
        PdfName name => name.Value,
        PdfNameObject indirectName => indirectName.Value,
        PdfArray { Elements.Count: 1 } array => Name(array.Elements[0]),
        PdfReference reference => Name(reference.Value),
        _ => null,
    };

    /// <summary>Colour channels for RGB, grey and ICC-based RGB/grey colour spaces; null for anything else (CMYK, indexed...).</summary>
    private static int? Components(PdfItem? colorSpace)
    {
        switch (Name(colorSpace))
        {
            case "/DeviceRGB":
                return 3;
            case "/DeviceGray":
                return 1;
        }

        PdfItem? resolved = colorSpace is PdfReference r ? r.Value : colorSpace;
        if (resolved is PdfArray { Elements.Count: 2 } array && Name(array.Elements[0]) == "/ICCBased"
            && (array.Elements[1] as PdfReference)?.Value is PdfDictionary profile)
        {
            int n = profile.Elements.GetInteger("/N");
            return n is 1 or 3 ? n : null;
        }

        return null;
    }
}

/// <summary>Combines images, in the order they were dragged, into one PDF with a page per image.</summary>
public sealed class ImagesToPdfTool : IFileTool
{
    private static readonly MediaFormat[] Inputs = [MediaFormats.Jpeg, MediaFormats.Png, MediaFormats.WebP, MediaFormats.Heic, MediaFormats.Tiff, MediaFormats.Bmp, MediaFormats.Gif];

    public string Id => "images-to-pdf";

    public string DisplayName => "Combine to PDF";

    public string Glyph => ""; // Document

    public bool IsAvailable => true;

    public bool CombinesInputs => true;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public string NameSuffix => " (combined)";

    public bool Accepts(MediaFormat input) => Inputs.Contains(input);

    public string OutputExtension(MediaFormat input) => ".pdf";

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            using var pages = new MagickImageCollection();
            for (int i = 0; i < job.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var page = new MagickImage(job.Inputs[i]);
                    page.AutoOrient();
                    page.BackgroundColor = MagickColors.White;
                    page.Alpha(AlphaOption.Remove);
                    page.Density = new Density(150);
                    page.Quality = 85;
                    pages.Add(page);
                }
                catch (MagickException ex)
                {
                    throw new ConversionException($"Couldn't read {Path.GetFileName(job.Inputs[i])}.", ex);
                }

                progress?.Report((i + 1.0) / job.Inputs.Count * 0.8);
            }

            pages.Write(job.OutputPath, MagickFormat.Pdf);
        },
        cancellationToken);
}
