using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;
using DragConvert.Core.Overlay;

namespace DragConvert.App.Overlay;

/// <summary>
/// The "Ring" style: draws the overlay from <see cref="OverlayModel"/> and the theme tokens: a round plate, a ring of tiles, a centre
/// badge for what's being dragged, a caption pill, and the status bubble shown after a drop. Used by the overlay
/// window, the UI gallery and the snapshot tool, so all three show exactly the same visuals.
/// </summary>
internal sealed class RingOverlayView : OverlayView
{
    private readonly OverlayTheme _theme;
    private readonly bool _showPlate;
    private readonly double _outer;
    private readonly double _platePadding;
    private readonly Canvas _ring = new();
    private readonly ScaleTransform _ringScale = new(1, 1);
    private readonly Ellipse _plate;
    private readonly Border _badge;
    private readonly TextBlock _badgeGlyph;
    private readonly TextBlock _badgeCount;
    private readonly Border _caption;
    private readonly TextBlock _captionText;
    private readonly Grid _bubble;
    private readonly ScaleTransform _bubbleScale = new(1, 1);
    private readonly List<Border> _tileViews = [];
    private OverlayModel? _model;
    private IReadOnlyList<OverlayTile> _tiles = [];
    private int _hovered = -1;
    private string _summary = string.Empty;

    public RingOverlayView(OverlayTheme theme, bool showPlate = true)
    {
        _theme = theme;
        _showPlate = showPlate;
        _platePadding = OverlayTheme.Number("Overlay.PlatePadding");
        _outer = CreateModel(OverlayModel.MaxTiles).OuterRadius;

        Diameter = 2 * (_outer + _platePadding);
        Width = Diameter;
        Height = Diameter + OverlayTheme.Number("Overlay.CaptionHeight") + 6;
        Background = Brushes.Transparent;

        _ring.Width = Diameter;
        _ring.Height = Diameter;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = _ringScale;

        // The plate is what makes the layered window a drop target: its pixels aren't fully transparent.
        _plate = new Ellipse
        {
            Width = Diameter,
            Height = Diameter,
            Fill = showPlate ? theme.Plate : new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Stroke = showPlate ? theme.PlateBorder : null,
            StrokeThickness = 1,
        };
        _ring.Children.Add(_plate);

        double badgeSize = OverlayTheme.Number("Overlay.BadgeSize");
        _badgeGlyph = new TextBlock
        {
            FontFamily = OverlayTheme.IconFont,
            FontSize = 20,
            Foreground = theme.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _badgeCount = new TextBlock
        {
            FontFamily = OverlayTheme.SmallFont,
            FontSize = OverlayTheme.Number("Overlay.DetailSize"),
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var badgeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        badgeStack.Children.Add(_badgeGlyph);
        badgeStack.Children.Add(_badgeCount);
        _badge = new Border
        {
            Width = badgeSize,
            Height = badgeSize,
            CornerRadius = new CornerRadius(badgeSize / 2),
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(1),
            Child = badgeStack,
            Visibility = Visibility.Collapsed,
        };
        Place(_badge, Center, Center);
        _ring.Children.Add(_badge);

        _captionText = new TextBlock
        {
            FontFamily = OverlayTheme.TextFont,
            FontSize = OverlayTheme.Number("Overlay.CaptionSize"),
            Foreground = theme.CaptionText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Diameter - 24,
        };
        _caption = new Border
        {
            Background = theme.Caption,
            BorderBrush = theme.PlateBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 5),
            Child = _captionText,
            Visibility = Visibility.Collapsed,
        };
        FitPlate();

        _bubble = BuildBubble();
        Children.Add(_ring);
        Children.Add(_caption);
        Children.Add(_bubble);
        _caption.SizeChanged += (_, _) => SetLeft(_caption, (Diameter - _caption.ActualWidth) / 2);
    }

    public double Diameter { get; }

    public double Center => Diameter / 2;

    /// <summary>Back to the just-shown state: no tiles, no badge, no caption, no bubble.</summary>
    public override void Reset()
    {
        SetTiles([], animate: false);
        _badge.Visibility = Visibility.Collapsed;
        SetCaption(null);
        _bubble.Visibility = Visibility.Collapsed;
        _ring.BeginAnimation(OpacityProperty, null);
        _ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _ring.Opacity = 1;
        _ringScale.ScaleX = _ringScale.ScaleY = 1;
        Opacity = 1;
    }

    /// <summary>Centre badge: the dragged files' category (or a folder for mixed drags) and how many there are.</summary>
    public override void SetBadge(MediaCategory? category, int count)
    {
        _badgeGlyph.Text = category is { } c ? OverlayTheme.Glyph(c) : OverlayTheme.Glyphs.Files;
        _badgeCount.Text = count > 1 ? count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        _badgeCount.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _badge.Visibility = Visibility.Visible;
    }

    public override void SetTiles(IReadOnlyList<OverlayTile> tiles, string? summary = null, bool animate = true)
    {
        foreach (Border view in _tileViews)
        {
            _ring.Children.Remove(view);
        }

        _tileViews.Clear();
        _tiles = tiles.Take(OverlayModel.MaxTiles).ToList();
        _model = _tiles.Count > 0 ? CreateModel(_tiles.Count) : null;
        _hovered = -1;
        _summary = summary ?? string.Empty;
        FitPlate();
        SetCaption(_summary);
        if (_model is null)
        {
            return;
        }

        double stagger = OverlayTheme.Number("Motion.TileStaggerMs");
        for (int i = 0; i < _tiles.Count; i++)
        {
            Border view = BuildTile(_tiles[i]);
            var (cx, cy) = _model.TileCenter(i);
            Place(view, Center + cx, Center + cy);
            _tileViews.Add(view);
            _ring.Children.Add(view);

            if (animate && Animate)
            {
                var begin = TimeSpan.FromMilliseconds(i * stagger);
                var duration = OverlayTheme.Duration("Motion.ShowMs");
                view.Opacity = 0;
                view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = EaseOut() });
                var scale = (ScaleTransform)view.RenderTransform;
                var grow = new DoubleAnimation(OverlayTheme.Number("Motion.ShowFromScale"), 1, duration) { BeginTime = begin, EasingFunction = EaseOut() };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }
    }

