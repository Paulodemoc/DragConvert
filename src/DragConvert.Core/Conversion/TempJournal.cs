namespace DragConvert.Core.Conversion;

/// <summary>Told about every temp file or folder while it exists, so a crash can't leave it behind for good.</summary>
public interface ITempTracker
{
    void Created(string path);

    void Finished(string path);
}

/// <summary>
/// Records in-flight temp outputs in a small file. Temp files sit beside the user's files, anywhere on disk, so they
/// can't be found by scanning; after a crash, <see cref="CleanUp"/> at the next start deletes the ones listed here.
/// </summary>
public sealed class TempJournal(string journalPath) : ITempTracker
{
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public string JournalPath { get; } = journalPath;

    public void Created(string path)
    {
        lock (_gate)
        {
            if (_active.Add(path))
            {
                Persist();
            }
        }
    }

    public void Finished(string path)
    {
        lock (_gate)
        {
            if (_active.Remove(path))
            {
                Persist();
            }
        }
    }

    /// <summary>
    /// Deletes leftovers from a previous run (only paths carrying <see cref="OutputPaths.TempMarker"/>, so a damaged
    /// journal can never delete a user's file) and empties the journal. Returns how many were removed.
    /// </summary>
    public int CleanUp()
    {
        lock (_gate)
        {
            int removed = 0;
            if (File.Exists(JournalPath))
            {
                foreach (string path in File.ReadAllLines(JournalPath).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!OutputPaths.IsTempFile(path))
                    {
                        continue;
                    }

                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, recursive: true);
                            removed++;
                        }
                        else if (File.Exists(path))
                        {
                            File.Delete(path);
                            removed++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Still locked or gone read-only: leave it rather than fail startup.
                    }
                }
            }

            _active.Clear();
            Persist();
            return removed;
        }
    }

    private void Persist()
    {
        try
        {
            string? directory = Path.GetDirectoryName(JournalPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = JournalPath + ".tmp";
            File.WriteAllLines(temp, _active);
            File.Move(temp, JournalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bookkeeping only; a failed write must never fail a conversion.
        }
    }
}
