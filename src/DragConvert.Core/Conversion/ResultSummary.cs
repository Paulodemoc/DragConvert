namespace DragConvert.Core.Conversion;

/// <summary>The files a drop on one target will convert, and the ones it skips because they can't become that target.</summary>
public sealed record DropPlan(MediaFormat Target, IReadOnlyList<string> Convert, IReadOnlyList<string> Skipped);

public sealed record NotificationText(string Title, string Body);

/// <summary>Wording for the one summary notification shown after a drop.</summary>
public static class ResultSummary
{
    private const int NamesShown = 3;

    public static NotificationText Describe(MediaFormat target, IReadOnlyList<ConversionResult> results, IReadOnlyList<string> skipped)
    {
        var succeeded = results.Where(r => r.Status == ConversionStatus.Succeeded).ToList();
        var failed = results.Where(r => r.Status == ConversionStatus.Failed).ToList();
        int cancelled = results.Count(r => r.Status == ConversionStatus.Cancelled);
        var lines = new List<string>();

        string title;
        if (cancelled > 0)
        {
            title = "Conversion cancelled";
            lines.Add($"{succeeded.Count} of {results.Count} {Files(results.Count)} converted to {target.DisplayName} before cancelling.");
        }
        else if (failed.Count == 0)
        {
            title = succeeded.Count == 1 ? $"Converted to {target.DisplayName}" : $"{succeeded.Count} files converted to {target.DisplayName}";
            if (succeeded.Count == 1)
            {
                lines.Add(Path.GetFileName(succeeded[0].OutputPath!));
            }
        }
        else if (succeeded.Count == 0 && failed.Count == 1)
        {
            title = $"Couldn't convert {Path.GetFileName(failed[0].InputPath)}";
            lines.Add(failed[0].Error ?? "Unknown error.");
        }
        else
        {
            title = succeeded.Count == 0
                ? $"{failed.Count} files failed to convert to {target.DisplayName}"
                : $"{succeeded.Count} converted to {target.DisplayName}, {failed.Count} failed";
            lines.Add(string.Join("; ", failed.Take(NamesShown).Select(f => $"{Path.GetFileName(f.InputPath)}: {f.Error}")) + More(failed.Count));
        }

        if (skipped.Count > 0)
        {
            lines.Add($"Skipped {skipped.Count} {Files(skipped.Count)} that can't become {target.DisplayName}: {Names(skipped)}");
        }

        return new NotificationText(title, string.Join(Environment.NewLine, lines));
    }

    /// <summary>Wording for a tool run: "Compressed 3 files", "Couldn't merge: reason".</summary>
    public static NotificationText DescribeTool(string toolName, IReadOnlyList<DragConvert.Core.Tools.ToolResult> results, IReadOnlyList<string> skipped)
    {
        var succeeded = results.Where(r => r.Status == ConversionStatus.Succeeded).ToList();
        var failed = results.Where(r => r.Status == ConversionStatus.Failed).ToList();
        int cancelled = results.Count(r => r.Status == ConversionStatus.Cancelled);
        var lines = new List<string>();

        string title;
        if (cancelled > 0)
        {
            title = $"{toolName} cancelled";
            lines.Add($"{succeeded.Count} of {results.Count} done before cancelling.");
        }
        else if (failed.Count == 0)
        {
            title = succeeded.Count == 1 ? $"{toolName}: done" : $"{toolName}: {succeeded.Count} done";
            if (succeeded.Count == 1)
            {
                lines.Add(Path.GetFileName(succeeded[0].OutputPath!));
            }
        }
        else if (succeeded.Count == 0 && failed.Count == 1)
        {
            title = $"{toolName} failed";
            lines.Add(failed[0].Error ?? "Unknown error.");
        }
        else
        {
            title = $"{toolName}: {succeeded.Count} done, {failed.Count} failed";
            lines.Add(string.Join("; ", failed.Take(NamesShown).Select(f => $"{Path.GetFileName(f.Inputs[0])}: {f.Error}")) + More(failed.Count));
        }

        if (skipped.Count > 0)
        {
            lines.Add($"Skipped {skipped.Count} {Files(skipped.Count)} it doesn't apply to: {Names(skipped)}");
        }

        return new NotificationText(title, string.Join(Environment.NewLine, lines));
    }

    /// <summary>Shown instead of a conversion when nothing in the drop can become the chosen target (or anything at all).</summary>
    public static NotificationText NothingToConvert(IReadOnlyList<string> files, MediaFormat? target = null) =>
        files.Count == 0
            ? new NotificationText("Nothing to convert", "The drop didn't contain any files.")
            : new NotificationText(
                "Nothing to convert",
                target is null
                    ? $"DragConvert can't convert {Names(files)}."
                    : $"None of the {files.Count} {Files(files.Count)} can become {target.DisplayName}.");

    private static string Files(int count) => count == 1 ? "file" : "files";

    private static string Names(IReadOnlyList<string> paths) => string.Join(", ", paths.Take(NamesShown).Select(Path.GetFileName)) + More(paths.Count);

    private static string More(int count) => count > NamesShown ? $" and {count - NamesShown} more" : string.Empty;
}