    public override void SetCaption(string? text)
    {
        _summary = text ?? string.Empty;
        if (_hovered < 0)
        {
            ShowCaption(_summary);
        }
    }

    /// <summary>The tile under a point in this view's coordinates, or -1.</summary>
    public override int HitTest(Point p) => _model?.HitTest(p.X - Center, p.Y - Center) ?? -1;

    public override void SetHovered(int tile)
    {
        if (tile == _hovered || tile >= _tileViews.Count)
        {
            return;
        }

        if (_hovered >= 0)
        {
            StyleTile(_tileViews[_hovered], hovered: false);
        }

        _hovered = tile;
        if (tile >= 0)
        {
            StyleTile(_tileViews[tile], hovered: true);
            OverlayTile t = _tiles[tile];
            ShowCaption(t.Detail is null ? $"{t.Label}, beside the original" : $"{t.Label} for {t.Detail} files");
        }
        else
        {
            ShowCaption(_summary);
        }
    }

    /// <summary>Show: plate fades and scales in.</summary>
    public override void AnimateIn()
    {
        if (!Animate)
        {
            return;
        }

        var duration = OverlayTheme.Duration("Motion.ShowMs");
        double from = OverlayTheme.Number("Motion.ShowFromScale");
        _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut() });
        _ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = EaseOut() });
        _ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = EaseOut() });
    }

    /// <summary>Hide: everything fades out, then <paramref name="completed"/> runs.</summary>
    public override void AnimateOut(Action completed)
    {
        if (!Animate)
        {
            completed();
            return;
        }

        var fade = new DoubleAnimation(0, OverlayTheme.Duration("Motion.HideMs"));
        fade.Completed += (_, _) => completed();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>After a drop: the ring collapses and a spinner appears where the dropped tile was.</summary>
    public override void ShowWorking(int droppedTile)
    {
        var (x, y) = droppedTile >= 0 && _model is not null && droppedTile < _tiles.Count ? _model.TileCenter(droppedTile) : (0, 0);
        double size = OverlayTheme.Number("Overlay.BubbleSize");
        SetLeft(_bubble, Center + x - (size / 2));
        SetTop(_bubble, Center + y - (size / 2));
        SetCaption(null);
        _caption.Visibility = Visibility.Collapsed;

        // Background circle stays; the spinner shows, the result glyph hides.
        foreach (UIElement child in _bubble.Children)
        {
            if (child is Path or TextBlock)
            {
                child.Visibility = child is Path ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        _bubble.Visibility = Visibility.Visible;
        if (Animate)
        {
            var collapse = OverlayTheme.Duration("Motion.CollapseMs");
            _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0, collapse));
            _ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, collapse) { EasingFunction = EaseOut() });
            _ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, collapse) { EasingFunction = EaseOut() });
            _bubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, 1, collapse) { EasingFunction = EaseOut() });
            _bubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.5, 1, collapse) { EasingFunction = EaseOut() });
        }
        else
        {
            _ring.Opacity = 0;
        }
    }

    /// <summary>Replaces the spinner with a check mark (success) or a cross (failure).</summary>
    public override void ShowResult(bool success)
    {
        foreach (UIElement child in _bubble.Children)
        {
            if (child is TextBlock glyph)
            {
                glyph.Text = success ? OverlayTheme.Glyphs.Check : OverlayTheme.Glyphs.Failure;
                glyph.Foreground = success ? _theme.Success : _theme.Failure;
                glyph.Visibility = Visibility.Visible;
            }
            else if (child is Path)
            {
                child.Visibility = Visibility.Collapsed;
            }
        }

        if (Animate)
        {
            var pop = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            _bubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            _bubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
    }

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Sizes the plate (and moves the caption) to the current ring: eight tiles need a wider ring than six, and the
    /// window is sized for the widest. With no tiles yet, the plate takes the six-tile size.
    /// </summary>
    private void FitPlate()
    {
        double radius = (_model ?? CreateModel(6)).OuterRadius + _platePadding;
        _plate.Width = _plate.Height = 2 * radius;
        SetLeft(_plate, Center - radius);
        SetTop(_plate, Center - radius);
        SetTop(_caption, Center + radius + 6);
    }

    private static OverlayModel CreateModel(int tiles) => new(
        tiles,
        OverlayTheme.Number("Overlay.TileSize"),
        OverlayTheme.Number("Overlay.DeadZoneRadius"),
        OverlayTheme.Number("Overlay.Gap"));

    private static void Place(FrameworkElement element, double centerX, double centerY)
    {
        SetLeft(element, centerX - (element.Width / 2));
        SetTop(element, centerY - (element.Height / 2));
    }

    private Border BuildTile(OverlayTile tile)
    {
        double size = OverlayTheme.Number("Overlay.TileSize");
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = OverlayTheme.Glyph(tile.Category),
            FontFamily = OverlayTheme.IconFont,
            FontSize = OverlayTheme.Number("Overlay.GlyphSize"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3),
        });
        stack.Children.Add(new TextBlock
        {
            Text = tile.Label,
            FontFamily = OverlayTheme.TextFont,
            FontSize = OverlayTheme.Number("Overlay.LabelSize"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (tile.Detail is { } detail)
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                FontFamily = OverlayTheme.SmallFont,
                FontSize = OverlayTheme.Number("Overlay.DetailSize"),
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        var view = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(OverlayTheme.Number("Overlay.TileRadius")),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Child = stack,
        };
        AutomationProperties.SetName(view, tile.Detail is null ? $"Convert to {tile.Label}" : $"Convert {tile.Detail} files to {tile.Label}");
        StyleTile(view, hovered: false, animate: false);
        return view;
    }

    private void StyleTile(Border view, bool hovered, bool animate = true)
    {
        view.Background = hovered ? _theme.Accent : _theme.Tile;
        view.BorderBrush = hovered ? _theme.Accent : _theme.TileBorder;
        TextElement.SetForeground(view, hovered ? _theme.AccentText : _theme.Text);

        double to = hovered ? OverlayTheme.Number("Motion.HoverScale") : 1;
        var scale = (ScaleTransform)view.RenderTransform;
        if (animate && Animate)
        {
            var grow = new DoubleAnimation(to, OverlayTheme.Duration("Motion.HoverMs")) { EasingFunction = EaseOut() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        else
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = to;
        }
    }

    private void ShowCaption(string text)
    {
        _captionText.Text = text;
        _caption.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private Grid BuildBubble()
    {
        double size = OverlayTheme.Number("Overlay.BubbleSize");
        var grid = new Grid
        {
            Width = size,
            Height = size,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _bubbleScale,
        };
        grid.Children.Add(new Ellipse { Fill = _theme.Tile, Stroke = _theme.TileBorder, StrokeThickness = 1 });

        // Spinner: a 270° arc that rotates while the conversion runs.
        double r = (size / 2) - 10;
        var rotate = new RotateTransform(0, size / 2, size / 2);
        var arc = new Path
        {
            Stroke = _theme.Accent,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new PathGeometry([new PathFigure(new Point(size / 2, 10), [new ArcSegment(new Point((size / 2) - r, size / 2), new Size(r, r), 0, true, SweepDirection.Clockwise, true)], false)]),
            RenderTransform = rotate,
        };
        if (Animate)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        grid.Children.Add(arc);
        grid.Children.Add(new TextBlock
        {
            FontFamily = OverlayTheme.IconFont,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        });
        return grid;
    }
}
