using System.Text.Json;

namespace DragConvert.Core.Instance;

/// <summary>
/// What a second launch (Send To, a shortcut, the shell extension later) forwards to the running tray process over
/// the named pipe: one JSON object per line.
/// </summary>
public sealed record InstanceMessage(string Command, IReadOnlyList<string> Paths)
{
    /// <summary>Open the picker for these files.</summary>
    public const string OpenCommand = "open";

    /// <summary>Bring up the tray window (launched with no files).</summary>
    public const string ShowCommand = "show";

    public static InstanceMessage Open(IReadOnlyList<string> paths) => new(OpenCommand, paths);

    public static InstanceMessage Show() => new(ShowCommand, []);

    public string Serialize() => JsonSerializer.Serialize(new Wire(Command, Paths.ToArray()));

    public static InstanceMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            Wire? wire = JsonSerializer.Deserialize<Wire>(line);
            return wire?.Command is { Length: > 0 } command ? new InstanceMessage(command, wire.Paths ?? []) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Wire(string Command, string[]? Paths);
}
