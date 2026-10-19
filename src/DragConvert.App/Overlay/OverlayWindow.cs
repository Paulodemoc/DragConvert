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
    /// <summary>Per-pixel transparent window (AllowsTransparency): a true ring shape.</summary>
    Layered,

    /// <summary>Opaque borderless window with a DWM system backdrop (acrylic) and rounded corners.</summary>
    Dwm,
}

/// <summary>One tile: its label and an optional second line such as "3 of 5".</summary>
internal sealed record OverlayTile(string Label, string? Detail = null);

/// <summary>What the drag carries, read once when it first enters the overlay.</summary>
internal sealed record DragPayload(
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> VirtualFileNames,
    IReadOnlyList<string> Formats,
    DragDropEffects AllowedEffects);

/// <summary>
/// The drag overlay: a ring of tiles drawn from <see cref="OverlayModel"/>. Created hidden at startup and only moved
/// and shown afterwards; never activates. WPF's AllowDrop registers it as an OLE drop target. The whole disc, centre
/// included, is hit-testable so Windows reports the dragged files as soon as the ring appears under the cursor.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const double CaptionHeight = 28;

    private static readonly ILogger Logger = Log.ForContext<OverlayWindow>();

    private readonly OverlayRenderer _renderer;
    private readonly bool _dark;
    private readonly double _outerRadius;
    private readonly Canvas _root = new() { Background = Brushes.Transparent };
    private readonly Canvas _ring = new();
    private readonly TextBlock _caption;
    private readonly ScaleTransform _ringScale = new(1, 1);
    private readonly List<Border> _tileViews = [];
    private OverlayModel? _model;
    private IReadOnlyList<OverlayTile> _tiles = [];
    private int _hovered = -1;
    private string _summary = string.Empty;
    private (int X, int Y) _anchor;
    private DragPayload? _payload;

    public OverlayWindow(OverlayRenderer renderer)
    {
        _renderer = renderer;
        _dark = IsSystemDarkTheme();
        _outerRadius = new OverlayModel(OverlayModel.MaxTiles).OuterRadius;

        double diameter = 2 * _outerRadius;
        Width = diameter;
        Height = diameter + CaptionHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Title = "DragConvert overlay";
        Background = Brushes.Transparent;

        if (renderer == OverlayRenderer.Layered)
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

        _caption = new TextBlock
        {
            Width = diameter,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 12,
            Foreground = TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(8, 4, 8, 4),
        };
        Canvas.SetTop(_caption, diameter + 2);

        _ring.Width = diameter;
        _ring.Height = diameter;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = _ringScale;

        // Alpha-0 pixels of a layered window are invisible to OS hit testing; a nearly transparent disc keeps the
        // whole ring, centre included, a drop target.
        if (renderer == OverlayRenderer.Layered)
        {
            _ring.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            });
        }

        _root.Children.Add(_ring);
        _root.Children.Add(_caption);
        Content = _root;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Raised once per show, when the drag first enters the overlay.</summary>
    public event EventHandler<DragPayload>? DragEntered;

    /// <summary>Raised on drop with the tile index (-1 outside the tiles).</summary>
    public event EventHandler<int>? TileDropped;

    public nint Handle { get; private set; }

    public bool BackdropApplied { get; private set; }

    private Brush TextBrush => _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

    /// <summary>Creates the HWND without showing the window, so the first real show only moves and shows it.</summary>
    public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Renders one frame off-screen so the first real show doesn't pay for first-render setup.</summary>
    public void WarmUp(Action? completed = null)
    {
        SetTiles([new OverlayTile("JPEG"), new OverlayTile("PNG", "1 of 2")]);
        OverlayWindowInterop.MoveTo(Handle, -20000, -20000);
        Show();
        Dispatcher.BeginInvoke(
            () =>
            {
                Hide();
                SetTiles([]);
                completed?.Invoke();
            },
            DispatcherPriority.ContextIdle);
    }

    public void ShowAt(int x, int y)
    {
        SetTiles([]);
        _payload = null;
        _summary = string.Empty;
        _caption.Text = string.Empty;
        _anchor = (x, y);

        OverlayWindowInterop.CenterOnPoint(Handle, x, y);
        if (!IsVisible)
        {
            Show();
        }

        AnimateIn();
    }

    /// <summary>Replaces the tiles (at most <see cref="OverlayModel.MaxTiles"/>) and the caption shown when no tile is hovered.</summary>
    public void SetTiles(IReadOnlyList<OverlayTile> tiles, string? summary = null)
    {
        foreach (Border view in _tileViews)
        {
            _ring.Children.Remove(view);
        }

        _tileViews.Clear();
        _tiles = tiles.Take(OverlayModel.MaxTiles).ToList();
        _model = _tiles.Count > 0 ? new OverlayModel(_tiles.Count) : null;
        _hovered = -1;
        _summary = summary ?? string.Empty;
        _caption.Text = _summary;

        if (_model is null)
        {
            return;
        }

        var tileBrush = new SolidColorBrush(_dark ? Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF2, 0xFB, 0xFB, 0xFB));
        Brush text = TextBrush;
        for (int i = 0; i < _tiles.Count; i++)
        {
            var (cx, cy) = _model.TileCenter(i);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = _tiles[i].Label,
                Foreground = text,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
            });
            if (_tiles[i].Detail is { } detail)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = detail,
                    Foreground = text,
                    Opacity = 0.7,
                    FontFamily = new FontFamily("Segoe UI Variable Small, Segoe UI"),
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                });
            }

            var view = new Border
            {
                Width = _model.TileSize,
                Height = _model.TileSize,
                CornerRadius = new CornerRadius(8),
                Background = tileBrush,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Child = stack,
            };
            Canvas.SetLeft(view, _outerRadius + cx - (_model.TileSize / 2));
            Canvas.SetTop(view, _outerRadius + cy - (_model.TileSize / 2));
            _tileViews.Add(view);
            _ring.Children.Add(view);
        }
    }

    public void SetCaption(string text)
    {
        _summary = text;
        if (_hovered < 0)
        {
            _caption.Text = text;
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
        SetHovered(-1);
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int tile = HitTest(e);
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
        TileDropped?.Invoke(this, tile);
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

    private int HitTest(DragEventArgs e)
    {
        if (_model is null)
        {
            return -1;
        }

        Point p = e.GetPosition(_ring);
        return _model.HitTest(p.X - _outerRadius, p.Y - _outerRadius);
    }

    private void UpdateHover(DragEventArgs e)
    {
        int tile = HitTest(e);
        SetHovered(tile);
        e.Effects = tile >= 0 ? ChooseEffect(e.AllowedEffects) : DragDropEffects.None;
    }

    private void SetHovered(int tile)
    {
        if (tile == _hovered)
        {
            return;
        }

        if (_hovered >= 0 && _hovered < _tileViews.Count)
        {
            _tileViews[_hovered].BorderBrush = Brushes.Transparent;
            AnimateScale((ScaleTransform)_tileViews[_hovered].RenderTransform, 1.0, 100);
        }

        _hovered = tile;
        if (tile >= 0)
        {
            _tileViews[tile].BorderBrush = new SolidColorBrush(SystemParameters.WindowGlassColor);
            AnimateScale((ScaleTransform)_tileViews[tile].RenderTransform, 1.06, 100);
            _caption.Text = $"{_tiles[tile].Label}{(_tiles[tile].Detail is { } d ? $" ({d})" : string.Empty)}";
        }
        else
        {
            _caption.Text = _summary;
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
