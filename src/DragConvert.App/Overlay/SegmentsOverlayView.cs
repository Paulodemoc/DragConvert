using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;

namespace DragConvert.App.Overlay;

/// <summary>
/// The "Segments" style: a ring split into one slice per option, sized to share the full circle (two options make two
/// half-rings, eight make eighths), with even gaps and rounded corners, labels inside the slices and a centre circle
/// naming the hovered option. Sits on the same frosted backdrop as the glass style.
/// </summary>
internal sealed class SegmentsOverlayView : OverlayView
{
    private readonly double _outer;
    private readonly double _inner;
    private readonly double _gap;
    private readonly double _corner;
    private readonly double _radius;
    private readonly Canvas _ring = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Image _backdrop = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Ellipse _scrim;
    private readonly Grid _center;
    private readonly TextBlock _centerText;
    private readonly TextBlock _centerDetail;
    private readonly Border _countBadge;
    private readonly TextBlock _countText;
    private readonly Path _spinner;
    private readonly RotateTransform _spin;
    private readonly TextBlock _resultGlyph;
    private readonly List<(Path Slice, TranslateTransform Nudge, StackPanel Labels)> _slices = [];
    private IReadOnlyList<OverlayTile> _tiles = [];
    private int _hovered = -1;
    private string _summary = string.Empty;

