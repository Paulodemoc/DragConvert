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

    /// <summary>
    /// A chord for a trigger with a tools key that isn't part of it: Alt normally, Shift when the trigger already
    /// uses Alt (Ctrl+Alt, Shift+Alt).
    /// </summary>
    public static ModifierChord ForTrigger(Modifiers trigger) =>
        new(trigger, trigger.HasFlag(Modifiers.Alt) ? (trigger.HasFlag(Modifiers.Shift) ? Modifiers.Control : Modifiers.Shift) : Modifiers.Alt);

    /// <summary>Name of the tools key for hints: "Alt", "Shift" or "Ctrl".</summary>
    public string ToolsKeyName => ToolsModifier switch
    {
        Modifiers.Shift => "Shift",
        Modifiers.Control => "Ctrl",
        _ => "Alt",
    };

    public bool IsTriggerHeld(KeyState keys) => Trigger != Modifiers.None && (keys.Modifiers & Trigger) == Trigger;

    /// <summary>True when the tools key is down, with or without the trigger: once the overlay is open, it alone switches to tools.</summary>
    public bool IsToolsKeyHeld(KeyState keys) => ToolsModifier != Modifiers.None && (keys.Modifiers & ToolsModifier) == ToolsModifier;

    /// <summary>Trigger and tools key together: the overlay opens straight in tools mode.</summary>
    public bool IsToolsMode(KeyState keys) => IsTriggerHeld(keys) && IsToolsKeyHeld(keys);
}
