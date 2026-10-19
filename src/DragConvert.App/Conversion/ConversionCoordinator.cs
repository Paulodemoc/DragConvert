using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using DragConvert.App.Notifications;
using DragConvert.Core.Conversion;
using DragConvert.Core.Settings;
using Serilog;

namespace DragConvert.App.Conversion;

internal sealed record RecentConversion(string InputPath, string OutputPath, MediaFormat Target, DateTime Finished)
{
    public string Description => $"{Path.GetFileName(InputPath)} → {Target.DisplayName}";
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
    private readonly NotificationService _notifications;
    private readonly Func<AppSettings> _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, CancellationTokenSource> _running = [];

    public ConversionCoordinator(FormatCatalog catalog, NotificationService notifications, Func<AppSettings> settings, Dispatcher dispatcher)
    {
        Catalog = catalog;
        _queue = new JobQueue(catalog);
        _notifications = notifications;
        _settings = settings;
        _dispatcher = dispatcher;
        _notifications.ActionInvoked += OnNotificationAction;
    }

    public FormatCatalog Catalog { get; }

    /// <summary>Most recent successful conversions first (UI thread only).</summary>
    public ObservableCollection<RecentConversion> Recent { get; } = [];

    /// <summary>Resolves dropped paths to files. Plain files resolve instantly; folders are walked off the UI thread.</summary>
    public static Task<DropSet> ExpandAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        paths.All(File.Exists)
            ? Task.FromResult(new DropSet(paths, 0, Truncated: false))
            : Task.Run(() => DropExpander.Expand(paths, cancellationToken: cancellationToken), cancellationToken);

    public async Task ConvertAsync(IReadOnlyList<string> droppedPaths, MediaFormat target, DropSet? expanded = null)
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
                return;
            }

            if (plan.Convert.Count > ConfirmThreshold)
            {
                bool confirmed = await _notifications.ConfirmAsync(
                    new NotificationText($"Convert {plan.Convert.Count} files to {target.DisplayName}?", "Outputs are saved beside each original."),
                    $"Convert {plan.Convert.Count}");
                if (!confirmed)
                {
                    Logger.Information("Large batch declined");
                    return;
                }
            }

            await RunAsync(plan, set.Truncated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Error(ex, "Drop failed");
            _notifications.Show(new NotificationText("Couldn't read the dropped files", ex.Message));
        }
    }

    public void Dispose() => _queue.Dispose();

    private async Task RunAsync(DropPlan plan, bool truncated)
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
                Recent.Insert(0, new RecentConversion(ok.InputPath, ok.OutputPath!, ok.Target, DateTime.Now));
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

        // Same tag replaces the progress notification, if one was shown.
        _notifications.Show(text, actions, progressShown ? tag : null);
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
            case "open" when File.Exists(argument):
                Shell.Open(argument);
                break;
            case "reveal" when File.Exists(argument):
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
