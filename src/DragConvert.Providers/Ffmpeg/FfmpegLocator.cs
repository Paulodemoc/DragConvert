namespace DragConvert.Providers.Ffmpeg;

/// <summary>
/// Finds ffmpeg.exe: the DRAGCONVERT_FFMPEG environment variable, next to the app, the repo's tools/bin
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
        foreach (string candidate in new[] { Path.Combine(baseDir, executableName), Path.Combine(baseDir, "engines", executableName) })
        {
            if (File.Exists(candidate))
            {
                return candidate;
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
}
