namespace DragConvert.Core.Tests;

/// <summary>A fresh temp folder per test, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dragconvert-tests", Guid.NewGuid().ToString("N"));

    public string File(string name, string content = "source")
    {
        string path = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public string[] Files() => Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
