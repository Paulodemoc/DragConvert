using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tests;

public sealed class OutputPathsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Output_goes_beside_the_original_with_the_new_extension()
    {
        string input = _folder.File("IMG_0001.heic");
        Assert.Equal(Path.Combine(_folder.Path, "IMG_0001.jpg"), OutputPaths.PlanFinal(input, MediaFormats.Jpeg));
    }

    [Fact]
    public void Collisions_append_a_counter_and_never_overwrite()
    {
        string input = _folder.File("photo.heic");
        _folder.File("photo.jpg");
        _folder.File("photo (1).jpg");
        Assert.Equal(Path.Combine(_folder.Path, "photo (2).jpg"), OutputPaths.PlanFinal(input, MediaFormats.Jpeg));
    }

    [Fact]
    public void Same_format_output_never_targets_the_input_itself()
    {
        string input = _folder.File("photo.jpg");
        Assert.Equal(Path.Combine(_folder.Path, "photo (1).jpg"), OutputPaths.PlanFinal(input, MediaFormats.Jpeg));
    }

    [Fact]
    public void Output_directory_overrides_the_input_folder()
    {
        string input = _folder.File("a.png");
        string outDir = Directory.CreateDirectory(Path.Combine(_folder.Path, "out")).FullName;
        Assert.Equal(Path.Combine(outDir, "a.webp"), OutputPaths.PlanFinal(input, MediaFormats.WebP, outDir));
    }

    [Fact]
    public void Temp_files_sit_beside_the_output_keep_its_extension_and_are_recognisable()
    {
        string final = Path.Combine(_folder.Path, "photo.jpg");
        string temp = OutputPaths.TempFor(final);
        Assert.Equal(_folder.Path, Path.GetDirectoryName(temp));
        Assert.EndsWith(".jpg", temp, StringComparison.Ordinal);
        Assert.True(OutputPaths.IsTempFile(temp));
        Assert.False(OutputPaths.IsTempFile(final));
        Assert.NotEqual(temp, OutputPaths.TempFor(final));
    }
}
