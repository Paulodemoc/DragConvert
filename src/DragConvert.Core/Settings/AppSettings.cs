using System.Text.Json;
using System.Text.Json.Serialization;
using DragConvert.Core.Input;

namespace DragConvert.Core.Settings;

public enum OutputLocation
{
    /// <summary>Beside each original (default).</summary>
    SameFolder,

    /// <summary>Everything into <see cref="AppSettings.OutputFolder"/>.</summary>
    FixedFolder,
}

public sealed record AppSettings
{
    /// <summary>Modifier chord that shows the overlay during a drag; <see cref="Modifiers.None"/> turns the overlay off.</summary>
    public Modifiers Trigger { get; init; } = Modifiers.Control | Modifiers.Shift;

    public OutputLocation Output { get; init; } = OutputLocation.SameFolder;

    public string? OutputFolder { get; init; }

    public bool StartWithWindows { get; init; }

    public bool SendToMenu { get; init; } = true;

    /// <summary>"layered" or "dwm"; applied at the next start.</summary>
    public string Renderer { get; init; } = "layered";

    /// <summary>Folder outputs go to, or null for "beside each original".</summary>
    public string? OutputDirectory =>
        Output == OutputLocation.FixedFolder && !string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : null;
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. A missing or unreadable file yields defaults.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Json) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes to a temp file and renames it, so a crash mid-save never leaves a truncated settings file.</summary>
    public void Save(AppSettings settings)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, Path, overwrite: true);
    }
}
