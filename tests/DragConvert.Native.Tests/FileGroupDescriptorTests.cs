using System.Buffers.Binary;
using System.Text;
using DragConvert.Native.DragDrop;

namespace DragConvert.Native.Tests;

public class FileGroupDescriptorTests
{
    private static byte[] Build(params string[] names)
    {
        var blob = new byte[4 + (names.Length * 592)];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, (uint)names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            Encoding.Unicode.GetBytes(names[i]).CopyTo(blob, 4 + (i * 592) + 72);
        }

        return blob;
    }

    [Fact]
    public void Reads_every_file_name() =>
        Assert.Equal(["photo.heic", "report.pdf"], FileGroupDescriptor.ReadFileNames(Build("photo.heic", "report.pdf")));

    [Fact]
    public void Truncated_blob_returns_what_fits()
    {
        byte[] blob = Build("a.txt", "b.txt");
        Assert.Equal(["a.txt"], FileGroupDescriptor.ReadFileNames(blob.AsSpan(0, blob.Length - 10)));
    }

    [Fact]
    public void Empty_blob_returns_nothing() => Assert.Empty(FileGroupDescriptor.ReadFileNames([]));
}
