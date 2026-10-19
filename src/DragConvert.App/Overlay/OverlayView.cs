using System.Windows;
using System.Windows.Controls;
using DragConvert.Core.Conversion;

namespace DragConvert.App.Overlay;

/// <summary>One tile: its label, an optional second line such as "3 of 5", and the category that picks its icon.</summary>
internal sealed record OverlayTile(string Label, string? Detail, MediaCategory Category);

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
