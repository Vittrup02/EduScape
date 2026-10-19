using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Eduscape.Tests.Infrastructure;

/// <summary>Small images built in memory, including a JPEG carrying EXIF metadata like a phone photo.</summary>
internal static class TestImages
{
    /// <summary>Text hidden in the EXIF block, standing in for a GPS position or camera serial number.</summary>
    public const string ExifSecret = "secret-gps-55.6761N-12.5683E";

    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    /// <summary>
    /// A JPEG with an EXIF APP1 segment right after the start marker: orientation <paramref name="orientation"/>
    /// (6 = stored sideways, shown rotated 90° clockwise) and <see cref="ExifSecret"/> as image description.
    /// </summary>
    public static byte[] JpegWithExif(int width, int height, ushort orientation)
    {
        var jpeg = Jpeg(width, height);
        var secret = Encoding.ASCII.GetBytes(ExifSecret + "\0");

        // TIFF header (little endian), one IFD with two entries: Orientation (SHORT) and ImageDescription (ASCII).
        const int ifdOffset = 8;
        const int entries = 2;
        var valueOffset = ifdOffset + 2 + (entries * 12) + 4;
        var tiff = new byte[valueOffset + secret.Length];
        "II*\0"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), ifdOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(ifdOffset), entries);
        WriteEntry(tiff.AsSpan(ifdOffset + 2), tag: 0x010E, type: 2, count: (uint)secret.Length, value: (uint)valueOffset);
        WriteEntry(tiff.AsSpan(ifdOffset + 14), tag: 0x0112, type: 3, count: 1, value: orientation);
        secret.CopyTo(tiff.AsSpan(valueOffset));

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment.AsSpan(4));

        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    public static bool Contains(byte[] content, string text) =>
        content.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    private static void WriteEntry(Span<byte> entry, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], value);
    }

    /// <summary>Left half red, right half blue, so tests can tell how the picture was turned.</summary>
    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Blue);
        using (var red = new SKPaint { Color = SKColors.Red })
        {
            surface.Canvas.DrawRect(0, 0, width / 2f, height, red);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
