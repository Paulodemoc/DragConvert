using System.Net;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DragConvert.Core.Conversion;
using Markdig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Documents;

/// <summary>
/// Text out of documents: PDF → TXT/MD (PdfPig text extraction, one section per page), DOCX → TXT/MD/HTML (headings,
/// paragraphs, bold/italic, lists, links and tables via the OpenXML SDK), Markdown → HTML/TXT (Markdig).
/// Layout, images and fonts aren't carried over; for faithful layouts see docs/roadmap.md (Tier C).
/// </summary>
public sealed class DocumentTextProvider : IConversionProvider
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public string Id => "document-text";

    public bool IsAvailable => true;

    public IReadOnlyCollection<ConversionCapability> Capabilities { get; } =
    [
        new(Pdf, Txt), new(Pdf, MediaFormats.Markdown),
        new(Docx, Txt), new(Docx, MediaFormats.Markdown), new(Docx, Html),
        new(MediaFormats.Markdown, Html), new(MediaFormats.Markdown, Txt),
    ];

    public Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            MediaFormat input = FromPath(job.InputPath)!;
            string output = input.Id switch
            {
                "pdf" => PdfText(job.InputPath, markdown: job.Target == MediaFormats.Markdown, cancellationToken),
                "docx" => DocxText(job.InputPath, job.Target),
                _ => job.Target == Html
                    ? HtmlPage(Path.GetFileNameWithoutExtension(job.InputPath), Markdig.Markdown.ToHtml(File.ReadAllText(job.InputPath), Pipeline))
                    : Markdig.Markdown.ToPlainText(File.ReadAllText(job.InputPath), Pipeline),
            };
            File.WriteAllText(job.OutputPath, output, Utf8);
        },
        cancellationToken);

    /// <summary>A minimal, readable HTML page around converted content.</summary>
    public static string HtmlPage(string title, string body) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
        body { font-family: "Segoe UI", system-ui, sans-serif; max-width: 48rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.55; }
        table { border-collapse: collapse; } td, th { border: 1px solid #ccc; padding: .3rem .6rem; }
        </style>
        </head>
        <body>
        {{body}}
        </body>
        </html>
        """;

    private static string PdfText(string path, bool markdown, CancellationToken cancellationToken)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = ContentOrderTextExtractor.GetText(page).Trim();
                if (markdown)
                {
                    sb.Append("## Page ").Append(page.Number).Append("\n\n");
                }

                sb.Append(text).Append("\n\n");
            }

            string result = sb.ToString().TrimEnd() + "\n";
            return string.IsNullOrWhiteSpace(result.Replace("## Page", string.Empty, StringComparison.Ordinal).Trim('#', ' ', '\n', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
                ? throw new ConversionException($"{Path.GetFileName(path)} has no text layer (it may be scanned images; OCR isn't supported).")
                : result;
        }
        catch (Exception ex) when (ex is not ConversionException and not OperationCanceledException)
        {
            string reason = ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                ? "is password-protected"
                : "isn't a readable PDF";
            throw new ConversionException($"{Path.GetFileName(path)} {reason}.", ex);
        }
    }

    private static string DocxText(string path, MediaFormat target)
    {
        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(path, isEditable: false);
        }
        catch (Exception ex) when (ex is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException or FileFormatException)
        {
            throw new ConversionException($"{Path.GetFileName(path)} isn't a readable Word document.", ex);
        }

        using (document)
        {
            var reader = new DocxReader(document);
            var writer = new StringBuilder();
            foreach (DocxReader.Block block in reader.Blocks())
            {
                writer.Append(target.Id switch
                {
                    "md" => block.ToMarkdown(),
                    "html" => block.ToHtml(),
                    _ => block.ToText(),
                });
            }

            string body = writer.ToString().Trim() + "\n";
            return target == Html ? HtmlPage(Path.GetFileNameWithoutExtension(path), body) : body;
        }
    }
}
