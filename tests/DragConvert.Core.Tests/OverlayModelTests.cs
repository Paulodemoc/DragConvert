using DragConvert.Core.Overlay;

namespace DragConvert.Core.Tests;

public class OverlayModelTests
{
    [Fact]
    public void Tile_zero_is_at_the_top()
    {
        var model = new OverlayModel(8);
        var (x, y) = model.TileCenter(0);
        Assert.Equal(0, x, 6);
        Assert.Equal(-model.RingRadius, y, 6);
    }

    [Fact]
    public void Tiles_go_clockwise()
    {
        var model = new OverlayModel(4);
        var (x, y) = model.TileCenter(1);
        Assert.Equal(model.RingRadius, x, 6);
        Assert.Equal(0, y, 6);
    }

    [Fact]
    public void Dead_zone_and_outside_ring_hit_nothing()
    {
        var model = new OverlayModel(8);
        Assert.Equal(-1, model.HitTest(0, 0));
        Assert.Equal(-1, model.HitTest(model.DeadZoneRadius - 1, 0));
        Assert.Equal(-1, model.HitTest(model.OuterRadius + 1, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void Every_tile_centre_hits_its_own_tile(int count)
    {
        var model = new OverlayModel(count);
        for (int i = 0; i < count; i++)
        {
            var (x, y) = model.TileCenter(i);
            Assert.Equal(i, model.HitTest(x, y));
        }
    }

    [Fact]
    public void Gap_between_tiles_belongs_to_the_nearer_tile()
    {
        var model = new OverlayModel(4);
        // Slightly clockwise of the top tile, between tile 0 (top) and tile 1 (right).
        double angle = (-Math.PI / 2) + 0.3;
        Assert.Equal(0, model.HitTest(model.RingRadius * Math.Cos(angle), model.RingRadius * Math.Sin(angle)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Tile_count_must_be_between_one_and_eight(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayModel(count));
}
