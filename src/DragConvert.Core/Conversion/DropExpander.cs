namespace DragConvert.Core.Conversion;

/// <summary>The files a drop resolves to once folders are expanded.</summary>
public sealed record DropSet(IReadOnlyList<string> Files, int FolderCount, bool Truncated);

/// <summary>
/// Turns dropped paths into files: folders expand recursively, outputs later land beside each original.
/// Folder links (junctions, symlinks) are not followed, so a link back up the tree can't loop.
/// </summary>
public static class DropExpander
{
    public const int DefaultMaxFiles = 10_000;

    public static DropSet Expand(IEnumerable<string> droppedPaths, int maxFiles = DefaultMaxFiles, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int folders = 0;
        bool truncated = false;

        foreach (string path in droppedPaths)
        {
            if (File.Exists(path))
            {
                Add(path);
            }
            else if (Directory.Exists(path))
            {
                folders++;
                Walk(new DirectoryInfo(path));
            }

            if (truncated)
            {
                break;
            }
        }

        return new DropSet(files, folders, truncated);

        void Walk(DirectoryInfo directory)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(directory);
            while (pending.Count > 0 && !truncated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DirectoryInfo current = pending.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = current.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                var subfolders = new List<DirectoryInfo>();
                foreach (FileSystemInfo entry in entries)
                {
                    if (entry.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo sub)
                    {
                        if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            subfolders.Add(sub);
                        }
                    }
                    else if (!Add(entry.FullName))
                    {
                        return;
                    }
                }

                // Push in reverse so folders are visited in name order.
                for (int i = subfolders.Count - 1; i >= 0; i--)
                {
                    pending.Push(subfolders[i]);
                }
            }
        }

        bool Add(string file)
        {
            // Our own in-progress temp files never count as input.
            if (OutputPaths.IsTempFile(file) || !seen.Add(Path.GetFullPath(file)))
            {
                return true;
            }

            if (files.Count >= maxFiles)
            {
                truncated = true;
                return false;
            }

            files.Add(file);
            return true;
        }
    }
}
