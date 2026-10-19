using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;
using DragConvert.Core.Overlay;

namespace DragConvert.App.Overlay;

/// <summary>
/// The "Glass" style: a clear frosted disc with a rim highlight, an inner disc holding a large icon of what's being
/// dragged, and round glass bubbles with labels around it; the hovered bubble turns bright and glows. No colour of its
/// own: a neutral darkening, scaled to how bright the backdrop is, keeps the white text readable.
/// Behind the tint sits a blurred copy of the screen area under the overlay, captured just before it appears: DWM's
/// blur only comes as a rectangle, so the frosted look is done here to keep the disc perfectly round.
/// </summary>
internal sealed class GlassOverlayView : OverlayView
{
    private readonly double _bubble;
    private readonly double _labelHeight;
    private readonly double _innerRadius;
    private readonly double _gap;
    private readonly Ellipse _scrim;
    private readonly Ellipse _sheen;
    private readonly EllipseGeometry _backdropClip;
    private readonly double _rimPadding;
    private readonly Image _backdrop = new() { Stretch = Stretch.None, IsHitTestVisible = false };
    private readonly Ellipse _disc;
    private readonly double _radius;
    private readonly Canvas _ring = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Grid _center;
    private readonly TextBlock _centerGlyph;
    private readonly Border _countBadge;
    private readonly TextBlock _countText;
    private readonly TextBlock _caption;
    private readonly Path _spinner;
    private readonly TextBlock _resultGlyph;
    private readonly List<(Grid Root, Ellipse Bubble, TextBlock Glyph)> _tileViews = [];
    private OverlayModel? _model;
    private IReadOnlyList<OverlayTile> _tiles = [];
    private int _hovered = -1;
    private string _summary = string.Empty;

    /// <summary>Extra screen captured around the disc so the blur has real pixels at its edge; cropped after blurring.</summary>
    public const double BackdropMargin = 32;