    public SegmentsOverlayView()
    {
        _outer = OverlayTheme.Number("Segments.OuterRadius");
        _inner = OverlayTheme.Number("Segments.InnerRadius");
        _gap = OverlayTheme.Number("Segments.Gap");
        _corner = OverlayTheme.Number("Segments.Corner");
        _radius = _outer + OverlayTheme.Number("Segments.RimPadding");
        Width = Height = 2 * _radius;
        Background = Brushes.Transparent;

        _ring.Width = _ring.Height = Width;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = _scale;

        // Frosted base: blurred backdrop, brightness-aware darkening, a faint white frost and a light rim.
        _backdrop.Width = _backdrop.Height = Width;
        _backdrop.Clip = new EllipseGeometry(new Point(_radius, _radius), _radius - 1, _radius - 1);
        _backdrop.Visibility = Visibility.Collapsed;
        _ring.Children.Add(_backdrop);
        _scrim = new Ellipse { Width = Width, Height = Height, Fill = Brushes.Black, Opacity = OverlayTheme.Number("Glass.ScrimNoBackdrop") };
        _ring.Children.Add(_scrim);
        _ring.Children.Add(new Ellipse
        {
            Width = Width,
            Height = Height,
            Fill = new SolidColorBrush(Color.FromArgb((byte)(OverlayTheme.Number("Segments.BaseFrost") * 255), 255, 255, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            StrokeThickness = 1.4,
        });

        // Centre: a light circle naming the hovered option (or the dragged file), with a count badge and status.
        double centerSize = 2 * (_inner - _gap);
        _centerText = new TextBlock
        {
            FontFamily = OverlayTheme.TextFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Token("Segments.Text")),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = centerSize - 18,
            MaxHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _centerDetail = new TextBlock
        {
            FontFamily = OverlayTheme.SmallFont,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = centerSize - 18,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var centerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        centerStack.Children.Add(_centerText);
        centerStack.Children.Add(_centerDetail);

        _countText = new TextBlock { FontFamily = OverlayTheme.SmallFont, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Token("Segments.HoverText")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _countBadge = new Border
        {
            MinWidth = 20,
            Height = 20,
            Padding = new Thickness(5, 0, 5, 0),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Token("Segments.Hover")),
            Child = _countText,
            // Inside the centre circle, above the text, so it never covers a slice.
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        double spinnerRadius = 16;
        _spin = new RotateTransform(0, spinnerRadius, spinnerRadius);
        _spinner = new Path
        {
            Width = 2 * spinnerRadius,
            Height = 2 * spinnerRadius,
            Stroke = new SolidColorBrush(Token("Segments.Hover")),
            StrokeThickness = 3.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new PathGeometry([new PathFigure(new Point(spinnerRadius, 2), [new ArcSegment(new Point(2, spinnerRadius), new Size(spinnerRadius - 2, spinnerRadius - 2), 0, true, SweepDirection.Clockwise, true)], false)]),
            RenderTransform = _spin,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _resultGlyph = new TextBlock
        {
            FontFamily = OverlayTheme.IconFont,
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _center = new Grid { Width = centerSize, Height = centerSize };
        _center.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb((byte)(OverlayTheme.Number("Segments.CenterAlpha") * 255), 255, 255, 255)),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1, Opacity = 0.25 },
        });
        _center.Children.Add(centerStack);
        _center.Children.Add(_countBadge);
        _center.Children.Add(_spinner);
        _center.Children.Add(_resultGlyph);
        Canvas.SetLeft(_center, _radius - (centerSize / 2));
        Canvas.SetTop(_center, _radius - (centerSize / 2));
        _ring.Children.Add(_center);

        Children.Add(_ring);
    }

    public override bool WantsBackdrop => true;

    public override void SetBackdrop(BitmapSource? snapshot, double brightness)
    {
        _backdrop.Source = snapshot;
        _backdrop.Visibility = snapshot is null ? Visibility.Collapsed : Visibility.Visible;
        _scrim.Opacity = snapshot is null
            ? OverlayTheme.Number("Glass.ScrimNoBackdrop")
            : OverlayTheme.Number("Glass.ScrimBase") + (OverlayTheme.Number("Glass.ScrimPerBrightness") * Math.Clamp(brightness, 0, 1));
    }

    public override void Reset()
    {
        SetTiles([], animate: false);
        SetCaption(null);
        Spin(_spin, on: false);
        _countBadge.Visibility = Visibility.Collapsed;
        ShowCenterContent(true);
        foreach (UIElement e in new UIElement[] { _ring, this })
        {
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = 1;
        }

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _scale.ScaleX = _scale.ScaleY = 1;
    }

    public override void SetBadge(MediaCategory? category, int count)
    {
        _countText.Text = count.ToString("N0", CultureInfo.CurrentCulture);
        _countBadge.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public override void SetTiles(IReadOnlyList<OverlayTile> tiles, string? summary = null, bool animate = true)
    {
        foreach (var view in _slices)
        {
            _ring.Children.Remove(view.Slice);
            _ring.Children.Remove(view.Labels);
        }

        _slices.Clear();
        _tiles = tiles.Take(Core.Overlay.OverlayModel.MaxTiles).ToList();
        _hovered = -1;
        SetCaption(summary);

        int count = _tiles.Count;
        double sweep = 2 * Math.PI / Math.Max(1, count);
        double stagger = OverlayTheme.Number("Motion.TileStaggerMs");
        for (int i = 0; i < count; i++)
        {
            double mid = (i * sweep) - (Math.PI / 2);
            var nudge = new TranslateTransform();
            var slice = new Path
            {
                Data = SliceGeometry(mid, sweep, count),
                StrokeThickness = 2 * _corner,
                StrokeLineJoin = PenLineJoin.Round,
                RenderTransform = nudge,
            };
            AutomationProperties.SetName(slice, _tiles[i].Detail is null ? $"Convert to {_tiles[i].Label}" : $"Convert {_tiles[i].Detail} files to {_tiles[i].Label}");

            StackPanel labels = BuildLabels(_tiles[i], sweep, mid);
            double labelRadius = (_inner + _outer) / 2;
            labels.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(labels, _radius + (labelRadius * Math.Cos(mid)) - (labels.DesiredSize.Width / 2));
            Canvas.SetTop(labels, _radius + (labelRadius * Math.Sin(mid)) - (labels.DesiredSize.Height / 2));
            labels.RenderTransform = nudge;

            _slices.Add((slice, nudge, labels));
            _ring.Children.Insert(_ring.Children.IndexOf(_center), slice);
            _ring.Children.Insert(_ring.Children.IndexOf(_center), labels);
            StyleSlice(i, hovered: false, animate: false);

            if (animate && Animate)
            {
                var fade = new DoubleAnimation(0, 1, OverlayTheme.Duration("Motion.ShowMs")) { BeginTime = TimeSpan.FromMilliseconds(i * stagger) };
                slice.Opacity = labels.Opacity = 0;
                slice.BeginAnimation(OpacityProperty, fade);
                labels.BeginAnimation(OpacityProperty, fade);
            }
        }
    }

    public override void SetCaption(string? text)
    {
        _summary = text ?? string.Empty;
        if (_hovered < 0)
        {
            ShowCenter(_summary, null);
        }
    }

    public override int HitTest(Point p)
    {
        int count = _tiles.Count;
        double dx = p.X - _radius;
        double dy = p.Y - _radius;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (count == 0 || distance < _inner || distance > _radius)
        {
            return -1;
        }

        // Clockwise from the top, shifted half a slice so each slice is centred on its angle (as OverlayModel does).
        double sweep = 2 * Math.PI / count;
        double angle = (Math.Atan2(dy, dx) + (Math.PI / 2) + (sweep / 2)) % (2 * Math.PI);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        return Math.Min((int)(angle / sweep), count - 1);
    }

    public override void SetHovered(int tile)
    {
        if (tile == _hovered || tile >= _slices.Count)
        {
            return;
        }

        if (_hovered >= 0)
        {
            StyleSlice(_hovered, hovered: false);
        }

        _hovered = tile;
        if (tile >= 0)
        {
            StyleSlice(tile, hovered: true);
            ShowCenter(_tiles[tile].Label, _tiles[tile].Detail is { } d ? $"{d} files" : null);
        }
        else
        {
            ShowCenter(_summary, null);
        }
    }

    public override void AnimateIn()
    {
        if (!Animate)
        {
            return;
        }

        var duration = OverlayTheme.Duration("Motion.ShowMs");
        double from = OverlayTheme.Number("Motion.ShowFromScale");
        _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut() });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = EaseOut() });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = EaseOut() });
    }

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

    public override void ShowWorking(int droppedTile)
    {
        for (int i = 0; i < _slices.Count; i++)
        {
            if (i == droppedTile)
            {
                continue;
            }

            foreach (UIElement e in new UIElement[] { _slices[i].Slice, _slices[i].Labels })
            {
                if (Animate)
                {
                    e.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, OverlayTheme.Duration("Motion.CollapseMs")));
                }
                else
                {
                    e.Opacity = 0.25;
                }
            }
        }

        ShowCenterContent(false);
        _spinner.Visibility = Visibility.Visible;
        Spin(_spin, on: true);
    }

    public override void ShowResult(bool success)
    {
        Spin(_spin, on: false);
        _spinner.Visibility = Visibility.Collapsed;
        _resultGlyph.Text = success ? OverlayTheme.Glyphs.Check : OverlayTheme.Glyphs.Failure;
        _resultGlyph.Foreground = success ? new SolidColorBrush(Token("Segments.Hover")) : new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        _resultGlyph.Visibility = Visibility.Visible;
    }

    private static Color Token(string key) => (Color)Application.Current.FindResource(key);

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    private static Point Polar(double centre, double radius, double angle) =>
        new(centre + (radius * Math.Cos(angle)), centre + (radius * Math.Sin(angle)));

    /// <summary>
    /// One ring segment inset by the corner radius: drawn with a stroke of twice that radius and round joins, which
    /// gives it rounded corners. The gap is a constant width, so the angular gap is wider at the inner edge.
    /// </summary>
    private Geometry SliceGeometry(double mid, double sweep, int count)
    {
        double outer = _outer - _corner;
        double inner = _inner + _corner;
        if (count == 1)
        {
            return new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(_radius, _radius), outer, outer),
                new EllipseGeometry(new Point(_radius, _radius), inner, inner));
        }

        double halfGap = (_gap / 2) + _corner;
        double outerHalf = (sweep / 2) - (halfGap / outer);
        double innerHalf = (sweep / 2) - (halfGap / inner);
        Point outerStart = Polar(_radius, outer, mid - outerHalf);
        Point outerEnd = Polar(_radius, outer, mid + outerHalf);
        Point innerEnd = Polar(_radius, inner, mid + innerHalf);
        Point innerStart = Polar(_radius, inner, mid - innerHalf);
        bool large = (2 * outerHalf) > Math.PI;
        var figure = new PathFigure(outerStart,
        [
            new ArcSegment(outerEnd, new Size(outer, outer), 0, large, SweepDirection.Clockwise, true),
            new LineSegment(innerEnd, true),
            new ArcSegment(innerStart, new Size(inner, inner), 0, (2 * innerHalf) > Math.PI, SweepDirection.Counterclockwise, true),
        ], closed: true);
        return new PathGeometry([figure]);
    }

    private StackPanel BuildLabels(OverlayTile tile, double sweep, double angle)
    {
        // Room across the slice at mid-radius: the chord for slices at the top and bottom, but only about the ring's
        // thickness for slices at the sides, whose long axis runs vertically. Blend by angle.
        double midRadius = (_inner + _outer) / 2;
        double chord = 2 * midRadius * Math.Sin(Math.Min(sweep, Math.PI) / 2);
        double thickness = _outer - _inner;
        double across = (chord * Math.Abs(Math.Sin(angle))) + (thickness * Math.Abs(Math.Cos(angle)));
        double maxWidth = Math.Max(30, Math.Min(across, chord) - _gap - 14);

        bool hasIcon = tile.Glyph is not null;
        string text = tile.Label.ToUpperInvariant();
        bool wraps = text.Contains(' ', StringComparison.Ordinal);
        double fontSize = FitFontSize(text, hasIcon ? 10.5 : 13, maxWidth, wraps);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        if (hasIcon)
        {
            stack.Children.Add(new TextBlock
            {
                Text = tile.Icon,
                FontFamily = OverlayTheme.IconFont,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = OverlayTheme.TextFont,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (tile.Detail is { } detail)
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                FontFamily = OverlayTheme.SmallFont,
                FontSize = 10,
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        return stack;
    }

    /// <summary>
    /// The largest size (down to 8 px) at which the label fits: every word within the width when it can wrap, the whole
    /// label otherwise.
    /// </summary>
    private double FitFontSize(string text, double preferred, double maxWidth, bool wraps)
    {
        string[] parts = wraps ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [text];
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(OverlayTheme.TextFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        for (double size = preferred; size > 8; size -= 0.5)
        {
            double widest = parts.Max(part => new FormattedText(part, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, dpi).WidthIncludingTrailingWhitespace);
            if (widest <= maxWidth)
            {
                return size;
            }
        }

        return 8;
    }

    private void StyleSlice(int index, bool hovered, bool animate = true)
    {
        var (slice, nudge, labels) = _slices[index];
        // Opaque colour with element opacity: fill and the rounding stroke blend as one layer, with no darker seam.
        Color color = hovered ? Token("Segments.Hover") : Colors.White;
        slice.Fill = slice.Stroke = new SolidColorBrush(color);
        slice.Opacity = hovered ? 1 : OverlayTheme.Number("Segments.SliceAlpha");
        slice.Effect = hovered ? new DropShadowEffect { Color = Token("Segments.Hover"), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.55 } : null;
        TextElement.SetForeground(labels, new SolidColorBrush(Token(hovered ? "Segments.HoverText" : "Segments.Text")));

        // The hovered slice moves a little outward along its angle.
        int count = _slices.Count;
        double mid = (index * 2 * Math.PI / Math.Max(1, count)) - (Math.PI / 2);
        double push = hovered && count > 1 ? OverlayTheme.Number("Segments.HoverPush") : 0;
        double toX = push * Math.Cos(mid);
        double toY = push * Math.Sin(mid);
        if (animate && Animate)
        {
            var duration = OverlayTheme.Duration("Motion.HoverMs");
            nudge.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(toX, duration) { EasingFunction = EaseOut() });
            nudge.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toY, duration) { EasingFunction = EaseOut() });
        }
        else
        {
            nudge.BeginAnimation(TranslateTransform.XProperty, null);
            nudge.BeginAnimation(TranslateTransform.YProperty, null);
            nudge.X = toX;
            nudge.Y = toY;
        }
    }

    private void ShowCenter(string text, string? detail)
    {
        _centerText.Text = text;
        _centerDetail.Text = detail ?? string.Empty;
        _centerDetail.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowCenterContent(bool visible)
    {
        Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
        _center.Children[1].Visibility = v;
        _spinner.Visibility = Visibility.Collapsed;
        _resultGlyph.Visibility = Visibility.Collapsed;
    }
}
