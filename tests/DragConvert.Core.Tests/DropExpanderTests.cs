using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tests;

public sealed class DropExpanderTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Make(string relative)
    {
        string path = Path.Combine(_folder.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private string Rel(string path) => Path.GetRelativePath(_folder.Path, path).Replace('\\', '/');

    [Fact]
    public void Plain_files_pass_through()
    {
        string a = Make("a.jpg");
        string b = Make("b.mp4");

        DropSet set = DropExpander.Expand([a, b], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([a, b], set.Files);
        Assert.Equal(0, set.FolderCount);
        Assert.False(set.Truncated);
    }

    [Fact]
    public void Folders_expand_recursively_files_before_subfolders_in_name_order()
    {
        Make("Photos/b.jpg");
        Make("Photos/a.jpg");
        Make("Photos/2024/c.heic");
        Make("Photos/2024/deep/d.png");

        DropSet set = DropExpander.Expand([Path.Combine(_folder.Path, "Photos")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Photos/a.jpg", "Photos/b.jpg", "Photos/2024/c.heic", "Photos/2024/deep/d.png"], set.Files.Select(Rel));
        Assert.Equal(1, set.FolderCount);
    }

    [Fact]
    public void Files_dropped_alongside_their_folder_are_not_duplicated()
    {
        string inside = Make("Photos/a.jpg");
        DropSet set = DropExpander.Expand([Path.Combine(_folder.Path, "Photos"), inside], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(set.Files);
    }

    [Fact]
    public void Our_temp_files_and_system_files_are_skipped()
    {
        Make("Photos/a.jpg");
        Make("Photos/.a.1234abcd" + OutputPaths.TempMarker + ".png");
        string system = Make("Photos/desktop.ini");
        File.SetAttributes(system, FileAttributes.System | FileAttributes.Hidden);

        DropSet set = DropExpander.Expand([Path.Combine(_folder.Path, "Photos")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Photos/a.jpg"], set.Files.Select(Rel));
    }

    [Fact]
    public void The_cap_truncates_and_says_so()
    {
        for (int i = 0; i < 5; i++)
        {
            Make($"Many/{i}.jpg");
        }

        DropSet set = DropExpander.Expand([Path.Combine(_folder.Path, "Many")], maxFiles: 3, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, set.Files.Count);
        Assert.True(set.Truncated);
    }

    [Fact]
    public void Missing_paths_are_ignored() =>
        Assert.Empty(DropExpander.Expand([Path.Combine(_folder.Path, "gone.jpg")], cancellationToken: TestContext.Current.CancellationToken).Files);
}
