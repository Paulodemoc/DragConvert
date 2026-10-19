using DragConvert.Core.Conversion;
using ImageMagick;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Image;

/// <summary>
/// Raster image conversions with Magick.NET, in process on a worker thread.
/// HEIC is read-only: HEIC output is out of the MVP because of HEVC patent licensing.
/// </summary>
public sealed class ImageProvider : IConversionProvider
{
    private static readonly MediaFormat[] Inputs = [Jpeg, Png, WebP, Heic, Tiff, Bmp, Gif];
    private static readonly MediaFormat[] Targets = [Jpeg, Png, WebP, Tiff, Bmp, Gif, Pdf];

    public string Id => "image";

    public bool IsAvailable => true;

    public IReadOnlyCollection<ConversionCapability> Capabilities { get; } =
        (from input in Inputs from target in Targets where input != target select new ConversionCapability(input, target)).ToArray();

    public Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                progress?.Report(0);
                try
                {
                    Convert(job, cancellationToken);
                }
                catch (MagickException ex)
                {
                    throw new ConversionException(FirstLine(ex.Message), ex);
                }
            },
            cancellationToken);

    private static void Convert(ConversionJob job, CancellationToken cancellationToken)
    {
        MagickFormat format = ToMagick(job.Target);
        using var frames = new MagickImageCollection(job.InputPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (frames.Count == 0)
        {
            throw new ConversionException("The image has no content.");
        }

        // Keep animation (GIF, WebP) and pages (multi-page TIFF to PDF/TIFF) where the target supports them.
        bool keepAllFrames = frames.Count > 1 && format is MagickFormat.Gif or MagickFormat.WebP or MagickFormat.Pdf or MagickFormat.Tiff;
        if (keepAllFrames)
        {
            if (format is MagickFormat.Gif or MagickFormat.WebP)
            {
                frames.Coalesce();
            }

            foreach (IMagickImage<byte> frame in frames)
            {
                Prepare(frame, format);
            }

            frames.Write(job.OutputPath, format);
            return;
        }

        using IMagickImage<byte> image = frames[0].Clone();
        Prepare(image, format);
        cancellationToken.ThrowIfCancellationRequested();
        image.Write(job.OutputPath, format);
    }

    private static void Prepare(IMagickImage<byte> image, MagickFormat format)
    {
        // Bake the EXIF rotation into the pixels so every viewer shows the same orientation.
        image.AutoOrient();

        if (format is MagickFormat.Jpeg or MagickFormat.Bmp or MagickFormat.Pdf)
        {
            // No (or poor) alpha support: flatten transparency onto white instead of black.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        image.Quality = format switch
        {
            MagickFormat.Jpeg => 90,
            MagickFormat.WebP => 85,
            _ => image.Quality,
        };
    }

    private static MagickFormat ToMagick(MediaFormat target) => target.Id switch
    {
        "jpeg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "tiff" => MagickFormat.Tiff,
        "bmp" => MagickFormat.Bmp,
        "gif" => MagickFormat.Gif,
        "pdf" => MagickFormat.Pdf,
        _ => throw new ConversionException($"Images can't be converted to {target.DisplayName}."),
    };

    private static string FirstLine(string message)
    {
        string line = message.Split('\n', 2)[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}
