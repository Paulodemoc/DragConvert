using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using DragConvert.App.Notifications;
using DragConvert.Core.Conversion;
using DragConvert.Core.Settings;
using DragConvert.Core.Tools;
using Serilog;

namespace DragConvert.App.Conversion;

/// <summary>How a drop ended, for the overlay's check mark or cross.</summary>
internal sealed record BatchOutcome(int Succeeded, int Failed, int Cancelled, bool Notified)
{
    public static readonly BatchOutcome Nothing = new(0, 0, 0, Notified: true);

    public static readonly BatchOutcome Declined = new(0, 0, 0, Notified: false);

    public bool AllSucceeded => Succeeded > 0 && Failed == 0 && Cancelled == 0;
}

/// <summary>A finished conversion or tool run; <see cref="Action"/> is the target format or the tool's name.</summary>
internal sealed record RecentConversion(string InputPath, string OutputPath, string Action, DateTime Finished)
{
    public string Description => $"{Path.GetFileName(InputPath)} → {Action}";
}

/// <summary>
/// Runs a drop from start to finish: expands folders, plans which files fit the target, asks before very large
/// batches, converts through the <see cref="JobQueue"/>, shows progress for slow jobs and one summary notification.
/// </summary>
internal sealed class ConversionCoordinator : IDisposable
{
    /// <summary>Above this many files the user confirms before the batch starts.</summary>
    public const int ConfirmThreshold = 200;

    private static readonly ILogger Logger = Log.ForContext<ConversionCoordinator>();
    private static readonly TimeSpan ProgressDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    private readonly JobQueue _queue;
    private readonly ToolRunner _tools;
    private readonly NotificationService _notifications;
    private readonly Func<AppSettings> _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, CancellationTokenSource> _running = [];

    public ConversionCoordinator(FormatCatalog catalog, ToolCatalog tools, NotificationService notifications, Func<AppSettings> settings, Dispatcher dispatcher, ITempTracker? temps = null)
    {
        Catalog = catalog;
        Tools = tools;
        _queue = new JobQueue(catalog, temps: temps);
        _tools = new ToolRunner(temps: temps);
        _notifications = notifications;
        _settings = settings;
        _dispatcher = dispatcher;
        _notifications.ActionInvoked += OnNotificationAction;
    }

    public FormatCatalog Catalog { get; }

    public ToolCatalog Tools { get; }

