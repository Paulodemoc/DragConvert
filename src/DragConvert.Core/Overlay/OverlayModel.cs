namespace DragConvert.Core.Overlay;

/// <summary>
/// Ring geometry and hit testing for the overlay, in device-independent pixels relative to the ring centre.
/// Has no UI types so any renderer (WPF now, Direct2D later) can draw from it.
/// </summary>
public sealed class OverlayModel
{
    public const int MaxTiles = 8;

    public OverlayModel(int tileCount, double tileSize = 56, double deadZoneRadius = 48, double gap = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tileCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tileCount, MaxTiles);

        TileCount = tileCount;
        TileSize = tileSize;
        DeadZoneRadius = deadZoneRadius;
        RingRadius = deadZoneRadius + gap + (tileSize / 2);
        OuterRadius = RingRadius + (tileSize / 2) + gap;
    }

    public int TileCount { get; }

    public double TileSize { get; }

    public double DeadZoneRadius { get; }

    /// <summary>Distance from the centre to each tile's centre.</summary>
    public double RingRadius { get; }

    /// <summary>Edge of the hittable annulus; also half the ring's bounding box.</summary>
    public double OuterRadius { get; }

    private double SectorAngle => 2 * Math.PI / TileCount;

    /// <summary>Tile centre offset from the ring centre. Tile 0 sits at the top; tiles go clockwise.</summary>
    public (double X, double Y) TileCenter(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TileCount);

        double angle = (index * SectorAngle) - (Math.PI / 2);
        return (RingRadius * Math.Cos(angle), RingRadius * Math.Sin(angle));
    }

    /// <summary>
    /// Returns the tile under an offset from the centre, or -1 inside the dead zone or outside the ring.
    /// Each tile owns its whole angular sector, so the gaps between tiles still count as hits.
    /// </summary>
    public int HitTest(double dx, double dy)
    {
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance < DeadZoneRadius || distance > OuterRadius)
        {
            return -1;
        }

        // Angle measured clockwise from the top, shifted by half a sector so each sector is centred on its tile.
        double angle = Math.Atan2(dy, dx) + (Math.PI / 2) + (SectorAngle / 2);
        angle %= 2 * Math.PI;
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        return Math.Min((int)(angle / SectorAngle), TileCount - 1);
    }
}
