using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using DragConvert.Providers.Documents;
using DragConvert.Providers.Ffmpeg;
using DragConvert.Providers.Image;
using DragConvert.Providers.Processes;
using DragConvert.Providers.Tools;
using DragConvert.Providers.Windows;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Tests;

/// <summary>M4b: the formats and tools added for format breadth (roadmap tiers A and B).</summary>
public sealed class FormatBreadthTests(MediaFixtures fixtures) : IClassFixture<MediaFixtures>
{
    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Work(string name) => fixtures.OutputPath(name);

    private string WriteText(string name, string content)
    {
        string path = Work(name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static async Task<string> Convert(IConversionProvider provider, string input, MediaFormat target, string output)
    {
        await provider.ConvertAsync(new ConversionJob(input, output, target, NoOptions), null, Ct);
        Assert.True(File.Exists(output), $"No output for {target}");
        return output;
    }

    private static async Task<string> Run(IFileTool tool, string output, params string[] inputs)
    {
        if (tool.OutputKind == ToolOutputKind.Folder)
        {
            Directory.CreateDirectory(output);
        }

        await tool.RunAsync(new ToolJob(inputs, output, NoOptions), null, Ct);
        return output;
    }

    private void RequireFfmpeg() => Assert.SkipWhen(fixtures.Ffmpeg is null || fixtures.Ffprobe is null, "ffmpeg not found; run tools/fetch-ffmpeg.ps1");

    private async Task<string> ProbeAsync(string path, string entries)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync(fixtures.Ffprobe!, ["-v", "error", "-show_entries", entries, "-of", "csv=p=0", path], TimeSpan.FromMinutes(1), onStandardOutputLine: l =>
        {
            if (!string.IsNullOrWhiteSpace(l))
            {
                lock (lines)
                {
                    lines.Add(l.Trim().TrimEnd(','));
                }
            }
        }, cancellationToken: Ct);
        return string.Join("|", lines);
    }

    // ---------- Images

    [Fact]
    public async Task Png_converts_to_avif()
    {
        string output = await Convert(new ImageProvider(), fixtures.TransparentPng, Avif, Work("out.avif"));
        var info = new MagickImageInfo(output);
        Assert.Equal(MagickFormat.Avif, info.Format);
        Assert.Equal(64u, info.Width);
    }

    [Fact]
    public async Task Svg_is_rasterised_at_twice_its_size()
    {
        string svg = WriteText("logo.svg", """<svg xmlns="http://www.w3.org/2000/svg" width="100" height="50"><rect width="100" height="50" fill="#3366ff"/><circle cx="25" cy="25" r="20" fill="#ffffff"/></svg>""");
        string output = await Convert(new ImageProvider(), svg, Png, Work("logo.png"));
        var info = new MagickImageInfo(output);
        Assert.Equal(200u, info.Width);
        Assert.Equal(100u, info.Height);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    public async Task Image_compress_makes_photos_smaller(string extension)
    {
        string input = Work($"big.{extension}");
        using (var photo = new MagickImage("plasma:", 3000, 2000))
        {
            photo.Quality = 98;
            photo.Write(input);
        }

        string output = await Run(new ImageCompressTool(), Work($"small.{extension}"), input);
        Assert.True(new FileInfo(output).Length < new FileInfo(input).Length);
        Assert.Equal(2560u, new MagickImageInfo(output).Width);
    }

    // ---------- Audio and video formats

    [Theory]
    [InlineData("aiff", "pcm_s16be")]
    [InlineData("wma", "wmav2")]
    public async Task Wav_converts_to_new_audio_formats(string targetId, string codec)
    {
        RequireFfmpeg();
        string output = await Convert(new FfmpegProvider(fixtures.Ffmpeg), fixtures.Wav, FromName(targetId)!, Work("out." + targetId));
        Assert.Equal(codec, await ProbeAsync(output, "stream=codec_name"));
    }

    [Theory]
    [InlineData("mov", "h264|aac")]
    [InlineData("mkv", "h264|aac")]
    [InlineData("avi", "mpeg4|mp3")]
    [InlineData("wmv", "wmv2|wmav2")]
    public async Task Mp4_converts_to_new_video_containers(string targetId, string codecs)
    {
        RequireFfmpeg();
        string output = await Convert(new FfmpegProvider(fixtures.Ffmpeg), fixtures.Mp4, FromName(targetId)!, Work("out." + targetId));
        Assert.Equal(codecs, await ProbeAsync(output, "stream=codec_name"));
    }

    // ---------- Audio and video tools

    [Fact]
    public async Task Audio_tools_compress_normalize_mono_and_draw_a_waveform()
    {
        RequireFfmpeg();
        string loud = Work("loud.mp3");
        await ProcessRunner.RunAsync(fixtures.Ffmpeg!, ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=330:duration=8", "-ac", "2", "-c:a", "libmp3lame", "-b:a", "320k", loud], TimeSpan.FromMinutes(1), cancellationToken: Ct);

        string compressed = await Run(new CompressAudioTool(fixtures.Ffmpeg), Work("loud-small.mp3"), loud);
        Assert.True(new FileInfo(compressed).Length < new FileInfo(loud).Length);

        string normalized = await Run(new NormalizeAudioTool(fixtures.Ffmpeg), Work("loud-norm.mp3"), loud);
        Assert.Equal("mp3", await ProbeAsync(normalized, "stream=codec_name"));

        string mono = await Run(new MonoAudioTool(fixtures.Ffmpeg), Work("loud-mono.mp3"), loud);
        Assert.Equal("1", await ProbeAsync(mono, "stream=channels"));

        string waveform = await Run(new WaveformTool(fixtures.Ffmpeg), Work("loud-wave.png"), loud);
        var info = new MagickImageInfo(waveform);
        Assert.Equal(1600u, info.Width);
        Assert.Equal(400u, info.Height);
    }

    [Fact]
    public async Task Video_tools_compress_mute_and_snapshot()
    {
        RequireFfmpeg();
        string big = Work("big.mp4");
        await ProcessRunner.RunAsync(
            fixtures.Ffmpeg!,
            ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=2560x1440:rate=30:duration=4", "-f", "lavfi", "-i", "sine=duration=4",
             "-pix_fmt", "nv12", "-c:v", "h264_mf", "-rate_control", "quality", "-quality", "100", "-c:a", "aac", "-shortest", big],
            TimeSpan.FromMinutes(2),
            cancellationToken: Ct);

        string compressed = await Run(new CompressVideoTool(fixtures.Ffmpeg), Work("big-small.mp4"), big);
        Assert.True(new FileInfo(compressed).Length < new FileInfo(big).Length);
        Assert.Equal("1920,1080", await ProbeAsync(compressed, "stream=width,height").ContinueWith(t => t.Result.Split('|')[0], Ct));

        string muted = await Run(new MuteVideoTool(fixtures.Ffmpeg), Work("big-muted.mp4"), big);
        Assert.Equal("h264", await ProbeAsync(muted, "stream=codec_name"));

        string snapshot = await Run(new SnapshotTool(fixtures.Ffmpeg), Work("big-shot.jpg"), big);
        Assert.Equal(MagickFormat.Jpeg, new MagickImageInfo(snapshot).Format);
    }

    // ---------- Subtitles

    private const string Srt = "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello <i>there</i>\r\n\r\n2\r\n00:01:02,250 --> 00:01:04,000\r\nSecond line\r\nwraps here\r\n";

    [Fact]
    public async Task Srt_converts_to_vtt_and_back_keeping_timings()
    {
        var provider = new SubtitleProvider();
        string vtt = await Convert(provider, WriteText("film.srt", Srt), Vtt, Work("film.vtt"));
        string vttText = await File.ReadAllTextAsync(vtt, Ct);
        Assert.StartsWith("WEBVTT", vttText, StringComparison.Ordinal);
        Assert.Contains("00:01:02.250 --> 00:01:04.000", vttText, StringComparison.Ordinal);

        string srt = await Convert(provider, vtt, MediaFormats.Srt, Work("again.srt"));
        IReadOnlyList<SubtitleCue> cues = SubtitleProvider.Parse(await File.ReadAllTextAsync(srt, Ct));
        Assert.Equal(2, cues.Count);
        Assert.Equal(TimeSpan.FromSeconds(62.25), cues[1].Start);
        Assert.Equal("Second line\nwraps here", cues[1].Text);
    }

    [Fact]
    public async Task Subtitles_to_text_keep_only_the_words()
    {
        string txt = await Convert(new SubtitleProvider(), WriteText("film2.srt", Srt), Txt, Work("film.txt"));
        Assert.Equal("Hello there\nSecond line wraps here\n", await File.ReadAllTextAsync(txt, Ct));
    }

    // ---------- Spreadsheets

    [Fact]
    public async Task Csv_tsv_and_xlsx_round_trip_tricky_fields()
    {
        const string csv = "name,price,notes\r\n\"Widget, large\",12.5,\"says \"\"hi\"\"\"\r\nGadget,7,\"two\nlines\"\r\n";
        var provider = new SpreadsheetProvider();
        string input = WriteText("data.csv", csv);

        string xlsx = await Convert(provider, input, Xlsx, Work("data.xlsx"));
        string back = await Convert(provider, xlsx, MediaFormats.Csv, Work("back.csv"));
        IReadOnlyList<IReadOnlyList<string>> rows = SpreadsheetProvider.ParseDelimited(File.ReadAllText(back), ',');
        Assert.Equal(["Widget, large", "12.5", "says \"hi\""], rows[1]);
        Assert.Equal("two\nlines", rows[2][2]);

        string tsv = await Convert(provider, input, Tsv, Work("data.tsv"));
        Assert.Contains("Widget, large\t12.5\tsays \"hi\"", await File.ReadAllTextAsync(tsv, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Semicolon_csv_is_detected()
    {
        string tsv = await Convert(new SpreadsheetProvider(), WriteText("eu.csv", "a;b\r\n1,5;2\r\n"), Tsv, Work("eu.tsv"));
        Assert.Equal("a\tb\r\n1,5\t2\r\n", await File.ReadAllTextAsync(tsv, Ct));
    }

    // ---------- Documents

    private string MakeDocx()
    {
        string path = Work("report.docx");
        using WordprocessingDocument doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        MainDocumentPart main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        StyleDefinitionsPart styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(new Style(new StyleName { Val = "heading 1" }) { StyleId = "Heading1", Type = StyleValues.Paragraph });
        string linkId = main.AddHyperlinkRelationship(new Uri("https://example.com/"), true).Id;
        Body body = main.Document.Body!;
        body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text("Quarterly report"))));
        body.Append(new Paragraph(new Run(new Text("Sales were ") { Space = SpaceProcessingModeValues.Preserve }), new Run(new RunProperties(new Bold()), new Text("up")), new Run(new Text(". See ") { Space = SpaceProcessingModeValues.Preserve }), new Hyperlink(new Run(new Text("the site"))) { Id = linkId }));
        body.Append(new Table(
            new TableRow(new TableCell(new Paragraph(new Run(new Text("Region")))), new TableCell(new Paragraph(new Run(new Text("Total"))))),
            new TableRow(new TableCell(new Paragraph(new Run(new Text("North")))), new TableCell(new Paragraph(new Run(new Text("42")))))));
        main.Document.Save();
        return path;
    }

    [Fact]
    public async Task Docx_converts_to_markdown_html_and_text()
    {
        var provider = new DocumentTextProvider();
        string docx = MakeDocx();

        string md = await File.ReadAllTextAsync(await Convert(provider, docx, MediaFormats.Markdown, Work("report.md")), Ct);
        Assert.Contains("# Quarterly report", md, StringComparison.Ordinal);
        Assert.Contains("Sales were **up**. See [the site](https://example.com/)", md, StringComparison.Ordinal);
        Assert.Contains("| Region | Total |", md, StringComparison.Ordinal);
        Assert.Contains("| North | 42 |", md, StringComparison.Ordinal);

        string html = await File.ReadAllTextAsync(await Convert(provider, docx, Html, Work("report.html")), Ct);
        Assert.Contains("<h1>Quarterly report</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>up</strong>", html, StringComparison.Ordinal);

        string txt = await File.ReadAllTextAsync(await Convert(provider, docx, Txt, Work("report.txt")), Ct);
        Assert.StartsWith("Quarterly report", txt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_text_is_extracted_per_page()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        string pdf = Work("text.pdf");
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            foreach (string line in new[] { "First page words", "Second page words" })
            {
                using XGraphics gfx = XGraphics.FromPdfPage(document.AddPage());
                gfx.DrawString(line, new XFont("Arial", 18), XBrushes.Black, 72, 100);
            }

            document.Save(pdf);
        }

        var provider = new DocumentTextProvider();
        string txt = await File.ReadAllTextAsync(await Convert(provider, pdf, Txt, Work("text.txt")), Ct);
        Assert.Contains("First page words", txt, StringComparison.Ordinal);
        Assert.Contains("Second page words", txt, StringComparison.Ordinal);

        string md = await File.ReadAllTextAsync(await Convert(provider, pdf, MediaFormats.Markdown, Work("text.md")), Ct);
        Assert.Contains("## Page 2", md, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Markdown_converts_to_html_and_plain_text()
    {
        string md = WriteText("notes.md", "# Title\n\nSome *emphasis* and a [link](https://example.com).\n\n- one\n- two\n");
        var provider = new DocumentTextProvider();

        string html = await File.ReadAllTextAsync(await Convert(provider, md, Html, Work("notes.html")), Ct);
        Assert.Contains("<h1 id=\"title\">Title</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<em>emphasis</em>", html, StringComparison.Ordinal);

        string txt = await File.ReadAllTextAsync(await Convert(provider, md, Txt, Work("notes.txt")), Ct);
        Assert.DoesNotContain("#", txt, StringComparison.Ordinal);
        Assert.Contains("Some emphasis and a link.", txt, StringComparison.Ordinal);
    }

    // ---------- Archives

    private string MakeTree()
    {
        string root = Work("tree");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(root, "sub", "b.dat"), "beta");
        return root;
    }

    [Fact]
    public async Task Zip_tool_keeps_relative_folders_and_extract_restores_them()
    {
        string root = MakeTree();
        string zip = await Run(new CreateZipTool(), Work("tree.zip"), Path.Combine(root, "a.txt"), Path.Combine(root, "sub", "b.dat"));
        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal(["a.txt", "sub/b.dat"], archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        }

        string folder = await Run(new ExtractArchiveTool(), Work("tree-out"), zip);
        Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(folder, "sub", "b.dat"), Ct));
    }

    [Fact]
    public async Task Tar_targz_and_gz_extract()
    {
        string root = MakeTree();
        string tar = Work("tree.tar");
        await TarFile.CreateFromDirectoryAsync(root, tar, includeBaseDirectory: false, Ct);
        string tarFolder = await Run(new ExtractArchiveTool(), Work("tar-out"), tar);
        Assert.True(File.Exists(Path.Combine(tarFolder, "sub", "b.dat")));

        string tgz = Work("tree.tar.gz");
        await using (FileStream file = File.Create(tgz))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await TarFile.CreateFromDirectoryAsync(root, gzip, includeBaseDirectory: false, Ct);
        }

        string tgzFolder = await Run(new ExtractArchiveTool(), Work("tgz-out"), tgz);
        Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(tgzFolder, "a.txt"), Ct));

        string gz = Work("note.txt.gz");
        await using (FileStream file = File.Create(gz))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await gzip.WriteAsync("hello"u8.ToArray(), Ct);
        }

        string gzFolder = await Run(new ExtractArchiveTool(), Work("gz-out"), gz);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(gzFolder, Path.GetFileNameWithoutExtension(gz)), Ct));
    }

    [Fact]
    public async Task Malicious_zip_cannot_write_outside_its_folder()
    {
        string zip = Work("evil.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(archive.CreateEntry("../escaped.txt").Open());
            writer.Write("gotcha");
        }

        string folder = Work("evil-out");
        await Assert.ThrowsAnyAsync<IOException>(() => Run(new ExtractArchiveTool(), folder, zip));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "escaped.txt")));
    }

    // ---------- Catalogs

    [Fact]
    public void Catalogs_offer_the_new_targets_and_tools()
    {
        var catalog = new FormatCatalog(AllProviders.Converters(fixtures.Ffmpeg));
        Assert.Contains(Txt, catalog.TargetsFor("report.pdf"));
        Assert.Contains(MediaFormats.Markdown, catalog.TargetsFor("report.docx"));
        Assert.Contains(Vtt, catalog.TargetsFor("film.srt"));
        Assert.Contains(Xlsx, catalog.TargetsFor("data.csv"));
        Assert.Contains(Avif, catalog.TargetsFor("photo.heic"));

        var tools = new ToolCatalog(AllProviders.Tools(fixtures.Ffmpeg));
        Assert.Contains(tools.ToolOptionsFor(["archive.rar"]), o => o.Tool.Id == "extract");
        Assert.Contains(tools.ToolOptionsFor(["mystery.xyz"]), o => o.Tool.Id == "create-zip");
        Assert.DoesNotContain(tools.ToolOptionsFor(["song.wav"]), o => o.Tool.Id == "compress-audio");
    }
}
