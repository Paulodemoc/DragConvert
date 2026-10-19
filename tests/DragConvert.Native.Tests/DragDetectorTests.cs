using DragConvert.Native.Input;

namespace DragConvert.Native.Tests;

public class DragDetectorTests
{
    private readonly DragDetector _detector = new(thresholdX: 4, thresholdY: 4);

    [Fact]
    public void Small_movement_is_not_a_drag()
    {
        _detector.ButtonDown(100, 100);
        Assert.Equal(DragTransition.None, _detector.Move(102, 102));
        Assert.Equal(DragTransition.None, _detector.ButtonUp());
    }

    [Fact]
    public void Leaving_the_drag_rectangle_starts_a_drag_once()
    {
        _detector.ButtonDown(100, 100);
        Assert.Equal(DragTransition.Started, _detector.Move(103, 100));
        Assert.Equal(DragTransition.None, _detector.Move(150, 150));
        Assert.True(_detector.IsDragging);
    }

    [Fact]
    public void Releasing_after_a_drag_ends_it()
    {
        _detector.ButtonDown(100, 100);
        _detector.Move(100, 110);
        Assert.Equal(DragTransition.Ended, _detector.ButtonUp());
        Assert.False(_detector.IsDragging);
    }

    [Fact]
    public void Movement_without_a_button_press_is_ignored() =>
        Assert.Equal(DragTransition.None, _detector.Move(500, 500));

    [Fact]
    public void A_new_press_resets_the_start_point()
    {
        _detector.ButtonDown(100, 100);
        _detector.ButtonUp();
        _detector.ButtonDown(300, 300);
        Assert.Equal(DragTransition.None, _detector.Move(301, 301));
    }
}
