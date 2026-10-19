using DragConvert.Native;

namespace DragConvert.Native.Tests;

public class DragMetricsTests
{
    [Fact]
    public void Drag_thresholds_are_positive()
    {
        Assert.True(DragMetrics.ThresholdX > 0);
        Assert.True(DragMetrics.ThresholdY > 0);
    }
}
