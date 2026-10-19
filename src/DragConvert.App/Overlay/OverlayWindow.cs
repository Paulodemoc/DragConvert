using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using DragConvert.Core.Overlay;
using DragConvert.Native.DragDrop;
using DragConvert.Native.Windowing;
using Microsoft.Win32;
using Serilog;

namespace DragConvert.App.Overlay;

public enum OverlayRenderer
{
    /// <summary>Per-pixel transparent window (AllowsTransparency): a true ring with a click-through centre.</summary>
    Layered,

    /// <summary>Opaque borderless window with a DWM system backdrop (acrylic) and rounded corners.</summary>
    Dwm,
}

internal sealed record DropPayload(
    int TileIndex,
    string? TileLabel,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> VirtualFileNames,
    IReadOnlyList<string> Formats);

/// <summary>
/// The drag overlay: a ring of tiles drawn from <see cref="OverlayModel"/>. Created hidden at startup and only
/// moved and shown afterwards. Never activates. WPF's AllowDrop registers it as an OLE drop target.
/// </summary>
internal sealed class OverlayWindow : Window
{
    // Placeholder tile sets for the spike; M3 fills tiles from FormatCatalog.
    private static readonly string[] FormatLabels = ["JPEG", "PNG", "WebP", "PDF", "MP3", "MP4", "GIF", "WAV"];
    private static readonly string[] ToolLabels = ["Compress", "Strip EXIF", "Split PDF", "Merge", "Resize", "Rotate"];

    private const double CaptionHeight = 28;

    private static readonly ILogger Logger = Log.ForContext<OverlayWindow>();

    private readonly OverlayRenderer _renderer;
    private readonly bool _dark;
    private readonly Canvas _root = new() { Background = Brushes.Transparent };
    private readonly Canvas _ring = new();
    private readonly TextBlock _caption;
    private readonly ScaleTransform _ringScale = new(1, 1);
    private readonly List<Border> _tiles = [];
    private OverlayModel _model = new(FormatLabels.Length);
    private string[] _labels = [];
    private int _hovered = -1;
    private string _dragSummary = string.Empty;
    private (int X, int Y) _anchor;
    private bool _dragEntered;