    /// <summary>True while any conversion or tool run is in progress.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_running)
            {
                return _running.Count > 0;
            }
        }
    }

    /// <summary>Most recent successful conversions first (UI thread only).</summary>
    public ObservableCollection<RecentConversion> Recent { get; } = [];

    /// <summary>Resolves dropped paths to files. Plain files resolve instantly; folders are walked off the UI thread.</summary>
    public static Task<DropSet> ExpandAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        paths.All(File.Exists)
            ? Task.FromResult(new DropSet(paths, 0, Truncated: false))
            : Task.Run(() => DropExpander.Expand(paths, cancellationToken: cancellationToken), cancellationToken);

    /// <param name="quietIfFast">
    /// For overlay drops: a batch that succeeds before the progress delay shows no notification, because the overlay's
    /// check mark already confirms it. Failures always notify.
    /// </param>
    public async Task<BatchOutcome> ConvertAsync(IReadOnlyList<string> droppedPaths, MediaFormat target, DropSet? expanded = null, bool quietIfFast = false)
    {
        try
        {
            DropSet set = expanded ?? await ExpandAsync(droppedPaths);
            DropPlan plan = Catalog.Plan(set.Files, target);
            Logger.Information(
                "Drop on {Target}: {Files} file(s) from {Folders} folder(s); converting {Convert}, skipping {Skip}{Truncated}",
                target,
                set.Files.Count,
                set.FolderCount,
                plan.Convert.Count,
                plan.Skipped.Count,
                set.Truncated ? " (folder walk truncated)" : string.Empty);

            if (plan.Convert.Count == 0)
            {
                _notifications.Show(ResultSummary.NothingToConvert(set.Files, target));
                return BatchOutcome.Nothing;
            }

            if (plan.Convert.Count > ConfirmThreshold)
            {
                bool confirmed = await _notifications.ConfirmAsync(
                    new NotificationText($"Convert {plan.Convert.Count} files to {target.DisplayName}?", "Outputs are saved beside each original."),
                    $"Convert {plan.Convert.Count}");
                if (!confirmed)
                {
                    Logger.Information("Large batch declined");
                    return BatchOutcome.Declined;
                }
            }

            return await RunAsync(plan, set.Truncated, quietIfFast);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error(ex, "Drop failed");
            _notifications.Show(new NotificationText("Couldn't read the dropped files", ex.Message));
            return BatchOutcome.Nothing;
        }
    }

    /// <summary>Runs a tool on a drop, with the same confirmation, progress and summary behaviour as conversions.</summary>
    public async Task<BatchOutcome> RunToolAsync(IReadOnlyList<string> droppedPaths, IFileTool tool, DropSet? expanded = null, bool quietIfFast = false)
    {
        try
        {
            DropSet set = expanded ?? await ExpandAsync(droppedPaths);
            var (accepted, skipped) = ToolCatalog.Plan(tool, set.Files);
            Logger.Information("Tool {Tool}: {Files} file(s); using {Accepted}, skipping {Skipped}", tool.Id, set.Files.Count, accepted.Count, skipped.Count);

            if (accepted.Count < (tool.CombinesInputs ? 2 : 1))
            {
                _notifications.Show(new NotificationText(
                    $"{tool.DisplayName}: nothing to do",
                    tool.CombinesInputs ? $"{tool.DisplayName} needs at least two matching files." : $"{tool.DisplayName} doesn't apply to these files."));
                return BatchOutcome.Nothing;
            }

            if (!tool.CombinesInputs && accepted.Count > ConfirmThreshold
                && !await _notifications.ConfirmAsync(new NotificationText($"{tool.DisplayName}: {accepted.Count} files?", "Results are saved beside each original."), "Go ahead"))
            {
                return BatchOutcome.Declined;
            }

            return await RunToolBatchAsync(tool, accepted, skipped, quietIfFast);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error(ex, "Tool run failed");
            _notifications.Show(new NotificationText("Couldn't read the dropped files", ex.Message));
            return BatchOutcome.Nothing;
        }
    }

    public void Dispose()
    {
        _queue.Dispose();
        _tools.Dispose();
    }

    private async Task<BatchOutcome> RunToolBatchAsync(IFileTool tool, IReadOnlyList<string> accepted, IReadOnlyList<string> skipped, bool quietIfFast)
    {
        string tag = Guid.NewGuid().ToString("N")[..12];
        using var cts = new CancellationTokenSource();
        lock (_running)
        {
            _running[tag] = cts;
        }

        var stopwatch = Stopwatch.StartNew();
        double fraction = 0;
        Task<IReadOnlyList<ToolResult>> run = _tools.RunAsync(tool, accepted, new InlineProgress(p => Volatile.Write(ref fraction, p)), _settings().OutputDirectory, cts.Token);

        bool progressShown = false;
        uint sequence = 1;
        if (await Task.WhenAny(run, Task.Delay(ProgressDelay)) != run)
        {
            progressShown = true;
            string what = accepted.Count == 1 || tool.CombinesInputs ? Path.GetFileName(accepted[0]) + (accepted.Count > 1 ? $" and {accepted.Count - 1} more" : string.Empty) : $"{accepted.Count} files";
            _notifications.ShowProgress(tag, tool.DisplayName, what, [new NotificationAction("Cancel", "cancel", tag)]);
            while (await Task.WhenAny(run, Task.Delay(ProgressInterval)) != run)
            {
                _notifications.UpdateProgress(tag, Volatile.Read(ref fraction), "Working…", ++sequence);
            }
        }

        IReadOnlyList<ToolResult> results = await run;
        lock (_running)
        {
            _running.Remove(tag);
        }

        Logger.Information(
            "Tool {Tool} finished in {Seconds:F1} s: {Ok} succeeded, {Failed} failed",
            tool.Id,
            stopwatch.Elapsed.TotalSeconds,
            results.Count(r => r.Status == ConversionStatus.Succeeded),
            results.Count(r => r.Status == ConversionStatus.Failed));
        foreach (ToolResult failure in results.Where(r => r.Status == ConversionStatus.Failed))
        {
            Logger.Warning("Tool {Tool} failed on {Inputs}: {Error}", tool.Id, failure.Inputs, failure.Error);
        }

        var outputs = results.Where(r => r.Status == ConversionStatus.Succeeded).ToList();
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (ToolResult ok in outputs)
            {
                Recent.Insert(0, new RecentConversion(ok.Inputs[0], ok.OutputPath!, tool.DisplayName, DateTime.Now));
            }

            while (Recent.Count > 10)
            {
                Recent.RemoveAt(Recent.Count - 1);
            }
        });

        var outcome = new BatchOutcome(
            outputs.Count,
            results.Count(r => r.Status == ConversionStatus.Failed),
            results.Count(r => r.Status == ConversionStatus.Cancelled),
            Notified: true);
        if (quietIfFast && !progressShown && outcome.AllSucceeded && skipped.Count == 0)
        {
            return outcome with { Notified = false };
        }

        var actions = new List<NotificationAction>();
        if (outputs.Count == 1)
        {
            actions.Add(new NotificationAction(tool.OutputKind == ToolOutputKind.Folder ? "Open folder" : "Open", "open", outputs[0].OutputPath!));
        }

        if (outputs.Count > 0)
        {
            actions.Add(new NotificationAction("Show in folder", "reveal", outputs[0].OutputPath!));
        }

        if (outcome.Failed > 0)
        {
            actions.Add(new NotificationAction("Open log", "log"));
        }

        _notifications.Show(ResultSummary.DescribeTool(tool.DisplayName, results, skipped), actions, progressShown ? tag : null);
        return outcome;
    }

    private async Task<BatchOutcome> RunAsync(DropPlan plan, bool truncated, bool quietIfFast)
    {
        string tag = Guid.NewGuid().ToString("N")[..12];
        using var cts = new CancellationTokenSource();
        lock (_running)
        {
            _running[tag] = cts;
        }

        var stopwatch = Stopwatch.StartNew();
        var fractions = new double[plan.Convert.Count];
        int done = 0;
        string? outputDirectory = _settings().OutputDirectory;

        Task<ConversionResult>[] jobs = plan.Convert
            .Select((file, i) => _queue.ConvertAsync(
                file,
                plan.Target,
                progress: new InlineProgress(p => Volatile.Write(ref fractions[i], p)),
                outputDirectory: outputDirectory,
                cancellationToken: cts.Token)
                .ContinueWith(
                    t =>
                    {
                        Interlocked.Increment(ref done);
                        return t.Result;
                    },
                    TaskScheduler.Default))
            .ToArray();
        Task<ConversionResult[]> all = Task.WhenAll(jobs);

        // Fast jobs finish before this delay and only get the result notification.
        bool progressShown = false;
        uint sequence = 1;
        if (await Task.WhenAny(all, Task.Delay(ProgressDelay)) != all)
        {
            progressShown = true;
            string what = plan.Convert.Count == 1 ? Path.GetFileName(plan.Convert[0]) : $"{plan.Convert.Count} files";
            _notifications.ShowProgress(tag, $"Converting to {plan.Target.DisplayName}", what, [new NotificationAction("Cancel", "cancel", tag)]);
            while (await Task.WhenAny(all, Task.Delay(ProgressInterval)) != all)
            {
                double overall = fractions.Sum(f => Math.Clamp(f, 0, 1)) / fractions.Length;
                string status = plan.Convert.Count == 1 ? "Converting…" : $"{Volatile.Read(ref done)} of {plan.Convert.Count} done";
                _notifications.UpdateProgress(tag, overall, status, ++sequence);
            }
        }

        ConversionResult[] results = await all;
        lock (_running)
        {
            _running.Remove(tag);
        }

        Logger.Information(
            "Batch to {Target} finished in {Seconds:F1} s: {Ok} succeeded, {Failed} failed, {Cancelled} cancelled",
            plan.Target,
            stopwatch.Elapsed.TotalSeconds,
            results.Count(r => r.Status == ConversionStatus.Succeeded),
            results.Count(r => r.Status == ConversionStatus.Failed),
            results.Count(r => r.Status == ConversionStatus.Cancelled));
        foreach (ConversionResult failure in results.Where(r => r.Status == ConversionStatus.Failed))
        {
            Logger.Warning("Failed {Input} -> {Target}: {Error}", failure.InputPath, failure.Target, failure.Error);
        }

        var outputs = results.Where(r => r.Status == ConversionStatus.Succeeded).Select(r => r.OutputPath!).ToList();
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (ConversionResult ok in results.Where(r => r.Status == ConversionStatus.Succeeded))
            {
                Recent.Insert(0, new RecentConversion(ok.InputPath, ok.OutputPath!, ok.Target.DisplayName, DateTime.Now));
            }

            while (Recent.Count > 10)
            {
                Recent.RemoveAt(Recent.Count - 1);
            }
        });

        NotificationText text = ResultSummary.Describe(plan.Target, results, plan.Skipped);
        if (truncated)
        {
            text = text with { Body = string.Join(Environment.NewLine, text.Body, $"Only the first {DropExpander.DefaultMaxFiles:N0} files in the folders were included.").Trim() };
        }

        var actions = new List<NotificationAction>();
        if (outputs.Count == 1)
        {
            actions.Add(new NotificationAction("Open", "open", outputs[0]));
        }

        if (outputs.Count > 0)
        {
            actions.Add(new NotificationAction("Show in folder", "reveal", outputs[0]));
        }

        if (results.Any(r => r.Status == ConversionStatus.Failed))
        {
            actions.Add(new NotificationAction("Open log", "log"));
        }

        var outcome = new BatchOutcome(
            outputs.Count,
            results.Count(r => r.Status == ConversionStatus.Failed),
            results.Count(r => r.Status == ConversionStatus.Cancelled),
            Notified: true);
        if (quietIfFast && !progressShown && outcome.AllSucceeded && plan.Skipped.Count == 0 && !truncated)
        {
            Logger.Information("Fast batch: confirmed by the overlay's check mark instead of a notification");
            return outcome with { Notified = false };
        }

        // Same tag replaces the progress notification, if one was shown.
        _notifications.Show(text, actions, progressShown ? tag : null);
        return outcome;
    }

    private void OnNotificationAction(string verb, string argument)
    {
        switch (verb)
        {
            case "cancel":
                lock (_running)
                {
                    if (_running.TryGetValue(argument, out CancellationTokenSource? cts))
                    {
                        Logger.Information("Batch {Tag} cancelled from the notification", argument);
                        cts.Cancel();
                    }
                }

                break;
            case "open" when File.Exists(argument) || Directory.Exists(argument):
                Shell.Open(argument);
                break;
            case "reveal" when File.Exists(argument) || Directory.Exists(argument):
                Shell.Reveal(argument);
                break;
            case "log":
                Shell.Open(AppPaths.LogFolder);
                break;
        }
    }

    /// <summary>Progress&lt;T&gt; would post to the UI thread for every ffmpeg line; this just stores the value.</summary>
    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

internal static class Shell
{
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public static void Reveal(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
}
