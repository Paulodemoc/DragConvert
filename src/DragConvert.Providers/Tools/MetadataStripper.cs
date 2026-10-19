using System.Buffers.Binary;
using System.Text;

namespace DragConvert.Providers.Tools;

/// <summary>
/// Removes metadata from JPEG, PNG and WebP files without re-encoding: the image data is copied byte for byte and
/// only metadata blocks are dropped. A JPEG's EXIF orientation is kept (as a minimal EXIF block) so photos don't turn
/// sideways.
/// </summary>
public static class MetadataStripper
{
    private const ushort OrientationTag = 0x0112;

    /// <summary>
    /// Drops APP1 (EXIF, XMP), APP13 (IPTC), comments and other application segments. Keeps APP0 (JFIF), APP2 ICC
    /// profiles and APP14 (Adobe colour transform), which affect how the image looks.
    /// </summary>
    public static byte[] StripJpeg(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new FormatException("Not a JPEG file.");
        }

        using var output = new MemoryStream(jpeg.Length);
        output.Write([0xFF, 0xD8]);
        int orientation = 1;
        bool orientationWritten = false;
        int pos = 2;
        while (pos + 4 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                throw new FormatException("Corrupt JPEG segment.");
            }

            byte marker = jpeg[pos + 1];
            if (marker == 0xFF)
            {
                pos++; // fill byte
                continue;
            }

            if (marker == 0xDA)
            {
                // Start of scan: everything from here on is image data.
                WriteOrientation();
                output.Write(jpeg[pos..]);
                return output.ToArray();
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(pos + 2)..]);
            if (length < 2 || pos + 2 + length > jpeg.Length)
            {
                throw new FormatException("Corrupt JPEG segment length.");
            }

            ReadOnlySpan<byte> segment = jpeg.Slice(pos, length + 2);
            ReadOnlySpan<byte> payload = segment[4..];
            bool keep = marker switch
            {
                0xE0 => true,                                         // APP0 JFIF
                0xE1 => false,                                        // APP1 EXIF / XMP
                0xE2 => payload.StartsWith("ICC_PROFILE\0"u8),        // APP2: ICC yes, FlashPix etc. no
                0xEE => true,                                         // APP14 Adobe
                >= 0xE3 and <= 0xEF => false,                         // other APPn (incl. APP13 IPTC)
                0xFE => false,                                        // COM
                _ => true,                                            // tables, frame headers, ...
            };

            if (marker == 0xE1 && payload.StartsWith("Exif\0\0"u8))
            {
                orientation = ReadOrientation(payload[6..]);
            }

            if (keep)
            {
                // Put the orientation right after JFIF, before frame and table segments.
                if (marker != 0xE0)
                {
                    WriteOrientation();
                }

                output.Write(segment);
            }

            pos += length + 2;
        }

        throw new FormatException("JPEG has no image data.");

        void WriteOrientation()
        {
            if (orientationWritten)
            {
                return;
            }

            orientationWritten = true;
            if (orientation is > 1 and <= 8)
            {
                output.Write(MinimalExif(orientation));
            }
        }
    }

    /// <summary>Drops tEXt, zTXt, iTXt, eXIf and tIME chunks.</summary>
    public static byte[] StripPng(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!png.StartsWith(signature))
        {
            throw new FormatException("Not a PNG file.");
        }

        using var output = new MemoryStream(png.Length);
        output.Write(signature);
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[pos..]);
            if (length < 0 || pos + 12 + length > png.Length)
            {
                throw new FormatException("Corrupt PNG chunk.");
            }

            string type = Encoding.ASCII.GetString(png.Slice(pos + 4, 4));
            if (type is not ("tEXt" or "zTXt" or "iTXt" or "eXIf" or "tIME"))
            {
                output.Write(png.Slice(pos, length + 12));
            }

            pos += length + 12;
            if (type == "IEND")
            {
                break;
            }
        }

        return output.ToArray();
    }

    /// <summary>Drops the EXIF and XMP chunks and clears their flags in the VP8X header.</summary>
    public static byte[] StripWebP(ReadOnlySpan<byte> webp)
    {
        if (webp.Length < 12 || !webp.StartsWith("RIFF"u8) || !webp[8..12].SequenceEqual("WEBP"u8))
        {
            throw new FormatException("Not a WebP file.");
        }

        using var body = new MemoryStream(webp.Length);
        body.Write("WEBP"u8);
        int pos = 12;
        while (pos + 8 <= webp.Length)
        {
            string fourCc = Encoding.ASCII.GetString(webp.Slice(pos, 4));
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp[(pos + 4)..]);
            int padded = size + (size & 1);
            if (size < 0 || pos + 8 + size > webp.Length)
            {
                throw new FormatException("Corrupt WebP chunk.");
            }

            if (fourCc is not ("EXIF" or "XMP "))
            {
                byte[] chunk = webp.Slice(pos, 8 + Math.Min(padded, webp.Length - pos - 8)).ToArray();
                if (fourCc == "VP8X" && chunk.Length > 8)
                {
                    chunk[8] &= unchecked((byte)~0x0C); // EXIF (0x08) and XMP (0x04) present flags
                }

                body.Write(chunk);
            }

            pos += 8 + padded;
        }

        byte[] content = body.ToArray();
        byte[] result = new byte[8 + content.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)content.Length);
        content.CopyTo(result, 8);
        return result;
    }

    /// <summary>Reads the Orientation tag from a TIFF-structured EXIF block (after the "Exif\0\0" header); 1 if absent.</summary>
    internal static int ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return 1;
        }

        bool little = tiff[0] == 'I' && tiff[1] == 'I';
        uint ifd = U32(tiff, 4, little);
        if (ifd + 2 > tiff.Length)
        {
            return 1;
        }

        int count = U16(tiff, (int)ifd, little);
        for (int i = 0; i < count; i++)
        {
            int entry = (int)ifd + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
            {
                break;
            }

            if (U16(tiff, entry, little) == OrientationTag)
            {
                return U16(tiff, entry + 8, little);
            }
        }

        return 1;
    }

    private static ushort U16(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    /// <summary>An APP1 segment holding EXIF with only the Orientation tag (little-endian TIFF, one IFD entry).</summary>
    internal static byte[] MinimalExif(int orientation)
    {
        byte[] payload =
        [
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // TIFF header, IFD0 at offset 8
            0x01, 0x00,                                               // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,           // tag 0x0112, SHORT, count 1
            (byte)orientation, 0x00, 0x00, 0x00,                      // value
            0x00, 0x00, 0x00, 0x00,                                   // no next IFD
        ];
        byte[] segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }
}
