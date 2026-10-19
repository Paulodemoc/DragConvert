using DragConvert.Core.Input;

namespace DragConvert.Core.Tests;

public class ModifierChordTests
{
    private static readonly ModifierChord Chord = ModifierChord.Default;

    [Fact]
    public void Ctrl_shift_holds_the_trigger() =>
        Assert.True(Chord.IsTriggerHeld(new KeyState(Control: true, Shift: true, Alt: false, Escape: false)));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Partial_chord_does_not_hold_the_trigger(bool control, bool shift) =>
        Assert.False(Chord.IsTriggerHeld(new KeyState(control, shift, Alt: false, Escape: false)));

    [Fact]
    public void Alt_with_trigger_is_tools_mode()
    {
        var keys = new KeyState(Control: true, Shift: true, Alt: true, Escape: false);
        Assert.True(Chord.IsTriggerHeld(keys));
        Assert.True(Chord.IsToolsMode(keys));
    }

    [Fact]
    public void Alt_without_trigger_is_not_tools_mode() =>
        Assert.False(Chord.IsToolsMode(new KeyState(Control: false, Shift: false, Alt: true, Escape: false)));

    [Fact]
    public void Empty_trigger_never_holds() =>
        Assert.False(new ModifierChord(Modifiers.None, Modifiers.Alt).IsTriggerHeld(new KeyState(true, true, true, false)));
}
