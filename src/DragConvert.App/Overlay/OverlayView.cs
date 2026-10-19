using System.Windows;
using System.Windows.Controls;
using DragConvert.Core.Conversion;

namespace DragConvert.App.Overlay;

/// <summary>
/// One tile: its label, an optional second line such as "3 of 5", and its icon: the category's, or a tool's own glyph.
/// </summary>
internal sealed record OverlayTile(string Label, string? Detail, MediaCategory Category, string? Glyph = null)
{
    public string Icon => Glyph ?? Theme.OverlayTheme.Glyph(Category);
}

/// <summary>
/// What every overlay style draws: tiles around a centre, a badge for what's being dragged, a caption, and the
/// status shown after a drop. The overlay window, the UI gallery and the snapshot tool all use it, so they show
/// exactly the same visuals.
/// </summary>
internal abstract class OverlayView : Canvas
{
    /// <summary>Forces animations on or off (the snapshot tool turns them off); null follows the Windows setting.</summary>
    public static bool? AnimationsOverride { get; set; }

    /// <summary>True for styles that draw a blurred copy of the screen behind them (see <see cref="SetBackdrop"/>).</summary>
    public virtual bool WantsBackdrop => false;

    protected static bool Animate => AnimationsOverride ?? SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// Starts or stops a spinner's rotation. Only runs while a conversion is shown: a forever animation keeps WPF
    /// rendering frames even on a hidden window, which costs CPU while the app sits idle.
    /// </summary>
    protected static void Spin(System.Windows.Media.RotateTransform transform, bool on)
    {
        if (on && Animate)
        {
            transform.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }
        else
        {
            transform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }
    }

    /// <summary>
    /// The screen area behind the overlay, captured just before it appears and already blurred, exactly the size of the
    /// view, with its average brightness (0 to 1); null when the capture failed. Only called when <see cref="WantsBackdrop"/>.
    /// </summary>
    public virtual void SetBackdrop(System.Windows.Media.Imaging.BitmapSource? snapshot, double brightness)
    {
    }

    /// <summary>Back to the just-shown state: no tiles, no badge, no caption, no status.</summary>
    public abstract void Reset();

    /// <summary>Centre badge: the dragged files' category (null for mixed) and how many there are.</summary>
    public abstract void SetBadge(MediaCategory? category, int count);

    public abstract void SetTiles(IReadOnlyList<OverlayTile> tiles, string? summary = null, bool animate = true);

    public abstract void SetCaption(string? text);

    /// <summary>The tile under a point in this view's coordinates, or -1.</summary>
    public abstract int HitTest(Point p);

    public abstract void SetHovered(int tile);

    public abstract void AnimateIn();

    public abstract void AnimateOut(Action completed);

    /// <summary>After a drop: tiles go away and a spinner shows the conversion running.</summary>
    public abstract void ShowWorking(int droppedTile);

    /// <summary>Replaces the spinner with a check mark (success) or a cross (failure).</summary>
    public abstract void ShowResult(bool success);
}
