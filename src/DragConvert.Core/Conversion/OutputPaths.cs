namespace DragConvert.Core.Conversion;

/// <summary>Output naming: a new file beside the original, never overwriting, written via a temp file and renamed.</summary>
public static class OutputPaths
{
    /// <summary>Marks temp files so a crashed run's leftovers can be found and removed at startup.</summary>
    public const string TempMarker = ".dragconvert-tmp";

    /// <summary><c>&lt;name&gt;.&lt;ext&gt;</c> in the input's folder (or <paramref name="outputDirectory"/>); on collision <c>&lt;name&gt; (1).&lt;ext&gt;</c>, and so on.</summary>
    public static string PlanFinal(string inputPath, MediaFormat target, string? outputDirectory = null) =>
        PlanFinal(inputPath, string.Empty, target.Extension, outputDirectory);

    /// <summary>
    /// <c>&lt;name&gt;&lt;suffix&gt;&lt;extension&gt;</c> in the input's folder (or <paramref name="outputDirectory"/>), e.g.
    /// "report (compressed).pdf" or the folder "report pages"; on collision a counter is added before the extension.
    /// </summary>
    public static string PlanFinal(string inputPath, string suffix, string extension, string? outputDirectory = null)
    {
        string directory = outputDirectory ?? Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath) + suffix;
        string candidate = Path.Combine(directory, name + extension);
        for (int n = 1; File.Exists(candidate) || Directory.Exists(candidate); n++)
        {
            candidate = Path.Combine(directory, $"{name} ({n}){extension}");
        }

        return candidate;
    }

    /// <summary>A hidden-style temp name in the same folder that keeps the target extension, so tools infer the format.</summary>
    public static string TempFor(string finalPath)
    {
        string directory = Path.GetDirectoryName(finalPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(finalPath);
        string random = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(directory, $".{name}.{random}{TempMarker}{Path.GetExtension(finalPath)}");
    }

    public static bool IsTempFile(string path) => Path.GetFileName(path).Contains(TempMarker, StringComparison.OrdinalIgnoreCase);
}
