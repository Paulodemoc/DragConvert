namespace DragConvert.Native.Input;

public enum DragTransition
{
    None,
    Started,
    Ended,
}

/// <summary>
/// Left-button drag state machine fed from the low-level mouse hook.
/// Pure logic with no allocation, because it runs inside the hook callback.
/// </summary>
public sealed class DragDetector
{
    private readonly int _thresholdX;
    private readonly int _thresholdY;
    private bool _pressed;
    private bool _dragging;
    private int _startX;
    private int _startY;

    public DragDetector(int thresholdX, int thresholdY)
    {
        _thresholdX = Math.Max(1, thresholdX);
        _thresholdY = Math.Max(1, thresholdY);
    }

    public bool IsDragging => _dragging;

    public void ButtonDown(int x, int y)
    {
        _pressed = true;
        _dragging = false;
        _startX = x;
        _startY = y;
    }

    /// <summary>Returns <see cref="DragTransition.Started"/> once, when movement first leaves the drag rectangle.</summary>
    public DragTransition Move(int x, int y)
    {
        if (!_pressed || _dragging)
        {
            return DragTransition.None;
        }

        // Same rule as Windows: the drag starts once the cursor leaves the SM_CXDRAG x SM_CYDRAG rectangle.
        if (Math.Abs(x - _startX) > _thresholdX / 2 || Math.Abs(y - _startY) > _thresholdY / 2)
        {
            _dragging = true;
            return DragTransition.Started;
        }

        return DragTransition.None;
    }

    public DragTransition ButtonUp()
    {
        bool wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        return wasDragging ? DragTransition.Ended : DragTransition.None;
    }
}
