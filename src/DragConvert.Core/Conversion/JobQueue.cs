namespace DragConvert.Core.Conversion;

/// <summary>
/// Runs conversions with bounded parallelism. For each job it picks the provider, plans the output name, has the
/// provider write a temp file and renames it on success, so a crash or failure never leaves a half-written file
/// under the final name.
/// </summary>
public sealed class JobQueue : IDisposable
{
    private readonly FormatCatalog _catalog;
    private readonly SemaphoreSlim _gate;

    public JobQueue(FormatCatalog catalog, int? maxParallelism = null)
    {
        _catalog = catalog;
        MaxParallelism = Math.Max(1, maxParallelism ?? (Environment.ProcessorCount / 2));
        _gate = new SemaphoreSlim(MaxParallelism, MaxParallelism);
    }

    public int MaxParallelism { get; }

    public async Task<ConversionResult> ConvertAsync(
        string inputPath,
        MediaFormat target,
        IReadOnlyDictionary<string, string>? options = null,
        IProgress<double>? progress = null,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ConversionResult.Cancel(inputPath, target);
        }

        string? tempPath = null;
        try
        {
            if (!File.Exists(inputPath))
            {
                return ConversionResult.Failure(inputPath, target, "The file no longer exists.");
            }

            IConversionProvider? provider = _catalog.FindProvider(inputPath, target);
            if (provider is null)
            {
                string from = MediaFormats.FromPath(inputPath)?.DisplayName ?? Path.GetExtension(inputPath);
                return ConversionResult.Failure(inputPath, target, $"Can't convert {from} to {target.DisplayName}.");
            }

            string finalPath = OutputPaths.PlanFinal(inputPath, target, outputDirectory);
            tempPath = OutputPaths.TempFor(finalPath);
            var job = new ConversionJob(Path.GetFullPath(inputPath), tempPath, target, options ?? new Dictionary<string, string>());

            await provider.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                throw new ConversionException($"The {provider.Id} converter produced no output.");
            }

            finalPath = MoveIntoPlace(tempPath, inputPath, target, outputDirectory, finalPath);
            tempPath = null;
            progress?.Report(1);
            return ConversionResult.Success(inputPath, target, finalPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ConversionResult.Cancel(inputPath, target);
        }
        catch (ConversionException ex)
        {
            return ConversionResult.Failure(inputPath, target, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConversionResult.Failure(inputPath, target, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bug or an unexpected file: report it like any failure instead of losing the result.
            return ConversionResult.Failure(inputPath, target, $"Unexpected error: {ex.Message}");
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
            }

            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Renames without overwriting; if another file took the planned name meanwhile, picks the next free one.</summary>
    private static string MoveIntoPlace(string tempPath, string inputPath, MediaFormat target, string? outputDirectory, string finalPath)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: false);
                return finalPath;
            }
            catch (IOException) when (attempt < 20 && File.Exists(finalPath))
            {
                finalPath = OutputPaths.PlanFinal(inputPath, target, outputDirectory);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for startup cleanup, which removes files carrying OutputPaths.TempMarker.
        }
    }
}
