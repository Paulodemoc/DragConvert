namespace DragConvert.Core.Conversion;

public enum MediaCategory
{
    Image,
    Audio,
    Video,
    Document,
}

/// <summary>A file format the app can produce or recognise. The first extension is the one used for output files.</summary>
public sealed record MediaFormat(string Id, string DisplayName, MediaCategory Category, params string[] Extensions)
{
    public string Extension => Extensions[0];

    public override string ToString() => DisplayName;
}

/// <summary>Every format the app knows about, in the order tiles and pickers list them.</summary>
public static class MediaFormats
{
    public static readonly MediaFormat Jpeg = new("jpeg", "JPEG", MediaCategory.Image, ".jpg", ".jpeg", ".jpe", ".jfif");
    public static readonly MediaFormat Png = new("png", "PNG", MediaCategory.Image, ".png");
    public static readonly MediaFormat WebP = new("webp", "WebP", MediaCategory.Image, ".webp");
    public static readonly MediaFormat Heic = new("heic", "HEIC", MediaCategory.Image, ".heic", ".heif");
    public static readonly MediaFormat Tiff = new("tiff", "TIFF", MediaCategory.Image, ".tif", ".tiff");
    public static readonly MediaFormat Bmp = new("bmp", "BMP", MediaCategory.Image, ".bmp");
    public static readonly MediaFormat Gif = new("gif", "GIF", MediaCategory.Image, ".gif");
    public static readonly MediaFormat Pdf = new("pdf", "PDF", MediaCategory.Document, ".pdf");

    public static readonly MediaFormat Mp3 = new("mp3", "MP3", MediaCategory.Audio, ".mp3");
    public static readonly MediaFormat M4a = new("m4a", "M4A", MediaCategory.Audio, ".m4a", ".aac");
    public static readonly MediaFormat Wav = new("wav", "WAV", MediaCategory.Audio, ".wav");
    public static readonly MediaFormat Flac = new("flac", "FLAC", MediaCategory.Audio, ".flac");
    public static readonly MediaFormat Ogg = new("ogg", "OGG", MediaCategory.Audio, ".ogg", ".oga");
    public static readonly MediaFormat Opus = new("opus", "Opus", MediaCategory.Audio, ".opus");
    public static readonly MediaFormat Wma = new("wma", "WMA", MediaCategory.Audio, ".wma");

    public static readonly MediaFormat Mp4 = new("mp4", "MP4", MediaCategory.Video, ".mp4", ".m4v");
    public static readonly MediaFormat WebM = new("webm", "WebM", MediaCategory.Video, ".webm");
    public static readonly MediaFormat Mov = new("mov", "MOV", MediaCategory.Video, ".mov");
    public static readonly MediaFormat Mkv = new("mkv", "MKV", MediaCategory.Video, ".mkv");
    public static readonly MediaFormat Avi = new("avi", "AVI", MediaCategory.Video, ".avi");
    public static readonly MediaFormat Wmv = new("wmv", "WMV", MediaCategory.Video, ".wmv");

    public static IReadOnlyList<MediaFormat> All { get; } =
    [
        Jpeg, Png, WebP, Heic, Tiff, Bmp, Gif, Pdf,
        Mp3, M4a, Wav, Flac, Ogg, Opus, Wma,
        Mp4, WebM, Mov, Mkv, Avi, Wmv,
    ];

    private static readonly Dictionary<string, MediaFormat> ByExtension = All
        .SelectMany(f => f.Extensions.Select(e => (Extension: e, Format: f)))
        .ToDictionary(x => x.Extension, x => x.Format, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, MediaFormat> ById = All.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Format of a path or bare extension (".jpg" or "jpg"), or null if unknown.</summary>
    public static MediaFormat? FromPath(string pathOrExtension)
    {
        string extension = Path.GetExtension(pathOrExtension);
        if (extension.Length == 0)
        {
            extension = "." + pathOrExtension.TrimStart('.');
        }

        return ByExtension.GetValueOrDefault(extension);
    }

    /// <summary>Format by id or display name ("jpeg", "JPEG") or by extension ("jpg"), or null if unknown.</summary>
    public static MediaFormat? FromName(string name) => ById.GetValueOrDefault(name) ?? FromPath(name);
}
