namespace DragConvert.Core.Input;

[Flags]
public enum Modifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>Snapshot of the keys the overlay cares about.</summary>
public readonly record struct KeyState(bool Control, bool Shift, bool Alt, bool Escape)
{
    public Modifiers Modifiers =>
        (Control ? Modifiers.Control : Modifiers.None)
        | (Shift ? Modifiers.Shift : Modifiers.None)
        | (Alt ? Modifiers.Alt : Modifiers.None);
}

/// <summary>
/// The trigger chord (default Ctrl+Shift) and the extra key that switches the overlay to tools (default Alt).
/// </summary>
public sealed record ModifierChord(Modifiers Trigger, Modifiers ToolsModifier)
{
    public static ModifierChord Default { get; } = new(Modifiers.Control | Modifiers.Shift, Modifiers.Alt);

    public bool IsTriggerHeld(KeyState keys) => Trigger != Modifiers.None && (keys.Modifiers & Trigger) == Trigger;

    public bool IsToolsMode(KeyState keys) =>
        IsTriggerHeld(keys) && ToolsModifier != Modifiers.None && (keys.Modifiers & ToolsModifier) == ToolsModifier;
}
