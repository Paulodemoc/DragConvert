using DragConvert.Core.Conversion;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Core.Tests;

public sealed class TempJournalTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string JournalPath => Path.Combine(_folder.Path, "state", "temp-journal.txt");

    [Fact]
    public void Leftovers_from_a_crash_are_deleted_at_the_next_start()
    {
        // A run that "crashed": temp file and temp folder recorded, never finished.
        string tempFile = _folder.File(".photo.1a2b3c4d" + OutputPaths.TempMarker + ".jpg");
        string tempFolder = Path.Combine(_folder.Path, ".doc pages.5e6f7a8b" + OutputPaths.TempMarker);
        Directory.CreateDirectory(tempFolder);
        File.WriteAllText(Path.Combine(tempFolder, "page 1.pdf"), "x");
        var crashed = new TempJournal(JournalPath);
        crashed.Created(tempFile);
        crashed.Created(tempFolder);

        int removed = new TempJournal(JournalPath).CleanUp();

        Assert.Equal(2, removed);
        Assert.False(File.Exists(tempFile));
        Assert.False(Directory.Exists(tempFolder));
        Assert.Empty(File.ReadAllLines(JournalPath));
    }

    [Fact]
    public void Paths_without_the_temp_marker_are_never_deleted()
    {
        string userFile = _folder.File("holiday.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.WriteAllLines(JournalPath, [userFile]);

        Assert.Equal(0, new TempJournal(JournalPath).CleanUp());
        Assert.True(File.Exists(userFile));
    }

    [Fact]
    public void Finished_temps_leave_the_journal()
    {
        var journal = new TempJournal(JournalPath);
        string temp = Path.Combine(_folder.Path, ".a.12345678" + OutputPaths.TempMarker + ".png");
        journal.Created(temp);
        Assert.Contains(temp, File.ReadAllLines(JournalPath));

        journal.Finished(temp);
        Assert.Empty(File.ReadAllLines(JournalPath));
    }

    [Fact]
    public async Task Job_queue_records_temps_only_while_they_exist()
    {
        string input = _folder.File("photo.png");
        var journal = new TempJournal(JournalPath);
        string? seenDuringRun = null;
        var provider = new FakeProvider("fake", new ConversionCapability(Png, Jpeg)) { OnConvert = _ => seenDuringRun = File.ReadAllText(JournalPath) };
        using var queue = new JobQueue(new FormatCatalog([provider]), temps: journal);

        ConversionResult result = await queue.ConvertAsync(input, Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Contains(OutputPaths.TempMarker, seenDuringRun, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllLines(JournalPath));
    }
}
