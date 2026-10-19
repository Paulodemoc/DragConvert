using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using DragConvert.Providers.Processes;
using ImageMagick;
using PdfSharp.Pdf.IO;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Tools;

/// <summary>
/// Saves a copy without metadata (camera, location, author, comments): "&lt;name&gt; (clean).&lt;ext&gt;".
/// JPEG, PNG and WebP losslessly; TIFF and GIF through Magick.NET; PDFs through PDFsharp; audio and video by copying
/// the streams with FFmpeg (no re-encoding). HEIC isn't supported because it can't be written back.
/// </summary>
public sealed class RemoveMetadataTool(string? ffmpegPath) : IFileTool
{
    private static readonly MediaFormat[] Lossless = [Jpeg, Png, WebP];
    private static readonly MediaFormat[] ViaMagick = [Tiff, Gif];
    private static readonly MediaFormat[] Media = [Mp3, M4a, Wav, Flac, Ogg, Opus, Mp4, WebM, Mov, Mkv];

    private bool HasFfmpeg => ffmpegPath is not null && File.Exists(ffmpegPath);

    public string Id => "remove-metadata";

    public string DisplayName => "Remove metadata";

    public string Glyph => ""; // EraseTool

    public bool IsAvailable => true;

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public string NameSuffix => " (clean)";

    public bool Accepts(MediaFormat input) =>
        Lossless.Contains(input) || ViaMagick.Contains(input) || input == Pdf || (HasFfmpeg && Media.Contains(input));

    public string OutputExtension(MediaFormat input) => input.Extension;

    public async Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string input = job.Inputs[0];
        MediaFormat format = FromPath(input) ?? throw new ConversionException($"Can't remove metadata from {Path.GetFileName(input)}.");

        if (Media.Contains(format))
        {
            await StripMediaAsync(input, job.OutputPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        await Task.Run(
            () =>
            {
                try
                {
                    if (format == Pdf)
                    {
                        using var document = PdfFiles.Open(input, PdfDocumentOpenMode.Modify);
                        PdfFiles.StripMetadata(document);
                        PdfFiles.Save(document, job.OutputPath);
                    }
                    else if (Lossless.Contains(format))
                    {
                        byte[] data = File.ReadAllBytes(input);
                        byte[] clean = format == Jpeg ? MetadataStripper.StripJpeg(data)
                            : format == Png ? MetadataStripper.StripPng(data)
                            : MetadataStripper.StripWebP(data);
                        File.WriteAllBytes(job.OutputPath, clean);
                    }
                    else
                    {
                        using var frames = new MagickImageCollection(input);
                        foreach (IMagickImage<byte> frame in frames)
                        {
                            frame.Strip();
                        }

                        frames.Write(job.OutputPath);
                    }
                }
                catch (FormatException ex)
                {
                    throw new ConversionException($"{Path.GetFileName(input)}: {ex.Message}", ex);
                }
                catch (MagickException ex)
                {
                    throw new ConversionException($"Couldn't read {Path.GetFileName(input)}.", ex);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task StripMediaAsync(string input, string output, CancellationToken cancellationToken)
    {
        if (!HasFfmpeg)
        {
            throw new ConversionException("FFmpeg is not installed.");
        }

        ProcessResult result = await ProcessRunner.RunAsync(
            ffmpegPath!,
            ["-hide_banner", "-nostdin", "-y", "-v", "error", "-i", input, "-map", "0", "-map_metadata", "-1", "-map_chapters", "-1", "-c", "copy", output],
            TimeSpan.FromHours(1),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            string reason = result.StandardErrorTail.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "FFmpeg failed.";
            throw new ConversionException(reason.Length > 200 ? reason[..200] + "…" : reason);
        }
    }
}
