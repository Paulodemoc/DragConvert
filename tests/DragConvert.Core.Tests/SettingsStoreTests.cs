using DragConvert.Core.Input;
using DragConvert.Core.Settings;

namespace DragConvert.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Missing_file_gives_defaults()
    {
        AppSettings settings = new SettingsStore(Path.Combine(_folder.Path, "settings.json")).Load();
        Assert.Equal(new AppSettings(), settings);
        Assert.Equal(Modifiers.Control | Modifiers.Shift, settings.Trigger);
        Assert.Null(settings.OutputDirectory);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var store = new SettingsStore(Path.Combine(_folder.Path, "sub", "settings.json"));
        var settings = new AppSettings
        {
            Trigger = Modifiers.Control | Modifiers.Alt,
            Output = OutputLocation.FixedFolder,
            OutputFolder = @"D:\Converted",
            StartWithWindows = true,
            SendToMenu = false,
            Renderer = "dwm",
        };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
        Assert.Contains("\"FixedFolder\"", File.ReadAllText(store.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        string path = _folder.File("settings.json", "{ not json");
        Assert.Equal(new AppSettings(), new SettingsStore(path).Load());
    }

    [Fact]
    public void Fixed_folder_without_a_path_falls_back_to_beside_the_original() =>
        Assert.Null(new AppSettings { Output = OutputLocation.FixedFolder, OutputFolder = " " }.OutputDirectory);
}
