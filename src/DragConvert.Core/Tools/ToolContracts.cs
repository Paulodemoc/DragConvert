using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tools;

public enum ToolOutputKind
{
    /// <summary>One file per run, e.g. "report (compressed).pdf".</summary>
    File,

    /// <summary>A folder of files per run, e.g. "report pages\".</summary>
    Folder,
}

/// <summary>
/// One run of a tool. <see cref="OutputPath"/> is a temporary file or folder in the destination folder; the tool
/// writes only there and never touches its inputs.
/// </summary>
public sealed record ToolJob(IReadOnlyList<string> Inputs, string OutputPath, IReadOnlyDictionary<string, string> Options);

/// <summary>
/// An action on files that isn't a plain format conversion: merge, split, compress, remove metadata. Shown when Alt is
/// held with the trigger, and in the tray window.
/// </summary>
public interface IFileTool
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Segoe Fluent Icons glyph for its tile.</summary>
    string Glyph { get; }

    bool IsAvailable { get; }

    /// <summary>True when all matching files go into one run with one output (merge); false for one run per file.</summary>
    bool CombinesInputs { get; }

    ToolOutputKind OutputKind { get; }

    /// <summary>Added to the first input's name: " (compressed)", " pages".</summary>
    string NameSuffix { get; }

    /// <summary>Fewest matching files a run needs: two for merging tools, one otherwise (or for "zip these").</summary>
    int MinimumInputs => CombinesInputs ? 2 : 1;

    /// <summary>True when the tool also takes files of unknown types (creating a ZIP works on anything).</summary>
    bool AcceptsAnyFile => false;

    bool Accepts(MediaFormat input);

    /// <summary>Extension of a file output for this input (ignored for folder outputs); input is null for unknown types.</summary>
    string OutputExtension(MediaFormat? input);

    /// <summary>Runs the tool. Throws <see cref="ConversionException"/> with a one-line reason on failure.</summary>
    Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>A tool offered for a set of files, and how many of them it applies to.</summary>
public sealed record ToolOption(IFileTool Tool, int FileCount, int TotalFiles)
{
    public bool AppliesToAll => FileCount == TotalFiles;
}

public sealed record ToolResult(IFileTool Tool, IReadOnlyList<string> Inputs, ConversionStatus Status, string? OutputPath, string? Error)
{
    public static ToolResult Success(IFileTool tool, IReadOnlyList<string> inputs, string output) => new(tool, inputs, ConversionStatus.Succeeded, output, null);

    public static ToolResult Failure(IFileTool tool, IReadOnlyList<string> inputs, string error) => new(tool, inputs, ConversionStatus.Failed, null, error);

    public static ToolResult Cancel(IFileTool tool, IReadOnlyList<string> inputs) => new(tool, inputs, ConversionStatus.Cancelled, null, null);
}
