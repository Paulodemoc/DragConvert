using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using DragConvert.App.Conversion;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;
using DragConvert.Core.Input;
using DragConvert.Core.Overlay;
using DragConvert.Core.Tools;
using DragConvert.Native;
using DragConvert.Native.Input;
using DragConvert.Native.Windowing;
using Serilog;

namespace DragConvert.App.Overlay;

/// <summary>
/// Ties the mouse hook, key polling, overlay and conversions together on the UI thread. Shows the overlay while a
/// drag is active and the trigger chord is held, fills its tiles from the format catalog once the drag's files are
/// known, and hands a drop on a tile to the <see cref="ConversionCoordinator"/>.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<OverlayController>();
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan DropWait = TimeSpan.FromMilliseconds(500);

    // Matches the coordinator's progress delay: slower batches hand over to the progress notification.
    private static readonly TimeSpan BubbleLimit = TimeSpan.FromSeconds(1.5);

    private readonly Dispatcher _dispatcher;
    private readonly ConversionCoordinator _coordinator;
    private OverlayWindow _overlay;
    private readonly MouseHookThread _hook;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _dropWait;
    private bool _dragActive;
    private bool _escaped;
    private bool _visible;
    private int _showCount;
    private int _bubble;

    // State for the drag currently over the overlay.
    private CancellationTokenSource? _dragCts;
    private IReadOnlyList<string> _dragPaths = [];
    private DropSet? _dragSet;
    private IReadOnlyList<TargetOption> _options = [];
    private IReadOnlyList<ToolOption> _toolOptions = [];
    private bool _toolsMode;

    public OverlayController(Dispatcher dispatcher, OverlayRenderer renderer, ConversionCoordinator coordinator, ModifierChord chord)
    {
        _dispatcher = dispatcher;
        _coordinator = coordinator;
        Renderer = renderer;
        Chord = chord;
        _overlay = CreateOverlay(renderer);
        _hook = new MouseHookThread(OnHookSignal);
        _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Input, (_, _) => Evaluate(Stopwatch.GetTimestamp(), fromHook: false), dispatcher);
        _poll.Stop();
        _dropWait = new DispatcherTimer(DropWait, DispatcherPriority.Normal, (_, _) => Hide("no drop within 500 ms of release"), dispatcher);
        _dropWait.Stop();
    }

    public OverlayRenderer Renderer { get; private set; }

    /// <summary>The trigger chord; <see cref="Modifiers.None"/> means the overlay never shows.</summary>
    public ModifierChord Chord { get; set; }

    /// <summary>When paused, drags are ignored (the hook stays installed but does nothing).</summary>
    public bool Paused { get; set; }

    public void Start()
    {
        PrepareOverlay();
        _hook.Start();
        if (_hook.IsInstalled)
        {
            Logger.Information("Mouse hook installed; drag threshold {X}x{Y} px; trigger {Trigger}", DragMetrics.ThresholdX, DragMetrics.ThresholdY, Chord.Trigger);
        }
        else
        {
            Logger.Error("Mouse hook failed to install (Win32 error {Error})", _hook.InstallError);
        }
    }

    /// <summary>Switches the overlay style immediately: the window is rebuilt and warmed up again.</summary>
    public void SwitchRenderer(OverlayRenderer renderer)
    {
        if (renderer == Renderer)
        {
            return;
        }

        _dropWait.Stop();
        _visible = false;
        _bubble++;
        OverlayWindow old = _overlay;
        old.DragEntered -= OnDragEntered;
        old.TileDropped -= OnTileDropped;
        old.Close();

        Renderer = renderer;
        _overlay = CreateOverlay(renderer);
        PrepareOverlay();
    }

    public void Dispose()
    {
        _poll.Stop();
        _dropWait.Stop();
        _dragCts?.Cancel();
        _dragCts?.Dispose();
        _hook.Dispose();
        _overlay.Close();
    }

    private OverlayWindow CreateOverlay(OverlayRenderer renderer)
    {
        var overlay = new OverlayWindow(renderer);
        overlay.DragEntered += OnDragEntered;
        overlay.TileDropped += OnTileDropped;
        return overlay;
    }

    private void PrepareOverlay()
    {
        long prepareStart = Stopwatch.GetTimestamp();
        _overlay.Prepare();
        Logger.Information("Overlay created hidden in {Ms:F1} ms; style {Renderer}", Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds, Renderer);

        long warmStart = Stopwatch.GetTimestamp();
        _overlay.WarmUp(() => Logger.Information("Overlay warm-up render done in {Ms:F1} ms; {Memory}", Stopwatch.GetElapsedTime(warmStart).TotalMilliseconds, MemorySummary()));
    }

    private static string MemorySummary()
    {
        using var process = Process.GetCurrentProcess();
        return $"working set {process.WorkingSet64 / (1024.0 * 1024):F1} MB, private {process.PrivateMemorySize64 / (1024.0 * 1024):F1} MB";
    }

    private static string Describe(DropSet set)
    {
        if (set.Files.Count == 1 && set.FolderCount == 0)
        {
            return Path.GetFileName(set.Files[0]);
        }

        string files = $"{set.Files.Count:N0}{(set.Truncated ? "+" : string.Empty)} file{(set.Files.Count == 1 ? string.Empty : "s")}";
        return set.FolderCount == 0 ? files : $"{files} in {set.FolderCount} folder{(set.FolderCount == 1 ? string.Empty : "s")}";
    }

    /// <summary>Runs on the hook thread: only posts to the UI thread.</summary>
    private void OnHookSignal(DragSignal signal) => _dispatcher.InvokeAsync(() => HandleSignal(signal), DispatcherPriority.Send);

    private void HandleSignal(DragSignal signal)
    {
        if (Paused)
        {
            return;
        }

        switch (signal.Transition)
        {
            case DragTransition.Started:
                _dragActive = true;
                _escaped = false;
                _poll.Start();
                Evaluate(signal.Timestamp, fromHook: true);
                break;

            case DragTransition.Ended:
                _dragActive = false;
                if (_visible)
                {
                    if (CursorInOverlay())
                    {
                        // The hook sees the release before the drop reaches us; wait for it instead of hiding now.
                        _dropWait.Start();
                    }
                    else
                    {
                        Hide("released outside the overlay");
                    }
                }
                else
                {
                    _poll.Stop();
                }

                break;
        }
    }

    private void Evaluate(long timestamp, bool fromHook)
    {
        KeyState keys = KeyStateReader.Read();

        if (keys.Escape)
        {
            if (_visible)
            {
                Hide("Esc");
            }

            _escaped = true;
        }

        if (!_dragActive)
        {
            if (!_visible)
            {
                _poll.Stop();
            }

            return;
        }

        bool held = Chord.IsTriggerHeld(keys);
        bool toolsMode = Chord.IsToolsMode(keys);
        if (!_visible)
        {
            if (held && !_escaped)
            {
                _toolsMode = toolsMode;
                Show(timestamp, fromHook);
            }
        }
        else if (!held && !CursorInOverlay())
        {
            Hide("trigger released");
        }
        else if (held && toolsMode != _toolsMode)
        {
            // Alt pressed or released while the ring is up: swap formats and tools.
            _toolsMode = toolsMode;
            ShowTiles();
        }
    }

    private void Show(long triggerTimestamp, bool fromHook)
    {
        var (x, y) = OverlayWindowInterop.GetCursorPosition();
        ResetDragState();
        _bubble++;
        _overlay.ShowAt(x, y);
        _visible = true;
        int showNumber = ++_showCount;

        void OnFirstFrame(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFirstFrame;
            double firstFrameMs = Stopwatch.GetElapsedTime(triggerTimestamp).TotalMilliseconds;
            Logger.Information(
                "Overlay shown #{Count} at {X},{Y}: {FirstFrame:F1} ms to first frame (from {Origin}); {Memory}{Warning}",
                showNumber,
                x,
                y,
                firstFrameMs,
                fromHook ? "drag start" : "key poll",
                MemorySummary(),
                firstFrameMs > 150 ? " — OVER the 150 ms target" : string.Empty);
        }

        CompositionTarget.Rendering += OnFirstFrame;
    }

    private void ResetDragState()
    {
        _dragCts?.Cancel();
        _dragCts?.Dispose();
        _dragCts = new CancellationTokenSource();
        _dragPaths = [];
        _dragSet = null;
        _options = [];
        _toolOptions = [];
    }

    private async void OnDragEntered(object? sender, DragPayload payload)
    {
        _dragPaths = payload.Paths;
        if (payload.Paths.Count == 0)
        {
            _overlay.View.SetCaption(payload.VirtualFileNames.Count > 0 ? "Attachments can't be converted yet" : "Nothing here can be converted");
            return;
        }

        CancellationToken token = _dragCts?.Token ?? CancellationToken.None;
        if (!payload.Paths.All(File.Exists))
        {
            _overlay.View.SetCaption("Reading folders…");
        }

        try
        {
            DropSet set = await ConversionCoordinator.ExpandAsync(payload.Paths, token);
            if (token.IsCancellationRequested || !_visible)
            {
                return;
            }

            _dragSet = set;
            _options = _coordinator.Catalog.TargetOptionsFor(set.Files);
            _toolOptions = _coordinator.Tools.ToolOptionsFor(set.Files);
            Logger.Information(
                "Drag has {Description}; {Count} target(s): {Targets}; {ToolCount} tool(s)",
                Describe(set),
                _options.Count,
                _options.Select(o => $"{o.Target.DisplayName} {o.FileCount}/{o.TotalFiles}"),
                _toolOptions.Count);

            MediaCategory[] categories = set.Files.Select(f => MediaFormats.FromPath(f)?.Category).OfType<MediaCategory>().Distinct().ToArray();
            _overlay.View.SetBadge(categories.Length == 1 ? categories[0] : null, set.Files.Count);
            ShowTiles();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(ex, "Reading the dragged files failed");
            _overlay.View.SetCaption("Couldn't read the dragged files");
        }
    }

    /// <summary>Puts the formats (or, with Alt held, the tools) for the current drag on the ring.</summary>
    private void ShowTiles()
    {
        if (_dragSet is not { } set)
        {
            return;
        }

        string files = Describe(set);
        if (_toolsMode)
        {
            if (_toolOptions.Count == 0)
            {
                _overlay.View.SetTiles([], "No tools for these files");
                return;
            }

            _overlay.View.SetTiles(
                _toolOptions.Take(OverlayModel.MaxTiles)
                    .Select(o => new OverlayTile(o.Tool.DisplayName, o.AppliesToAll ? null : $"{o.FileCount} of {o.TotalFiles}", MediaCategory.Document, o.Tool.Glyph))
                    .ToList(),
                $"Tools · {files}");
            return;
        }

        if (_options.Count == 0)
        {
            _overlay.View.SetTiles([], set.Files.Count == 0 ? "No files in this drag" : _toolOptions.Count > 0 ? "No conversions · hold Alt for tools" : "Nothing here can be converted");
            return;
        }

        string summary = files;
        if (_options.Count > OverlayModel.MaxTiles)
        {
            summary += $" · {_options.Count - OverlayModel.MaxTiles} more in the tray window";
        }

        if (_toolOptions.Count > 0)
        {
            summary += " · Alt for tools";
        }

        _overlay.View.SetTiles(
            _options.Take(OverlayModel.MaxTiles)
                .Select(o => new OverlayTile(o.Target.DisplayName, o.AppliesToAll ? null : $"{o.FileCount} of {o.TotalFiles}", o.Target.Category))
                .ToList(),
            summary);
    }

    private async void OnTileDropped(object? sender, int tile)
    {
        int count = _toolsMode ? _toolOptions.Count : _options.Count;
        if (tile < 0 || tile >= count)
        {
            Hide("dropped outside the tiles");
            return;
        }

        Task<BatchOutcome> batch;
        if (_toolsMode)
        {
            IFileTool tool = _toolOptions[tile].Tool;
            Logger.Information("Dropped on tool {Tool}", tool.Id);
            batch = _coordinator.RunToolAsync(_dragPaths, tool, _dragSet, quietIfFast: true);
        }
        else
        {
            MediaFormat target = _options[tile].Target;
            Logger.Information("Dropped on {Target}", target);
            batch = _coordinator.ConvertAsync(_dragPaths, target, _dragSet, quietIfFast: true);
        }

        // The overlay turns into a status bubble at the dropped tile: spinner, then a check mark or a cross.
        _dropWait.Stop();
        _visible = false;
        _dragCts?.Cancel();
        if (!_dragActive)
        {
            _poll.Stop();
        }

        int bubble = ++_bubble;
        _overlay.ShowWorking(tile);

        if (await Task.WhenAny(batch, Task.Delay(BubbleLimit)) != batch)
        {
            // Slow batch: the progress notification takes over.
            if (bubble == _bubble)
            {
                _overlay.FadeOut();
            }

            return;
        }

        BatchOutcome outcome = await batch;
        if (bubble != _bubble)
        {
            return;
        }

        if (outcome == BatchOutcome.Declined)
        {
            _overlay.FadeOut();
            return;
        }

        _overlay.View.ShowResult(outcome.AllSucceeded);
        await Task.Delay(OverlayTheme.Duration("Motion.ResultHoldMs"));
        if (bubble == _bubble)
        {
            _overlay.FadeOut();
        }
    }

    private void Hide(string reason)
    {
        _dropWait.Stop();
        if (!_visible)
        {
            return;
        }

        _overlay.FadeOut();
        _visible = false;
        _dragCts?.Cancel();
        if (!_dragActive)
        {
            _poll.Stop();
        }

        Logger.Information("Overlay hidden: {Reason}", reason);
    }

    private bool CursorInOverlay()
    {
        var (x, y) = OverlayWindowInterop.GetCursorPosition();
        return OverlayWindowInterop.GetWindowRect(_overlay.Handle).Contains(x, y);
    }
}
