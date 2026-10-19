using System.Buffers.Binary;
using System.Text;

namespace DragConvert.Native.DragDrop;

/// <summary>
/// Reads file names from a FILEGROUPDESCRIPTORW clipboard blob, the format used for virtual files
/// such as mail attachments that have no path on disk until materialized.
/// </summary>
public static class FileGroupDescriptor
{
    public const string FormatName = "FileGroupDescriptorW";

    // FILEDESCRIPTORW: dwFlags(4) clsid(16) sizel(8) pointl(8) dwFileAttributes(4) ftCreationTime(8)
    // ftLastAccessTime(8) ftLastWriteTime(8) nFileSizeHigh(4) nFileSizeLow(4) cFileName(WCHAR[260]).
    private const int DescriptorSize = 592;
    private const int FileNameOffset = 72;
    private const int FileNameBytes = 520;

    public static IReadOnlyList<string> ReadFileNames(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 4)
        {
            return [];
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        var names = new List<string>((int)Math.Min(count, 1024));
        for (int i = 0; i < count; i++)
        {
            int start = 4 + (i * DescriptorSize);
            if (start + DescriptorSize > blob.Length)
            {
                break;
            }

            string name = Encoding.Unicode.GetString(blob.Slice(start + FileNameOffset, FileNameBytes));
            int nul = name.IndexOf('\0', StringComparison.Ordinal);
            names.Add(nul >= 0 ? name[..nul] : name);
        }

        return names;
    }
}
