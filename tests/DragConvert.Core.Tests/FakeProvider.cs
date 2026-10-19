using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tests;

/// <summary>Test provider: writes a small file to the job's output path, or fails, waits or blocks on request.</summary>
internal sealed class FakeProvider(string id, params ConversionCapability[] capabilities) : IConversionProvider
{
    private int _running;

    public string Id { get; } = id;

    public bool IsAvailable { get; init; } = true;

    public IReadOnlyCollection<ConversionCapability> Capabilities { get; } = capabilities;

    public string? FailWith { get; init; }

    public bool WriteNothing { get; init; }

    public TimeSpan Delay { get; init; }

    public Action<ConversionJob>? OnConvert { get; init; }

    public List<ConversionJob> Jobs { get; } = [];

    public int MaxConcurrent { get; private set; }

    public async Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        lock (Jobs)
        {
            Jobs.Add(job);
            MaxConcurrent = Math.Max(MaxConcurrent, ++_running);
        }

        try
        {
            progress?.Report(0.5);
            OnConvert?.Invoke(job);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (!WriteNothing)
            {
                await File.WriteAllTextAsync(job.OutputPath, $"converted to {job.Target.Id}", cancellationToken);
            }

            if (FailWith is not null)
            {
                throw new ConversionException(FailWith);
            }
        }
        finally
        {
            lock (Jobs)
            {
                _running--;
            }
        }
    }
}
