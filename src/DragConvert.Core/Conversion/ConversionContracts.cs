namespace DragConvert.Core.Conversion;

/// <summary>One input format a provider can turn into one target format.</summary>
public sealed record ConversionCapability(MediaFormat Input, MediaFormat Target);

/// <summary>
/// A single conversion handed to a provider. <see cref="OutputPath"/> is a temporary file in the destination folder;
/// the provider writes only there and never touches <see cref="InputPath"/>.
/// </summary>
public sealed record ConversionJob(
    string InputPath,
    string OutputPath,
    MediaFormat Target,
    IReadOnlyDictionary<string, string> Options);

public interface IConversionProvider
{
    string Id { get; }

    /// <summary>False when a required engine is missing (for example ffmpeg.exe); the catalog then ignores the provider.</summary>
    bool IsAvailable { get; }

    IReadOnlyCollection<ConversionCapability> Capabilities { get; }

    /// <summary>Converts the file. Throws <see cref="ConversionException"/> with a one-line reason on failure.</summary>
    Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>A conversion failure whose message is short enough to show to the user as-is.</summary>
public sealed class ConversionException : Exception
{
    public ConversionException()
    {
    }

    public ConversionException(string message)
        : base(message)
    {
    }

    public ConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public enum ConversionStatus
{
    Succeeded,
    Failed,
    Cancelled,
}

public sealed record ConversionResult(string InputPath, MediaFormat Target, ConversionStatus Status, string? OutputPath, string? Error)
{
    public static ConversionResult Success(string input, MediaFormat target, string output) => new(input, target, ConversionStatus.Succeeded, output, null);

    public static ConversionResult Failure(string input, MediaFormat target, string error) => new(input, target, ConversionStatus.Failed, null, error);

    public static ConversionResult Cancel(string input, MediaFormat target) => new(input, target, ConversionStatus.Cancelled, null, null);
}
