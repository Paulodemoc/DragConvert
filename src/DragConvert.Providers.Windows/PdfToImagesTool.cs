using System.Globalization;
using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DragConvert.Providers.Windows;

/// <summary>
/// Renders each page of a PDF to a PNG in a folder "&lt;name&gt; images", with Windows' built-in PDF renderer
/// (Windows.Data.Pdf), so no PDF engine has to be bundled. Pages render at 150 DPI on a white background.
/// </summary>
public sealed class PdfToImagesTool : IFileTool
{
    private const double Dpi = 150;

    public string Id => "pdf-to-images";

    public string DisplayName => "Pages to images";

    public string Glyph => ""; // Photo

    public bool IsAvailable => true;

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.Folder;

    public string NameSuffix => " images";

    public bool Accepts(MediaFormat input) => input == MediaFormats.Pdf;

    public string OutputExtension(MediaFormat input) => ".png";

    public async Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string input = job.Inputs[0];
        string name = Path.GetFileNameWithoutExtension(input);
        PdfDocument document;
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(input);
            document = await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string reason = ex.HResult == unchecked((int)0x8007052B) || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "is password-protected"
                : "isn't a readable PDF";
            throw new ConversionException($"{Path.GetFileName(input)} {reason}.", ex);
        }

        uint count = document.PageCount;
        string digits = new('0', count.ToString(CultureInfo.InvariantCulture).Length);
        for (uint i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using PdfPage page = document.GetPage(i);
            using var rendered = new InMemoryRandomAccessStream();

            // Page.Size is in device-independent pixels (1/96 inch).
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width * Dpi / 96),
                DestinationHeight = (uint)Math.Round(page.Size.Height * Dpi / 96),
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
            };
            await page.RenderToStreamAsync(rendered, options);

            string output = Path.Combine(job.OutputPath, $"{name} - page {(i + 1).ToString(digits, CultureInfo.InvariantCulture)}.png");
            await using (FileStream target = File.Create(output))
            {
                rendered.Seek(0);
                await rendered.AsStreamForRead().CopyToAsync(target, cancellationToken);
            }

            progress?.Report((i + 1.0) / count);
        }
    }
}
