using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Core.Tests;

public sealed class ToolRunnerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Per_file_tool_writes_a_suffixed_file_beside_each_input()
    {
        string a = _folder.File("a.pdf", "A");
        string b = _folder.File("b.pdf", "B");
        using var runner = new ToolRunner();

        IReadOnlyList<ToolResult> results = await runner.RunAsync(new FakeTool("compress", " (compressed)"), [a, b], cancellationToken: Ct);

        Assert.All(results, r => Assert.Equal(ConversionStatus.Succeeded, r.Status));
        Assert.Equal(["a (compressed).pdf", "a.pdf", "b (compressed).pdf", "b.pdf"], _folder.Files());
        Assert.Equal("tool output from a.pdf", await File.ReadAllTextAsync(Path.Combine(_folder.Path, "a (compressed).pdf"), Ct));
        Assert.Equal("A", await File.ReadAllTextAsync(a, Ct));
    }

    [Fact]
    public async Task Combining_tool_makes_one_output_named_after_the_first_input()
    {
        string a = _folder.File("first.pdf");
        string b = _folder.File("second.pdf");
        using var runner = new ToolRunner();

        IReadOnlyList<ToolResult> results = await runner.RunAsync(new FakeTool("merge", " (merged)") { Combines = true }, [a, b], cancellationToken: Ct);

        ToolResult result = Assert.Single(results);
        Assert.Equal(Path.Combine(_folder.Path, "first (merged).pdf"), result.OutputPath);
        Assert.Equal("tool output from first.pdf, second.pdf", await File.ReadAllTextAsync(result.OutputPath!, Ct));
    }

    [Fact]
    public async Task Folder_tool_produces_a_folder_and_never_overwrites()
    {
        string a = _folder.File("doc.pdf");
        Directory.CreateDirectory(Path.Combine(_folder.Path, "doc pages"));
        using var runner = new ToolRunner();

        ToolResult result = Assert.Single(await runner.RunAsync(new FakeTool("split", " pages") { Folder = true }, [a], cancellationToken: Ct));

        Assert.Equal(Path.Combine(_folder.Path, "doc pages (1)"), result.OutputPath);
        Assert.Equal(["page-1.txt", "page-2.txt"], Directory.GetFiles(result.OutputPath!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Failure_leaves_no_temp_files_or_folders()
    {
        string a = _folder.File("doc.pdf");
        using var runner = new ToolRunner();

        ToolResult result = Assert.Single(await runner.RunAsync(new FakeTool("split", " pages") { Folder = true, FailWith = "Encrypted PDF." }, [a], cancellationToken: Ct));

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal("Encrypted PDF.", result.Error);
        Assert.Equal(["doc.pdf"], _folder.Files());
        Assert.Empty(Directory.GetDirectories(_folder.Path));
    }

    [Fact]
    public async Task Combining_tool_with_one_file_fails_instead_of_copying_it()
    {
        string a = _folder.File("only.pdf");
        using var runner = new ToolRunner();

        ToolResult result = Assert.Single(await runner.RunAsync(new FakeTool("merge", " (merged)") { Combines = true }, [a], cancellationToken: Ct));

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal(["only.pdf"], _folder.Files());
    }

    [Fact]
    public async Task Unexpected_exceptions_become_failures_and_leave_no_files()
    {
        string a = _folder.File("doc.pdf");
        using var runner = new ToolRunner();

        ToolResult result = Assert.Single(await runner.RunAsync(new FakeTool("compress", " (compressed)") { Throw = new InvalidCastException("GetName: Object is not a name.") }, [a], cancellationToken: Ct));

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("not a name", result.Error, StringComparison.Ordinal);
        Assert.Equal(["doc.pdf"], _folder.Files());
    }

    [Fact]
    public void Catalog_needs_two_files_for_combining_tools_and_counts_the_rest()
    {
        var merge = new FakeTool("merge", " (merged)") { Combines = true };
        var compress = new FakeTool("compress", " (compressed)");
        var catalog = new ToolCatalog([merge, compress]);

        Assert.Equal([compress], catalog.ToolOptionsFor(["one.pdf"]).Select(o => o.Tool));

        IReadOnlyList<ToolOption> options = catalog.ToolOptionsFor(["a.pdf", "b.pdf", "c.jpg"]);
        Assert.Equal([new ToolOption(merge, 2, 3), new ToolOption(compress, 2, 3)], options);
    }

    [Fact]
    public void Plan_splits_files_by_what_the_tool_accepts()
    {
        var (accepted, skipped) = ToolCatalog.Plan(new FakeTool("compress", " (compressed)"), ["a.pdf", "b.png", "c.PDF"]);
        Assert.Equal(["a.pdf", "c.PDF"], accepted);
        Assert.Equal(["b.png"], skipped);
    }

    /// <summary>Accepts PDFs; writes "tool output from …" (or two page files for folder outputs).</summary>
    private sealed class FakeTool(string id, string suffix) : IFileTool
    {
        public string Id { get; } = id;

        public string DisplayName => Id;

        public string Glyph => "?";

        public bool IsAvailable => true;

        public bool CombinesInputs => Combines;

        public bool Combines { get; init; }

        public bool Folder { get; init; }

        public string? FailWith { get; init; }

        public Exception? Throw { get; init; }

        public ToolOutputKind OutputKind => Folder ? ToolOutputKind.Folder : ToolOutputKind.File;

        public string NameSuffix { get; } = suffix;

        public bool Accepts(MediaFormat input) => input == Pdf;

        public string OutputExtension(MediaFormat? input) => input?.Extension ?? string.Empty;

        public async Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (Folder)
            {
                await File.WriteAllTextAsync(Path.Combine(job.OutputPath, "page-1.txt"), "1", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(job.OutputPath, "page-2.txt"), "2", cancellationToken);
            }
            else
            {
                await File.WriteAllTextAsync(job.OutputPath, "tool output from " + string.Join(", ", job.Inputs.Select(Path.GetFileName)), cancellationToken);
            }

            if (FailWith is not null)
            {
                throw new ConversionException(FailWith);
            }

            if (Throw is not null)
            {
                throw Throw;
            }
        }
    }
}
