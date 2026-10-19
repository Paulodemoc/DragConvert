using DragConvert.Core.Conversion;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Core.Tests;

public class ResultSummaryTests
{
    private static ConversionResult Ok(string name) => ConversionResult.Success($@"C:\in\{name}.heic", Jpeg, $@"C:\in\{name}.jpg");

    private static ConversionResult Fail(string name, string error) => ConversionResult.Failure($@"C:\in\{name}.heic", Jpeg, error);

    [Fact]
    public void Single_success_names_the_output()
    {
        NotificationText text = ResultSummary.Describe(Jpeg, [Ok("IMG_1")], []);
        Assert.Equal("Converted to JPEG", text.Title);
        Assert.Equal("IMG_1.jpg", text.Body);
    }

    [Fact]
    public void Several_successes_are_counted()
    {
        NotificationText text = ResultSummary.Describe(Jpeg, [Ok("a"), Ok("b"), Ok("c")], []);
        Assert.Equal("3 files converted to JPEG", text.Title);
        Assert.Equal(string.Empty, text.Body);
    }

    [Fact]
    public void Single_failure_gives_the_reason()
    {
        NotificationText text = ResultSummary.Describe(Jpeg, [Fail("IMG_1", "Corrupt image.")], []);
        Assert.Equal("Couldn't convert IMG_1.heic", text.Title);
        Assert.Equal("Corrupt image.", text.Body);
    }

    [Fact]
    public void Mixed_results_count_both_and_list_failures()
    {
        NotificationText text = ResultSummary.Describe(Jpeg, [Ok("a"), Ok("b"), Fail("c", "Corrupt image.")], []);
        Assert.Equal("2 converted to JPEG, 1 failed", text.Title);
        Assert.Equal("c.heic: Corrupt image.", text.Body);
    }

    [Fact]
    public void Skipped_files_are_named_with_overflow()
    {
        string[] skipped = [@"C:\v\1.mp4", @"C:\v\2.mp4", @"C:\v\3.mp4", @"C:\v\4.mp4", @"C:\v\5.mp4"];
        NotificationText text = ResultSummary.Describe(Jpeg, [Ok("a")], skipped);
        Assert.EndsWith("Skipped 5 files that can't become JPEG: 1.mp4, 2.mp4, 3.mp4 and 2 more", text.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_reports_progress_so_far()
    {
        var results = new[] { Ok("a"), ConversionResult.Cancel(@"C:\in\b.heic", Jpeg), ConversionResult.Cancel(@"C:\in\c.heic", Jpeg) };
        NotificationText text = ResultSummary.Describe(Jpeg, results, []);
        Assert.Equal("Conversion cancelled", text.Title);
        Assert.Equal("1 of 3 files converted to JPEG before cancelling.", text.Body);
    }

    [Fact]
    public void Nothing_to_convert_explains_why()
    {
        Assert.Equal("DragConvert can't convert notes.docx.", ResultSummary.NothingToConvert([@"C:\notes.docx"]).Body);
        Assert.Equal("None of the 2 files can become MP3.", ResultSummary.NothingToConvert([@"a.jpg", @"b.png"], Mp3).Body);
    }
}
