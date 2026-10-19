using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;
using DragConvert.Native.DragDrop;
using DragConvert.Native.Windowing;
using Serilog;

namespace DragConvert.App.Overlay;

public enum OverlayRenderer
{
    /// <summary>Per-pixel transparent window (AllowsTransparency): a true round plate.</summary>
    Layered,

    /// <summary>Opaque borderless window with a DWM system backdrop (acrylic) and rounded corners.</summary>
    Dwm,

    /// <summary>Blue glass disc with round bubbles; acrylic blur clipped to the disc on Windows 11.</summary>
    Glass,
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
    private readonly bool _useBackdrop;
    private (int X, int Y) _anchor;
    private DragPayload? _payload;
    private int _generation;

    public OverlayWindow(OverlayRenderer renderer)
    {
        _renderer = renderer;
        _theme = OverlayTheme.Current();
        _useBackdrop = renderer == OverlayRenderer.Dwm;
        View = renderer == OverlayRenderer.Glass
            ? new GlassOverlayView()
            : new RingOverlayView(_theme, showPlate: renderer == OverlayRenderer.Layered);

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

        if (!_useBackdrop)
        {
            AllowsTransparency = true;
        }
        else
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        Content = View;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Raised once per show, when the drag first enters the overlay.</summary>
    public event EventHandler<DragPayload>? DragEntered;

    /// <summary>Raised on drop with the tile index (-1 outside the tiles).</summary>
    public event EventHandler<int>? TileDropped;

    public OverlayView View { get; }

    public nint Handle { get; private set; }

    public bool BackdropApplied { get; private set; }

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
        View.SetBackdrop(null);
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

        ScreenRect placed = OverlayWindowInterop.CenterOnPoint(Handle, x, y);
        if (!IsVisible)
        {
            CaptureBackdrop(placed);
            Show();
        }

        if (BackdropApplied)
        {
            OverlayWindowInterop.ForceActiveFrame(Handle);
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
            if (generation == _generation)
            {
                Hide();
                View.Reset();
            }
        });
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

        if (_useBackdrop)
        {
            if (HwndSource.FromHwnd(Handle) is { CompositionTarget: { } target })
            {
                target.BackgroundColor = Colors.Transparent;
            }

            BackdropApplied = OverlayWindowInterop.TryApplySystemBackdrop(Handle, _theme.Variant == ThemeVariant.Dark);
            if (BackdropApplied)
            {
                // Keep the frame "active" so DWM renders the acrylic instead of its inactive grey fallback.
                HwndSource.FromHwnd(Handle)?.AddHook(KeepFrameActive);
                OverlayWindowInterop.ForceActiveFrame(Handle);
            }
            if (!BackdropApplied)
            {
                // Windows 10 or older Windows 11: the themed plate colour as a solid panel instead of acrylic.
                View.Background = _theme.Plate;
            }
        }
    }

    private static nint KeepFrameActive(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == OverlayWindowInterop.WmNcActivate && wParam == 0)
        {
            // Swallow "deactivate" so the backdrop never drops to its inactive fallback.
            handled = true;
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// For styles that blur what's behind them (glass): copy that screen area before the window appears, with a margin
    /// so the blur has real pixels at the disc's edge, and hand it to the view.
    /// </summary>
    private void CaptureBackdrop(ScreenRect placed)
    {
        if (!View.WantsBackdrop)
        {
            return;
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int margin = (int)Math.Ceiling(GlassOverlayView.BackdropMargin * scale);
        var area = new ScreenRect(placed.Left - margin, placed.Top - margin, placed.Right + margin, placed.Bottom + margin);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        // Margin rounded to the downscale factor so the crop lines up with the disc.
        margin -= margin % 3;
        area = new ScreenRect(placed.Left - margin, placed.Top - margin, placed.Right + margin, placed.Bottom + margin);
        ScreenPixels? pixels = ScreenCapture.Capture(area);
        if (pixels is null)
        {
            View.SetBackdrop(null);
            return;
        }

        BitmapSource bitmap = FrostedBackdrop.Create(pixels.Bgra, pixels.Width, pixels.Height, margin, sigma: 9 * scale, dpi: 96 * scale);
        View.SetBackdrop(bitmap);
        Logger.Debug("Backdrop captured {Width}x{Height} in {Ms:F1} ms", pixels.Width, pixels.Height, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private void UpdateHover(DragEventArgs e)
    {
        int tile = View.HitTest(e.GetPosition(View));
        View.SetHovered(tile);
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
    }
}
