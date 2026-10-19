using DragConvert.Core.Conversion;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Core.Tests;

public sealed class JobQueueTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static JobQueue Queue(FakeProvider provider, int parallelism = 2) => new(new FormatCatalog([provider]), parallelism);

    private static FakeProvider PngToJpeg() => new("fake", new ConversionCapability(Png, Jpeg));

    [Fact]
    public async Task Success_writes_a_new_file_beside_the_original_and_leaves_the_source_untouched()
    {
        string input = _folder.File("photo.png", "original bytes");
        using var queue = Queue(PngToJpeg());

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(_folder.Path, "photo.jpg"), result.OutputPath);
        Assert.Equal("converted to jpeg", await File.ReadAllTextAsync(result.OutputPath!, TestContext.Current.CancellationToken));
        Assert.Equal("original bytes", await File.ReadAllTextAsync(input, TestContext.Current.CancellationToken));
        Assert.Equal(["photo.jpg", "photo.png"], _folder.Files());
    }

    [Fact]
    public async Task Provider_writes_to_a_temp_path_not_the_final_name()
    {
        string input = _folder.File("photo.png");
        var provider = PngToJpeg();
        using var queue = Queue(provider);

        await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        ConversionJob job = Assert.Single(provider.Jobs);
        Assert.True(OutputPaths.IsTempFile(job.OutputPath));
        Assert.Equal(Path.GetFullPath(input), job.InputPath);
    }

    [Fact]
    public async Task Existing_output_is_never_overwritten()
    {
        string input = _folder.File("photo.png");
        _folder.File("photo.jpg", "keep me");
        using var queue = Queue(PngToJpeg());

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_folder.Path, "photo (1).jpg"), result.OutputPath);
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(_folder.Path, "photo.jpg"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failure_reports_the_reason_and_leaves_no_files_behind()
    {
        string input = _folder.File("photo.png");
        using var queue = Queue(new FakeProvider("fake", new ConversionCapability(Png, Jpeg)) { FailWith = "Corrupt image" });

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal("Corrupt image", result.Error);
        Assert.Equal(["photo.png"], _folder.Files());
    }

    [Fact]
    public async Task Empty_output_counts_as_failure()
    {
        string input = _folder.File("photo.png");
        using var queue = Queue(new FakeProvider("fake", new ConversionCapability(Png, Jpeg)) { WriteNothing = true });

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(["photo.png"], _folder.Files());
    }

    [Fact]
    public async Task Cancellation_reports_cancelled_and_cleans_up()
    {
        string input = _folder.File("photo.png");
        using var queue = Queue(new FakeProvider("fake", new ConversionCapability(Png, Jpeg)) { Delay = TimeSpan.FromSeconds(30) });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: cts.Token);

        Assert.Equal(ConversionStatus.Cancelled, result.Status);
        Assert.Equal(["photo.png"], _folder.Files());
    }

    [Fact]
    public async Task Unsupported_conversion_fails_with_a_readable_reason()
    {
        string input = _folder.File("voice.wav");
        using var queue = Queue(PngToJpeg());

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal("Can't convert WAV to JPEG.", result.Error);
    }

    [Fact]
    public async Task Missing_input_fails_cleanly()
    {
        using var queue = Queue(PngToJpeg());
        ConversionResult result = await queue.ConvertAsync(Path.Combine(_folder.Path, "gone.png"), Jpeg, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ConversionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Parallelism_is_bounded()
    {
        var provider = new FakeProvider("fake", new ConversionCapability(Png, Jpeg)) { Delay = TimeSpan.FromMilliseconds(150) };
        using var queue = Queue(provider, parallelism: 2);
        string[] inputs = Enumerable.Range(0, 6).Select(i => _folder.File($"p{i}.png")).ToArray();

        ConversionResult[] results = await Task.WhenAll(inputs.Select(i => queue.ConvertAsync(i, Jpeg, cancellationToken: TestContext.Current.CancellationToken)));

        Assert.All(results, r => Assert.Equal(ConversionStatus.Succeeded, r.Status));
        Assert.Equal(2, provider.MaxConcurrent);
    }

    [Fact]
    public void Default_parallelism_is_at_least_one() => Assert.True(new JobQueue(new FormatCatalog([])).MaxParallelism >= 1);
}