    public GlassOverlayView()
    {
        _bubble = OverlayTheme.Number("Glass.BubbleSize");
        _labelHeight = OverlayTheme.Number("Glass.LabelHeight");
        _innerRadius = OverlayTheme.Number("Glass.InnerRadius");
        _gap = OverlayTheme.Number("Glass.Gap");
        _rimPadding = OverlayTheme.Number("Glass.RimPadding");
        double frostHighlight = OverlayTheme.Number("Glass.FrostHighlight");
        double frostEdge = OverlayTheme.Number("Glass.FrostEdge");

        OverlayModel widest = CreateModel(OverlayModel.MaxTiles);
        _radius = widest.RingRadius + (TileBox / 2) + OverlayTheme.Number("Glass.RimPadding");
        Width = Height = 2 * _radius;
        Background = Brushes.Transparent;

        _ring.Width = _ring.Height = Width;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _ring.RenderTransform = _scale;

        // Frosted backdrop: the blurred screen capture (blurred before it gets here), clipped to the disc.
        _backdrop.Width = _backdrop.Height = Width;
        _backdrop.Stretch = Stretch.Fill;
        _backdropClip = new EllipseGeometry(new Point(_radius, _radius), _radius - 1, _radius - 1);
        _backdrop.Clip = _backdropClip;
        _backdrop.Visibility = Visibility.Collapsed;
        _ring.Children.Add(_backdrop);

        // Neutral darkening, set per show from the backdrop's brightness.
        _scrim = new Ellipse { Width = Width, Height = Height, Fill = Brushes.Black, Opacity = OverlayTheme.Number("Glass.ScrimNoBackdrop") };
        _ring.Children.Add(_scrim);

        // Frost: a faint white sheen, brighter towards the top-left like light hitting glass, and a bright rim.
        _disc = new Ellipse
        {
            Width = Width,
            Height = Height,
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.25),
                Center = new Point(0.5, 0.45),
                RadiusX = 0.75,
                RadiusY = 0.75,
                GradientStops =
                {
                    new GradientStop(WithAlpha(Colors.White, frostHighlight), 0),
                    new GradientStop(WithAlpha(Colors.White, frostEdge), 1),
                },
            },
            Stroke = Rim(0.85, 0.3),
            StrokeThickness = 1.6,
        };
        _ring.Children.Add(_disc);

        // Soft sheen across the upper half.
        _sheen = new Ellipse
        {
            Fill = new LinearGradientBrush(Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.5, 0), new Point(0.5, 0.55)),
            IsHitTestVisible = false,
        };
        _ring.Children.Add(_sheen);

        // Inner disc: a little darker, with its own faint rim.
        double inner = 2 * _innerRadius;
        var innerDisc = new Ellipse
        {
            Width = inner,
            Height = inner,
            Fill = new RadialGradientBrush(Color.FromArgb(18, 255, 255, 255), Color.FromArgb(40, 0, 0, 0)) { GradientOrigin = new Point(0.5, 0.3) },
            Stroke = Rim(0.55, 0.15),
            StrokeThickness = 1.2,
        };
        Place(innerDisc, Center, Center);
        _ring.Children.Add(innerDisc);

        // Centre: big icon, count badge, caption.
        _centerGlyph = new TextBlock
        {
            FontFamily = OverlayTheme.IconFont,
            FontSize = OverlayTheme.Number("Glass.CenterGlyphSize"),
            Foreground = new LinearGradientBrush(Colors.White, Color.FromRgb(0xE4, 0xE4, 0xE8), 90),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 1, Opacity = 0.45 },
        };
        _countText = new TextBlock
        {
            FontFamily = OverlayTheme.SmallFont,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Token("Glass.HoverIcon")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _countBadge = new Border
        {
            MinWidth = 22,
            Height = 22,
            Padding = new Thickness(5, 0, 5, 0),
            CornerRadius = new CornerRadius(11),
            Background = Brushes.White,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.4 },
            Child = _countText,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -12, -6),
            Visibility = Visibility.Collapsed,
        };
        var iconWithBadge = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        iconWithBadge.Children.Add(_centerGlyph);
        iconWithBadge.Children.Add(_countBadge);

        _caption = new TextBlock
        {
            FontFamily = OverlayTheme.SmallFont,
            FontSize = 11,
            Foreground = Brushes.White,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.7 },
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30,
            Width = inner - 26,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var centerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        centerStack.Children.Add(iconWithBadge);
        centerStack.Children.Add(_caption);

        double spinnerRadius = 20;
        var spin = new RotateTransform(0, spinnerRadius, spinnerRadius);
        _spinner = new Path
        {
            Width = 2 * spinnerRadius,
            Height = 2 * spinnerRadius,
            Stroke = Brushes.White,
            StrokeThickness = 3.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new PathGeometry([new PathFigure(new Point(spinnerRadius, 2), [new ArcSegment(new Point(2, spinnerRadius), new Size(spinnerRadius - 2, spinnerRadius - 2), 0, true, SweepDirection.Clockwise, true)], false)]),
            RenderTransform = spin,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        if (Animate)
        {
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        _resultGlyph = new TextBlock
        {
            FontFamily = OverlayTheme.IconFont,
            FontSize = 40,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        _center = new Grid { Width = inner, Height = inner };
        _center.Children.Add(centerStack);
        _center.Children.Add(_spinner);
        _center.Children.Add(_resultGlyph);
        Place(_center, Center, Center);
        _ring.Children.Add(_center);

        Children.Add(_ring);
        FitDisc();
    }

    public override bool WantsBackdrop => true;

    public override void SetBackdrop(BitmapSource? snapshot, double brightness)
    {
        // Over a dark backdrop the glass stays nearly clear; over a bright one it darkens just enough for white text.
        _backdrop.Source = snapshot;
        _backdrop.Visibility = snapshot is null ? Visibility.Collapsed : Visibility.Visible;
        _scrim.Opacity = snapshot is null
            ? OverlayTheme.Number("Glass.ScrimNoBackdrop")
            : OverlayTheme.Number("Glass.ScrimBase") + (OverlayTheme.Number("Glass.ScrimPerBrightness") * Math.Clamp(brightness, 0, 1));
    }

    private double Center => _radius;

    /// <summary>Bubble plus the label under it: the box each tile occupies on the ring.</summary>
    private double TileBox => _bubble + _labelHeight;

    public override void Reset()
    {
        SetTiles([], animate: false);
        SetCaption(null);
        _countBadge.Visibility = Visibility.Collapsed;
        _centerGlyph.Text = OverlayTheme.Glyphs.Files;
        ShowCenter(content: true);
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
        // A file icon for a single file (category glyph), a folder for mixed drags.
        _centerGlyph.Text = category is { } c ? OverlayTheme.Glyph(c) : OverlayTheme.Glyphs.Files;
        _countText.Text = count.ToString("N0", CultureInfo.CurrentCulture);
        _countBadge.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public override void SetTiles(IReadOnlyList<OverlayTile> tiles, string? summary = null, bool animate = true)
    {
        foreach (var view in _tileViews)
        {
            _ring.Children.Remove(view.Root);
        }

        _tileViews.Clear();
        _tiles = tiles.Take(OverlayModel.MaxTiles).ToList();
        _model = _tiles.Count > 0 ? CreateModel(_tiles.Count) : null;
        _hovered = -1;
        FitDisc();
        SetCaption(summary);
        if (_model is null)
        {
            return;
        }

        double stagger = OverlayTheme.Number("Motion.TileStaggerMs");
        for (int i = 0; i < _tiles.Count; i++)
        {
            var view = BuildTile(_tiles[i]);
            var (cx, cy) = _model.TileCenter(i);
            Place(view.Root, Center + cx, Center + cy);
            _tileViews.Add(view);
            _ring.Children.Add(view.Root);

            if (animate && Animate)
            {
                var begin = TimeSpan.FromMilliseconds(i * stagger);
                var duration = OverlayTheme.Duration("Motion.ShowMs");
                view.Root.Opacity = 0;
                view.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = EaseOut() });
            }
        }
    }

    public override void SetCaption(string? text)
    {
        _summary = text ?? string.Empty;
        if (_hovered < 0)
        {
            _caption.Text = _summary;
        }
    }

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
            _caption.Text = t.Detail is null ? $"Convert to {t.Label}" : $"{t.Label} for {t.Detail} files";
        }
        else
        {
            _caption.Text = _summary;
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
        // The bubbles fade away and the centre turns into the spinner.
        foreach (var view in _tileViews)
        {
            if (Animate)
            {
                view.Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, OverlayTheme.Duration("Motion.CollapseMs")));
            }
            else
            {
                view.Root.Opacity = 0;
            }
        }

        ShowCenter(content: false);
        _spinner.Visibility = Visibility.Visible;
    }

    public override void ShowResult(bool success)
    {
        _spinner.Visibility = Visibility.Collapsed;
        _resultGlyph.Text = success ? OverlayTheme.Glyphs.Check : OverlayTheme.Glyphs.Failure;
        _resultGlyph.Foreground = success ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xBC));
        _resultGlyph.Visibility = Visibility.Visible;
        if (Animate)
        {
            var pop = new ScaleTransform(1, 1);
            _resultGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
            _resultGlyph.RenderTransform = pop;
            var grow = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    private static Color Token(string key) => (Color)Application.Current.FindResource(key);

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static LinearGradientBrush Rim(double top, double bottom) =>
        new(Color.FromArgb((byte)(top * 255), 255, 255, 255), Color.FromArgb((byte)(bottom * 255), 255, 255, 255), new Point(0.5, 0), new Point(0.5, 1));

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    private static void Place(FrameworkElement element, double centerX, double centerY)
    {
        SetLeft(element, centerX - (element.Width / 2));
        SetTop(element, centerY - (element.Height / 2));
    }

    private OverlayModel CreateModel(int tiles) => new(tiles, TileBox, _innerRadius, _gap);

    /// <summary>
    /// Sizes the glass to the tiles actually shown: the farthest point of any bubble or label, plus the rim padding.
    /// The window stays sized for eight tiles; outside the disc it's transparent, so clicks and drops pass through.
    /// </summary>
    private void FitDisc()
    {
        double radius = _innerRadius + _gap + _bubble + _rimPadding;
        if (_model is not null)
        {
            const double LabelHalfWidth = 30;
            radius = 0;
            for (int i = 0; i < _model.TileCount; i++)
            {
                var (cx, cy) = _model.TileCenter(i);

                // Bubble: top part of the tile box. Label: a strip along its bottom edge.
                double bubbleY = cy - (_labelHeight / 2);
                radius = Math.Max(radius, Math.Sqrt((cx * cx) + (bubbleY * bubbleY)) + (_bubble / 2));
                double labelBottom = cy + (TileBox / 2);
                double labelX = Math.Abs(cx) + LabelHalfWidth;
                radius = Math.Max(radius, Math.Sqrt((labelX * labelX) + (labelBottom * labelBottom)));
            }

            radius += _rimPadding;
        }

        radius = Math.Min(radius, _radius);
        foreach (Ellipse layer in new[] { _scrim, _disc })
        {
            layer.Width = layer.Height = 2 * radius;
            Place(layer, Center, Center);
        }

        _sheen.Width = _sheen.Height = (2 * radius) - 8;
        Place(_sheen, Center, Center);
        _backdropClip.RadiusX = _backdropClip.RadiusY = radius - 1;
    }

    private void ShowCenter(bool content)
    {
        _center.Children[0].Visibility = content ? Visibility.Visible : Visibility.Collapsed;
        _spinner.Visibility = Visibility.Collapsed;
        _resultGlyph.Visibility = Visibility.Collapsed;
    }

    private (Grid Root, Ellipse Bubble, TextBlock Glyph) BuildTile(OverlayTile tile)
    {
        var bubble = new Ellipse { Width = _bubble, Height = _bubble, StrokeThickness = 1.3 };
        var glyph = new TextBlock
        {
            Text = OverlayTheme.Glyph(tile.Category),
            FontFamily = OverlayTheme.IconFont,
            FontSize = OverlayTheme.Number("Glass.BubbleGlyphSize"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var bubbleGrid = new Grid
        {
            Width = _bubble,
            Height = _bubble,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            VerticalAlignment = VerticalAlignment.Top,
        };
        bubbleGrid.Children.Add(bubble);
        bubbleGrid.Children.Add(glyph);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        var labelShadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.75 };
        labels.Children.Add(new TextBlock
        {
            Text = tile.Label,
            FontFamily = OverlayTheme.TextFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = labelShadow,
        });
        if (tile.Detail is { } detail)
        {
            labels.Children.Add(new TextBlock
            {
                Text = detail,
                FontFamily = OverlayTheme.SmallFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
                Effect = labelShadow,
            });
        }

        var root = new Grid { Width = TileBox, Height = TileBox };
        root.Children.Add(bubbleGrid);
        root.Children.Add(labels);
        AutomationProperties.SetName(root, tile.Detail is null ? $"Convert to {tile.Label}" : $"Convert {tile.Detail} files to {tile.Label}");

        var view = (root, bubble, glyph);
        StyleTile(view, hovered: false, animate: false);
        return view;
    }

    private void StyleTile((Grid Root, Ellipse Bubble, TextBlock Glyph) view, bool hovered, bool animate = true)
    {
        view.Bubble.Fill = hovered
            ? new RadialGradientBrush(Colors.White, Color.FromRgb(0xE6, 0xE6, 0xEA)) { GradientOrigin = new Point(0.4, 0.3) }
            : new LinearGradientBrush(Color.FromArgb(64, 255, 255, 255), Color.FromArgb(24, 255, 255, 255), 90);
        view.Bubble.Stroke = hovered ? Brushes.White : Rim(0.7, 0.25);
        view.Glyph.Foreground = hovered ? new SolidColorBrush(Token("Glass.HoverIcon")) : Brushes.White;
        var bubbleGrid = (Grid)view.Root.Children[0];
        bubbleGrid.Effect = hovered ? new DropShadowEffect { Color = Colors.White, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.85 } : null;

        double to = hovered ? OverlayTheme.Number("Glass.HoverScale") : 1;
        var scale = (ScaleTransform)bubbleGrid.RenderTransform;
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
}
