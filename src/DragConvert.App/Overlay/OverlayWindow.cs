using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;
using DragConvert.Native.DragDrop;
using DragConvert.Native.Windowing;
using Serilog;

namespace DragConvert.App.Overlay;

public enum OverlayRenderer
{
    /// <summary>"Ring": a themed round plate with square tiles.</summary>
    Layered,

    /// <summary>"Glass": a clear frosted disc (blurred copy of what's behind it) with round bubbles.</summary>
    Glass,

    /// <summary>"Segments": a ring split into one slice per option, on the frosted backdrop.</summary>
    Segments,
}

/// <summary>What the drag carries, read once when it first enters the overlay.</summary>
internal sealed record DragPayload(
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> VirtualFileNames,
    IReadOnlyList<string> Formats,
    DragDropEffects AllowedEffects);

/// <summary>
/// Hosts the <see cref="OverlayView"/> in a borderless, topmost window that never activates. Created hidden at
/// startup and only moved and shown afterwards. WPF's AllowDrop registers it as an OLE drop target; the whole plate,
/// centre included, is hit-testable so Windows reports the dragged files as soon as the ring appears.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly ILogger Logger = Log.ForContext<OverlayWindow>();

    private readonly OverlayRenderer _renderer;
    private readonly OverlayTheme _theme;
    private (int X, int Y) _anchor;
    private DragPayload? _payload;
    private int _generation;
    private bool _closed;

    public OverlayWindow(OverlayRenderer renderer)
    {
        _renderer = renderer;
        _theme = OverlayTheme.Current();
        View = renderer switch
        {
            OverlayRenderer.Glass => new GlassOverlayView(),
            OverlayRenderer.Segments => new SegmentsOverlayView(),
            _ => new RingOverlayView(_theme),
        };

        Width = View.Width;
        Height = View.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Title = "DragConvert overlay";
        Background = Brushes.Transparent;

        // Per-pixel transparency: the round shapes are the window; outside them, clicks and drops pass through.
        AllowsTransparency = true;

        Content = View;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Raised once per show, when the drag first enters the overlay.</summary>
    public event EventHandler<DragPayload>? DragEntered;

    /// <summary>Raised on drop with the tile index (-1 outside the tiles).</summary>
    public event EventHandler<int>? TileDropped;

    public OverlayView View { get; }

    public nint Handle { get; private set; }

    /// <summary>Creates the HWND without showing the window, so the first real show only moves and shows it.</summary>
    public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Renders one frame off-screen so the first real show doesn't pay for first-render setup.</summary>
    public void WarmUp(Action? completed = null)
    {
        View.SetBadge(MediaCategory.Image, 2);
        View.SetTiles([new OverlayTile("JPEG", null, MediaCategory.Image), new OverlayTile("PNG", "1 of 2", MediaCategory.Image)], "warm-up", animate: false);
        OverlayWindowInterop.MoveTo(Handle, -20000, -20000);

        // Run one backdrop capture and blur now, so the first real show doesn't pay for JIT and thread-pool start-up.
        CaptureBackdrop(new ScreenRect(0, 0, (int)Width, (int)Height));
        View.SetBackdrop(null, 0);
        Show();
        Dispatcher.BeginInvoke(
            () =>
            {
                Hide();
                View.Reset();
                completed?.Invoke();
            },
            DispatcherPriority.ContextIdle);
    }

    public void ShowAt(int x, int y)
    {
        _generation++;
        View.Reset();
        OverlayWindowInterop.SetClickThrough(Handle, false);
        _payload = null;
        _anchor = (x, y);

        // Moving to a monitor with different scaling resizes the window (WM_DPICHANGED), so centre twice: once to get
        // there, once with the size it has there.
        OverlayWindowInterop.CenterOnPoint(Handle, x, y);
        ScreenRect placed = OverlayWindowInterop.CenterOnPoint(Handle, x, y);
        if (!IsVisible)
        {
            CaptureBackdrop(placed, Native.Platform.SessionState.DpiScaleAt(x, y));
            Show();
        }

        View.AnimateIn();
    }

    /// <summary>After a drop: the window lets clicks through and shows only the status bubble.</summary>
    public void ShowWorking(int droppedTile)
    {
        OverlayWindowInterop.SetClickThrough(Handle, true);
        View.ShowWorking(droppedTile);
    }

    /// <summary>Fades out, then hides; a show that starts during the fade wins.</summary>
    public void FadeOut()
    {
        int generation = _generation;
        View.AnimateOut(() =>
        {
            // A newer show wins, and a window closed meanwhile (style switch) is left alone.
            if (generation == _generation && !_closed)
            {
                Hide();
                View.Reset();
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Logger.Debug("Overlay DPI changed {Old} -> {New}", oldDpi.PixelsPerDip, newDpi.PixelsPerDip);
        if (IsVisible)
        {
            // WPF resizes the window for the new DPI around its old corner; put it back around the cursor.
            Dispatcher.BeginInvoke(() => OverlayWindowInterop.CenterOnPoint(Handle, _anchor.X, _anchor.Y), DispatcherPriority.Render);
        }
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);

        // DragEnter bubbles up from every tile the cursor crosses; read the data once per show.
        if (_payload is null)
        {
            _payload = new DragPayload(SafePaths(e.Data), SafeVirtualNames(e.Data), SafeFormats(e.Data), e.AllowedEffects);
            Logger.Information(
                "DragEnter: allowed {Allowed}; {PathCount} path(s); {VirtualCount} virtual file(s); formats {Formats}",
                _payload.AllowedEffects,
                _payload.Paths.Count,
                _payload.VirtualFileNames.Count,
                _payload.Formats);
            DragEntered?.Invoke(this, _payload);
        }

        UpdateHover(e);
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        UpdateHover(e);
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        View.SetHovered(-1);
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int tile = View.HitTest(e.GetPosition(View));
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
        TileDropped?.Invoke(this, tile);
    }

    /// <summary>Copy whenever the source allows it; never Move, so the source never deletes the original.</summary>
    private static DragDropEffects ChooseEffect(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    private static IReadOnlyList<string> SafeFormats(IDataObject data)
    {
        try
        {
            return data.GetFormats(autoConvert: false);
        }
        catch (COMException ex)
        {
            Logger.Warning(ex, "Reading drag formats failed");
            return [];
        }
    }

    private static IReadOnlyList<string> SafePaths(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];
        }
        catch (COMException ex)
        {
            Logger.Warning(ex, "Reading CF_HDROP failed");
            return [];
        }
    }

    private static IReadOnlyList<string> SafeVirtualNames(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(FileGroupDescriptor.FormatName) && data.GetData(FileGroupDescriptor.FormatName) is MemoryStream stream
                ? FileGroupDescriptor.ReadFileNames(stream.ToArray())
                : [];
        }
        catch (COMException ex)
        {
            Logger.Warning(ex, "Reading FileGroupDescriptorW failed");
            return [];
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        OverlayWindowInterop.ApplyNoActivateToolWindow(Handle);

    }

    /// <summary>
    /// For styles that blur what's behind them (glass): copy that screen area before the window appears, with a margin
    /// so the blur has real pixels at the disc's edge, and hand it to the view.
    /// </summary>
    private void CaptureBackdrop(ScreenRect placed, double? monitorScale = null)
    {
        if (!View.WantsBackdrop)
        {
            return;
        }

        double scale = monitorScale ?? VisualTreeHelper.GetDpi(this).DpiScaleX;
        int margin = (int)Math.Ceiling(GlassOverlayView.BackdropMargin * scale);
        var area = new ScreenRect(placed.Left - margin, placed.Top - margin, placed.Right + margin, placed.Bottom + margin);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        // Margin rounded to the downscale factor so the crop lines up with the disc.
        margin -= margin % 3;
        area = new ScreenRect(placed.Left - margin, placed.Top - margin, placed.Right + margin, placed.Bottom + margin);
        ScreenPixels? pixels = ScreenCapture.Capture(area);
        if (pixels is null)
        {
            View.SetBackdrop(null, 0);
            return;
        }

        var (bitmap, brightness) = FrostedBackdrop.Create(pixels.Bgra, pixels.Width, pixels.Height, margin, sigma: OverlayTheme.Number("Glass.BlurSigma") * scale, dpi: 96 * scale);
        View.SetBackdrop(bitmap, brightness);
        Logger.Debug("Backdrop captured {Width}x{Height} in {Ms:F1} ms, brightness {Brightness:F2}", pixels.Width, pixels.Height, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, brightness);
    }

    private void UpdateHover(DragEventArgs e)
    {
        int tile = View.HitTest(e.GetPosition(View));
        View.SetHovered(tile);
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
    }
}
