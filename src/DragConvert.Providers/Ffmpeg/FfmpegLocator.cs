namespace DragConvert.Providers.Ffmpeg;

/// <summary>
/// Finds ffmpeg.exe: the DRAGCONVERT_FFMPEG environment variable, next to the app (or the exe a link points to), the repo's tools/bin
/// (development), then PATH.
/// </summary>
public static class FfmpegLocator
{
    public const string EnvironmentVariable = "DRAGCONVERT_FFMPEG";

    public static string? Find(string executableName = "ffmpeg.exe")
    {
        if (executableName == "ffmpeg.exe" && Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } fromEnv && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        string baseDir = AppContext.BaseDirectory;
        foreach (string dir in AppDirectories(baseDir))
        {
            foreach (string candidate in new[] { Path.Combine(dir, executableName), Path.Combine(dir, "engines", executableName) })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // Development: walk up from the build output to the repo root's tools/bin.
        for (DirectoryInfo? dir = new(baseDir); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tools", "bin", executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        return path?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Path.Combine(d, executableName))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The real app folder first: started through a symbolic link (winget's command alias in WinGet\Links), the base
    /// directory is the link's folder, which may hold other packages' ffmpeg.exe.
    /// </summary>
    private static IEnumerable<string> AppDirectories(string baseDir)
    {
        if (Environment.ProcessPath is { } process)
        {
            FileSystemInfo? target = null;
            try
            {
                target = File.ResolveLinkTarget(process, returnFinalTarget: true);
            }
            catch (IOException)
            {
            }

            if (target is not null && Path.GetDirectoryName(target.FullName) is { } real)
            {
                yield return real;
            }
        }

        yield return baseDir;
    }
}