    public OverlayWindow(OverlayRenderer renderer)
    {
        _renderer = renderer;
        _dark = IsSystemDarkTheme();

        double diameter = 2 * _model.OuterRadius;
        Width = diameter;
        Height = diameter + CaptionHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Title = "DragConvert overlay";

        if (renderer == OverlayRenderer.Layered)
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
        }
        else
        {
            Background = Brushes.Transparent;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        _caption = new TextBlock
        {
            Width = diameter,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12,
            Foreground = _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(8, 4, 8, 4),
        };
        Canvas.SetTop(_caption, diameter + 2);

        _ring.Width = diameter;
        _ring.Height = diameter;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = _ringScale;

        // In the layered renderer, alpha-0 pixels are invisible to OS hit testing, so drops in the dead zone fall
        // through to the window underneath. A nearly transparent annulus keeps the gaps between tiles hittable.
        if (renderer == OverlayRenderer.Layered)
        {
            double r = _model.OuterRadius;
            var annulus = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(r, r), r, r),
                new EllipseGeometry(new Point(r, r), _model.DeadZoneRadius, _model.DeadZoneRadius));
            _ring.Children.Add(new System.Windows.Shapes.Path { Data = annulus, Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) });
        }

        _root.Children.Add(_ring);
        _root.Children.Add(_caption);
        Content = _root;

        SetTiles(FormatLabels);
        SourceInitialized += OnSourceInitialized;
    }

    public event EventHandler<DropPayload>? Dropped;

    public nint Handle { get; private set; }

    public bool BackdropApplied { get; private set; }

    public bool IsToolsMode { get; private set; }

    /// <summary>Creates the HWND without showing the window, so the first real show only moves and shows it.</summary>
    public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Renders one frame off-screen so the first real show doesn't pay for first-render setup.</summary>
    public void WarmUp(Action? completed = null)
    {
        OverlayWindowInterop.MoveTo(Handle, -20000, -20000);
        Show();
        Dispatcher.BeginInvoke(
            () =>
            {
                Hide();
                completed?.Invoke();
            },
            DispatcherPriority.ContextIdle);
    }

    public void ShowAt(int x, int y, bool toolsMode)
    {
        SetToolsMode(toolsMode);
        SetHovered(-1);
        _dragSummary = string.Empty;
        _dragEntered = false;
        _caption.Text = toolsMode ? "Drop on a tool" : "Drop on a format";
        _anchor = (x, y);

        OverlayWindowInterop.CenterOnPoint(Handle, x, y);
        if (!IsVisible)
        {
            Show();
        }

        AnimateIn();
    }

    public void SetToolsMode(bool toolsMode)
    {
        if (toolsMode == IsToolsMode && _labels.Length > 0)
        {
            return;
        }

        IsToolsMode = toolsMode;
        SetTiles(toolsMode ? ToolLabels : FormatLabels);
        if (IsVisible && string.IsNullOrEmpty(_dragSummary))
        {
            _caption.Text = toolsMode ? "Drop on a tool" : "Drop on a format";
        }
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
        if (_dragEntered)
        {
            UpdateHover(e);
            e.Handled = true;
            return;
        }

        _dragEntered = true;
        IReadOnlyList<string> formats = SafeFormats(e.Data);
        IReadOnlyList<string> paths = SafePaths(e.Data);
        IReadOnlyList<string> virtualNames = SafeVirtualNames(e.Data);

        Logger.Information(
            "DragEnter: allowed {Allowed}; {PathCount} path(s) {Paths}; {VirtualCount} virtual file(s) {VirtualNames}; formats {Formats}",
            e.AllowedEffects,
            paths.Count,
            paths,
            virtualNames.Count,
            virtualNames,
            formats);

        _dragSummary = Summarize(paths.Count > 0 ? paths : virtualNames);
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
        SetHovered(-1);
        _caption.Text = _dragSummary;
        Logger.Debug("DragLeave");
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int tile = HitTest(e);
        var payload = new DropPayload(
            tile,
            tile >= 0 ? _labels[tile] : null,
            SafePaths(e.Data),
            SafeVirtualNames(e.Data),
            SafeFormats(e.Data));
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
        Dropped?.Invoke(this, payload);
    }

    private static bool IsSystemDarkTheme()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Copy whenever the source allows it; never Move, so the source never deletes the original.</summary>
    private static DragDropEffects ChooseEffect(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    private static string Summarize(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return "No files in this drag";
        }

        string[] extensions = names
            .Select(n => System.IO.Path.GetExtension(n).ToUpperInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string kinds = extensions.Length == 0 ? string.Empty : $" · {string.Join(", ", extensions)}";
        return names.Count == 1 ? $"{System.IO.Path.GetFileName(names[0])}" : $"{names.Count} files{kinds}";
    }

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

        if (_renderer == OverlayRenderer.Dwm)
        {
            if (HwndSource.FromHwnd(Handle) is { CompositionTarget: { } target })
            {
                target.BackgroundColor = Colors.Transparent;
            }

            BackdropApplied = OverlayWindowInterop.TryApplySystemBackdrop(Handle, _dark);
            if (!BackdropApplied)
            {
                // Windows 10 or older Windows 11: solid, slightly translucent tint instead of acrylic.
                _root.Background = new SolidColorBrush(_dark ? Color.FromArgb(0xF0, 0x20, 0x20, 0x20) : Color.FromArgb(0xF0, 0xF3, 0xF3, 0xF3));
            }
        }
    }

    private void SetTiles(string[] labels)
    {
        foreach (Border tile in _tiles)
        {
            _ring.Children.Remove(tile);
        }

        _tiles.Clear();
        _labels = labels;
        _model = new OverlayModel(labels.Length);
        _hovered = -1;

        var tileBrush = new SolidColorBrush(_dark ? Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF2, 0xFB, 0xFB, 0xFB));
        var textBrush = _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        double r = _model.OuterRadius;

        for (int i = 0; i < labels.Length; i++)
        {
            var (cx, cy) = _model.TileCenter(i);
            var tile = new Border
            {
                Width = _model.TileSize,
                Height = _model.TileSize,
                CornerRadius = new CornerRadius(8),
                Background = tileBrush,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Child = new TextBlock
                {
                    Text = labels[i],
                    Foreground = textBrush,
                    FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(tile, r + cx - (_model.TileSize / 2));
            Canvas.SetTop(tile, r + cy - (_model.TileSize / 2));
            _tiles.Add(tile);
            _ring.Children.Add(tile);
        }
    }

    private int HitTest(DragEventArgs e)
    {
        Point p = e.GetPosition(_ring);
        return _model.HitTest(p.X - _model.OuterRadius, p.Y - _model.OuterRadius);
    }

    private void UpdateHover(DragEventArgs e)
    {
        int tile = HitTest(e);
        SetHovered(tile);
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
        _caption.Text = tile >= 0 ? $"{_labels[tile]}, same folder" : _dragSummary;
    }

    private void SetHovered(int tile)
    {
        if (tile == _hovered)
        {
            return;
        }

        if (_hovered >= 0 && _hovered < _tiles.Count)
        {
            _tiles[_hovered].BorderBrush = Brushes.Transparent;
            AnimateScale((ScaleTransform)_tiles[_hovered].RenderTransform, 1.0, 100);
        }

        _hovered = tile;
        if (tile >= 0)
        {
            _tiles[tile].BorderBrush = new SolidColorBrush(SystemParameters.WindowGlassColor);
            AnimateScale((ScaleTransform)_tiles[tile].RenderTransform, 1.06, 100);
        }
    }

    private void AnimateIn()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _ring.Opacity = 1;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        _ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease });
        _ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, duration) { EasingFunction = ease });
    }

    private static void AnimateScale(ScaleTransform transform, double to, int milliseconds)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            transform.ScaleX = to;
            transform.ScaleY = to;
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
