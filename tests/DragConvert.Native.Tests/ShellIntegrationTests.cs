using System.Text;
using DragConvert.Native.Shell;

namespace DragConvert.Native.Tests;

public sealed class ShellIntegrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dragconvert-shell-tests", Guid.NewGuid().ToString("N"));

    public ShellIntegrationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Creates_a_shortcut_pointing_at_the_target_with_arguments()
    {
        string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string link = Path.Combine(_folder, "Test.lnk");

        ShellIntegration.CreateShortcut(link, target, "--send-to", "Convert with DragConvert");

        Assert.True(File.Exists(link));
        // .lnk files store the target and arguments as UTF-16 strings.
        string content = Encoding.Unicode.GetString(File.ReadAllBytes(link));
        Assert.Contains("--send-to", content, StringComparison.Ordinal);
        Assert.Contains("Convert with DragConvert", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Recreating_a_shortcut_replaces_it()
    {
        string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string link = Path.Combine(_folder, "Test.lnk");

        ShellIntegration.CreateShortcut(link, target, "--first", "one");
        ShellIntegration.CreateShortcut(link, target, "--second", "two");

        string content = Encoding.Unicode.GetString(File.ReadAllBytes(link));
        Assert.Contains("--second", content, StringComparison.Ordinal);
        Assert.DoesNotContain("--first", content, StringComparison.Ordinal);
    }
}
