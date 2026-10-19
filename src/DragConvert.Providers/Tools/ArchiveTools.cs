using System.Formats.Tar;
using System.IO.Compression;
using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using SharpCompress.Archives;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Tools;

/// <summary>
/// Extracts ZIP, TAR, GZIP (.gz, .tar.gz/.tgz), RAR and 7z into a folder beside the archive. Every entry's path is
/// checked to stay inside that folder, so a malicious archive can't write elsewhere ("zip slip").
/// </summary>
public sealed class ExtractArchiveTool : IFileTool
{
    public string Id => "extract";

    public string DisplayName => "Extract";

    public string Glyph => ""; // Package

    public bool IsAvailable => true;

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.Folder;

    public string NameSuffix => string.Empty;

    public bool Accepts(MediaFormat input) => input == Zip || input == Tar || input == Gzip || input == Rar || input == SevenZip;

    public string OutputExtension(MediaFormat? input) => string.Empty;

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            string input = job.Inputs[0];
            string destination = Path.GetFullPath(job.OutputPath) + Path.DirectorySeparatorChar;
            try
            {
                switch (FromPath(input)!.Id)
                {
                    case "zip":
                        // Validates entry paths against the destination and throws on traversal.
                        ZipFile.ExtractToDirectory(input, job.OutputPath, overwriteFiles: false);
                        break;
                    case "tar":
                        TarFile.ExtractToDirectory(input, job.OutputPath, overwriteFiles: false);
                        break;
                    case "gzip":
                        ExtractGzip(input, job.OutputPath);
                        break;
                    default:
                        ExtractWithSharpCompress(input, destination, cancellationToken);
                        break;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or SharpCompress.Common.ArchiveException or SharpCompress.Common.CryptographicException or NotSupportedException)
            {
                string reason = ex is SharpCompress.Common.CryptographicException || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                    ? "is password-protected"
                    : "isn't a readable archive";
                throw new ConversionException($"{Path.GetFileName(input)} {reason}.", ex);
            }
        },
        cancellationToken);

    private static void ExtractGzip(string input, string folder)
    {
        string name = Path.GetFileName(input);
        using FileStream file = File.OpenRead(input);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            TarFile.ExtractToDirectory(gzip, folder, overwriteFiles: false);
            return;
        }

        // A single compressed file: "report.pdf.gz" → "report.pdf".
        using FileStream output = File.Create(Path.Combine(folder, Path.GetFileNameWithoutExtension(name)));
        gzip.CopyTo(output);
    }

    private static void ExtractWithSharpCompress(string input, string destination, CancellationToken cancellationToken)
    {
        using IArchive archive = ArchiveFactory.OpenArchive(input, new SharpCompress.Readers.ReaderOptions());
        foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory && e.Key is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(Path.Combine(destination, entry.Key!.Replace('\\', '/').TrimStart('/')));
            if (!target.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The archive contains an unsafe path: {entry.Key}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream source = entry.OpenEntryStream();
            using FileStream output = new(target, FileMode.CreateNew);
            source.CopyTo(output);
        }
    }
}

/// <summary>Packs the dragged files (any type) into one ZIP, keeping their folder structure relative to each other.</summary>
public sealed class CreateZipTool : IFileTool
{
    public string Id => "create-zip";

    public string DisplayName => "Zip";

    public string Glyph => ""; // Package

    public bool IsAvailable => true;

    public bool CombinesInputs => true;

    public int MinimumInputs => 1;

    public bool AcceptsAnyFile => true;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public string NameSuffix => string.Empty;

    public bool Accepts(MediaFormat input) => true;

    public string OutputExtension(MediaFormat? input) => ".zip";

    public Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            string root = CommonDirectory(job.Inputs);
            using var zip = ZipFile.Open(job.OutputPath, ZipArchiveMode.Create);
            for (int i = 0; i < job.Inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string file = job.Inputs[i];
                zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Optimal);
                progress?.Report((i + 1.0) / job.Inputs.Count);
            }
        },
        cancellationToken);

    private static string CommonDirectory(IReadOnlyList<string> files)
    {
        string common = Path.GetDirectoryName(files[0])!;
        foreach (string file in files.Skip(1))
        {
            string folder = Path.GetDirectoryName(file)!;
            while (!folder.StartsWith(common.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(folder, common, StringComparison.OrdinalIgnoreCase))
            {
                common = Path.GetDirectoryName(common) ?? common;
                if (Path.GetPathRoot(common) == common)
                {
                    break;
                }
            }
        }

        return common;
    }
}
