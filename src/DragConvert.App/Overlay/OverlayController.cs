using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using DragConvert.Core.Input;
using DragConvert.Native;
using DragConvert.Native.Input;
using DragConvert.Native.Windowing;
using Serilog;

namespace DragConvert.App.Overlay;

/// <summary>
/// Ties the mouse hook, key polling and overlay together on the UI thread.
/// Shows the overlay while a drag is active and the trigger chord is held; hides it on release, Esc or drop.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<OverlayController>();
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan DropWait = TimeSpan.FromMilliseconds(500);

    private readonly Dispatcher _dispatcher;
    private readonly OverlayWindow _overlay;
    private readonly MouseHookThread _hook;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _dropWait;
    private readonly ModifierChord _chord = ModifierChord.Default;
    private bool _dragActive;
    private bool _escaped;
    private bool _visible;
    private int _showCount;

    public OverlayController(Dispatcher dispatcher, OverlayRenderer renderer)
    {
        _dispatcher = dispatcher;
        Renderer = renderer;
        _overlay = new OverlayWindow(renderer);
        _overlay.Dropped += OnDropped;
        _hook = new MouseHookThread(OnHookSignal);
        _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Input, (_, _) => Evaluate(Stopwatch.GetTimestamp(), fromHook: false), dispatcher);
        _poll.Stop();
        _dropWait = new DispatcherTimer(DropWait, DispatcherPriority.Normal, (_, _) => Hide("no drop within 500 ms of release"), dispatcher);
        _dropWait.Stop();
    }

    public OverlayRenderer Renderer { get; }

    public void Start()
    {
        long prepareStart = Stopwatch.GetTimestamp();
        _overlay.Prepare();
        Logger.Information(
            "Overlay created hidden in {Ms:F1} ms; renderer {Renderer}; backdrop applied {Backdrop}",
            Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds,
            Renderer,
            _overlay.BackdropApplied);

        long warmStart = Stopwatch.GetTimestamp();
        _overlay.WarmUp(() => Logger.Information("Overlay warm-up render done in {Ms:F1} ms; {Memory}", Stopwatch.GetElapsedTime(warmStart).TotalMilliseconds, MemorySummary()));

        _hook.Start();
        if (_hook.IsInstalled)
        {
            Logger.Information("Mouse hook installed; drag threshold {X}x{Y} px; trigger {Trigger}, tools {Tools}", DragMetrics.ThresholdX, DragMetrics.ThresholdY, _chord.Trigger, _chord.ToolsModifier);
        }
        else
        {
            Logger.Error("Mouse hook failed to install (Win32 error {Error})", _hook.InstallError);
        }
    }

    public void Dispose()
    {
        _poll.Stop();
        _dropWait.Stop();
        _hook.Dispose();
        _overlay.Close();
    }

    private static string MemorySummary()
    {
        using var process = Process.GetCurrentProcess();
        return $"working set {process.WorkingSet64 / (1024.0 * 1024):F1} MB, private {process.PrivateMemorySize64 / (1024.0 * 1024):F1} MB, managed {GC.GetTotalMemory(false) / (1024.0 * 1024):F1} MB";
    }

    /// <summary>Runs on the hook thread: only posts to the UI thread.</summary>
    private void OnHookSignal(DragSignal signal) => _dispatcher.InvokeAsync(() => HandleSignal(signal), DispatcherPriority.Send);

    private void HandleSignal(DragSignal signal)
    {
        switch (signal.Transition)
        {
            case DragTransition.Started:
                _dragActive = true;
                _escaped = false;
                Logger.Debug("Drag started at {X},{Y}; drag image visible {DragImage}", signal.X, signal.Y, OverlayWindowInterop.IsDragImageVisible());
                _poll.Start();
                Evaluate(signal.Timestamp, fromHook: true);
                break;

            case DragTransition.Ended:
                _dragActive = false;
                Logger.Debug("Drag ended at {X},{Y}", signal.X, signal.Y);
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

        bool held = _chord.IsTriggerHeld(keys);
        if (!_visible)
        {
            if (held && !_escaped)
            {
                Show(timestamp, _chord.IsToolsMode(keys), fromHook);
            }
        }
        else if (!held && !CursorInOverlay())
        {
            Hide("trigger released");
        }
        else if (held)
        {
            _overlay.SetToolsMode(_chord.IsToolsMode(keys));
        }
    }

    private void Show(long triggerTimestamp, bool toolsMode, bool fromHook)
    {
        var (x, y) = OverlayWindowInterop.GetCursorPosition();
        _overlay.ShowAt(x, y, toolsMode);
        _visible = true;
        int showNumber = ++_showCount;
        double showCallMs = Stopwatch.GetElapsedTime(triggerTimestamp).TotalMilliseconds;
        bool dragImage = OverlayWindowInterop.IsDragImageVisible();

        void OnFirstFrame(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFirstFrame;
            double firstFrameMs = Stopwatch.GetElapsedTime(triggerTimestamp).TotalMilliseconds;
            Logger.Information(
                "Overlay shown #{Count} at {X},{Y} ({Mode}): {FirstFrame:F1} ms to first frame, {ShowCall:F1} ms to Show(); measured from {Origin}; drag image visible {DragImage}; DPI scale {Dpi}; {Memory}{Warning}",
                showNumber,
                x,
                y,
                toolsMode ? "tools" : "formats",
                firstFrameMs,
                showCallMs,
                fromHook ? "drag start (trigger already held)" : "trigger detected by key poll (up to 10 ms after the press)",
                dragImage,
                VisualTreeHelperDpi(),
                MemorySummary(),
                firstFrameMs > 150 ? " — OVER the 150 ms target" : string.Empty);
        }

        CompositionTarget.Rendering += OnFirstFrame;
    }

    private double VisualTreeHelperDpi() => System.Windows.Media.VisualTreeHelper.GetDpi(_overlay).DpiScaleX;

    private void Hide(string reason)
    {
        _dropWait.Stop();
        if (!_visible)
        {
            return;
        }

        _overlay.Hide();
        _visible = false;
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

    private void OnDropped(object? sender, DropPayload payload)
    {
        if (payload.TileIndex >= 0)
        {
            Logger.Information(
                "DROP on tile {Tile} ({Label}): {PathCount} path(s) {Paths}; {VirtualCount} virtual file(s) {VirtualNames}; formats {Formats}",
                payload.TileIndex,
                payload.TileLabel,
                payload.Paths.Count,
                payload.Paths,
                payload.VirtualFileNames.Count,
                payload.VirtualFileNames,
                payload.Formats);
        }
        else
        {
            Logger.Information("Drop outside any tile (dead zone or panel edge); ignored");
        }

        Hide(payload.TileIndex >= 0 ? "dropped on a tile" : "dropped outside the tiles");
    }
}
