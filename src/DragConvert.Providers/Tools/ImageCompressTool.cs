using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using ImageMagick;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Tools;

/// <summary>
/// Makes photos and graphics smaller in the same format: at most 2560 px on the long side, rotation applied and
/// metadata dropped; JPEG/WebP/AVIF re-encoded at a lower quality, PNG reduced to 256 colours, TIFF re-compressed.
/// </summary>
public sealed class ImageCompressTool : IFileTool
{
    private const int MaxSide = 2560;

    public string Id => "compress-image";

    public string DisplayName => "Compress";

    public string Glyph => "";

    public bool IsAvailable => true;

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public string NameSuffix => " (compressed)";

    public bool Accepts(MediaFormat input) => input == Jpeg || input == Png || input == WebP || input == Avif || input == Tiff;

    public string OutputExtension(MediaFormat? input) => input?.Extension ?? string.Empty;

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            string input = job.Inputs[0];
            MediaFormat format = FromPath(input)!;
            try
            {
                using var image = new MagickImage(input);
                image.AutoOrient();
                image.Strip();
                if (Math.Max(image.Width, image.Height) > MaxSide)
                {
                    image.Resize(new MagickGeometry(MaxSide, MaxSide) { Greater = true });
                }

                cancellationToken.ThrowIfCancellationRequested();
                switch (format.Id)
                {
                    case "jpeg":
                        image.Quality = 75;
                        image.Write(job.OutputPath, MagickFormat.Jpeg);
                        break;
                    case "webp":
                        image.Quality = 75;
                        image.Write(job.OutputPath, MagickFormat.WebP);
                        break;
                    case "avif":
                        image.Quality = 55;
                        image.Write(job.OutputPath, MagickFormat.Avif);
                        break;
                    case "png":
                        // Lossy for PNG, like pngquant: 256 colours, with dithering so gradients stay smooth.
                        image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                        image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                        image.Write(job.OutputPath, MagickFormat.Png8);
                        break;
                    default:
                        image.Settings.Compression = CompressionMethod.Zip;
                        image.Write(job.OutputPath, MagickFormat.Tiff);
                        break;
                }
            }
            catch (MagickException ex)
            {
                throw new ConversionException($"Couldn't read {Path.GetFileName(input)}.", ex);
            }

            if (new FileInfo(job.OutputPath).Length >= new FileInfo(input).Length)
            {
                File.Delete(job.OutputPath);
                throw new ConversionException($"{Path.GetFileName(input)} is already as small as this tool can make it.");
            }
        },
        cancellationToken);
}
