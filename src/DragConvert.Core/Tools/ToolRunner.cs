using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tools;

/// <summary>
/// Runs tools with bounded parallelism and the same file safety as conversions: output goes to a temp file or folder
/// beside the first input and is renamed into place on success, never overwriting anything.
/// </summary>
public sealed class ToolRunner : IDisposable
{
    private readonly SemaphoreSlim _gate;
    private readonly ITempTracker? _temps;

    public ToolRunner(int? maxParallelism = null, ITempTracker? temps = null)
    {
        _temps = temps;
        int parallelism = Math.Max(1, maxParallelism ?? (Environment.ProcessorCount / 2));
        _gate = new SemaphoreSlim(parallelism, parallelism);
    }

    /// <summary>Runs the tool on the files it accepts: one run for combining tools, otherwise one run per file.</summary>
    public async Task<IReadOnlyList<ToolResult>> RunAsync(
        IFileTool tool,
        IReadOnlyList<string> files,
        IProgress<double>? progress = null,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (files.Count < tool.MinimumInputs)
        {
            return [ToolResult.Failure(tool, files, $"{tool.DisplayName} needs at least {tool.MinimumInputs} files.")];
        }

        IReadOnlyList<IReadOnlyList<string>> runs = tool.CombinesInputs ? [files] : files.Select(f => (IReadOnlyList<string>)[f]).ToList();
        var fractions = new double[runs.Count];
        Task<ToolResult>[] tasks = runs
            .Select((inputs, i) => RunOneAsync(
                tool,
                inputs,
                progress is null ? null : new Fraction(p =>
                {
                    fractions[i] = p;
                    progress.Report(fractions.Average());
                }),
                outputDirectory,
                cancellationToken))
            .ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void Dispose() => _gate.Dispose();

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for startup cleanup, which removes paths carrying OutputPaths.TempMarker.
        }
    }

    private async Task<ToolResult> RunOneAsync(IFileTool tool, IReadOnlyList<string> inputs, IProgress<double>? progress, string? outputDirectory, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Cancel(tool, inputs);
        }

        string? tempPath = null;
        try
        {
            if (inputs.FirstOrDefault(i => !File.Exists(i)) is { } missing)
            {
                return ToolResult.Failure(tool, inputs, $"{Path.GetFileName(missing)} no longer exists.");
            }

            string first = inputs[0];
            MediaFormat? format = MediaFormats.FromPath(first);
            if (format is null && !tool.AcceptsAnyFile)
            {
                throw new ConversionException($"{tool.DisplayName} can't open {Path.GetFileName(first)}.");
            }

            bool folder = tool.OutputKind == ToolOutputKind.Folder;
            string extension = folder ? string.Empty : tool.OutputExtension(format);
            string finalPath = OutputPaths.PlanFinal(first, tool.NameSuffix, extension, outputDirectory);
            tempPath = OutputPaths.TempFor(finalPath);
            _temps?.Created(tempPath);
            if (folder)
            {
                Directory.CreateDirectory(tempPath);
            }

            var job = new ToolJob(inputs.Select(Path.GetFullPath).ToList(), tempPath, new Dictionary<string, string>());
            await tool.RunAsync(job, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            bool produced = folder ? Directory.EnumerateFileSystemEntries(tempPath).Any() : File.Exists(tempPath) && new FileInfo(tempPath).Length > 0;
            if (!produced)
            {
                throw new ConversionException($"{tool.DisplayName} produced no output.");
            }

            finalPath = MoveIntoPlace(tempPath, first, tool.NameSuffix, extension, outputDirectory, finalPath, folder);
            _temps?.Finished(tempPath);
            tempPath = null;
            progress?.Report(1);
            return ToolResult.Success(tool, inputs, finalPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Cancel(tool, inputs);
        }
        catch (ConversionException ex)
        {
            return ToolResult.Failure(tool, inputs, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult.Failure(tool, inputs, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bug or an unexpected file: report it like any failure instead of losing the result.
            return ToolResult.Failure(tool, inputs, $"Unexpected error: {ex.Message}");
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
                _temps?.Finished(tempPath);
            }

            _gate.Release();
        }
    }

    private static string MoveIntoPlace(string tempPath, string first, string suffix, string extension, string? outputDirectory, string finalPath, bool folder)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (folder)
                {
                    Directory.Move(tempPath, finalPath);
                }
                else
                {
                    File.Move(tempPath, finalPath, overwrite: false);
                }

                return finalPath;
            }
            catch (IOException) when (attempt < 20 && (File.Exists(finalPath) || Directory.Exists(finalPath)))
            {
                finalPath = OutputPaths.PlanFinal(first, suffix, extension, outputDirectory);
            }
        }
    }

    private sealed class Fraction(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(Math.Clamp(value, 0, 1));
    }
}
