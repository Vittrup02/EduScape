using System.Buffers.Binary;
using System.IO.Compression;
using Eduscape.Infrastructure.Images;
using SkiaSharp;

namespace Eduscape.Tests.Infrastructure;

public class ImageProcessorTests
{
    [Fact]
    public async Task Jpeg_IsTurnedUpright_AndLosesAllMetadata()
    {
        var photo = TestImages.JpegWithExif(400, 200, orientation: 6);
        Assert.True(TestImages.Contains(photo, TestImages.ExifSecret));

        var processed = await ImageProcessor.ProcessAsync(photo, "image/jpeg", CancellationToken.None);

        Assert.Null(processed.Problem);
        var output = processed.Content!;
        Assert.False(TestImages.Contains(output, "Exif"));
        Assert.False(TestImages.Contains(output, TestImages.ExifSecret));
        Assert.True(ImageProcessor.HasSignatureOf(output, ProcessedImage.ContentType));

        // Stored 400×200 with the red half on the left; turned 90° clockwise it is 200×400 with red on top.
        using var bitmap = SKBitmap.Decode(output);
        Assert.Equal((200, 400), (bitmap.Width, bitmap.Height));
        Assert.Equal((200, 400), (processed.Width, processed.Height));
        AssertMostly(bitmap.GetPixel(100, 50), SKColors.Red);
        AssertMostly(bitmap.GetPixel(100, 350), SKColors.Blue);
    }

    [Fact]
    public async Task LargeImage_IsScaledDownToTheMaximumDimension()
    {
        var processed = await ImageProcessor.ProcessAsync(TestImages.Png(3000, 1000), "image/png", CancellationToken.None);

        Assert.Equal((ImageProcessor.MaxDimension, 800), (processed.Width, processed.Height));
    }

    [Fact]
    public async Task SmallImage_KeepsItsSize()
    {
        var processed = await ImageProcessor.ProcessAsync(TestImages.Jpeg(320, 180), "image/jpeg", CancellationToken.None);

        Assert.Equal((320, 180), (processed.Width, processed.Height));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task ContentThatDoesNotMatchTheDeclaredType_IsRejected(string declared)
    {
        var processed = await ImageProcessor.ProcessAsync(TestImages.Png(10, 10), declared, CancellationToken.None);

        Assert.Equal(ImageProblem.TypeNotAllowed, processed.Problem);
        Assert.Null(processed.Content);
    }

    [Fact]
    public async Task OtherTypes_AreRejected()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

        Assert.False(ImageProcessor.IsAllowedContentType("image/svg+xml"));
        Assert.Equal(
            ImageProblem.TypeNotAllowed,
            (await ImageProcessor.ProcessAsync(svg, "image/svg+xml", CancellationToken.None)).Problem);
    }

    [Fact]
    public async Task BrokenFileWithAJpegSignature_IsInvalid()
    {
        byte[] broken = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[64]];

        var processed = await ImageProcessor.ProcessAsync(broken, "image/jpeg", CancellationToken.None);

        Assert.Equal(ImageProblem.Invalid, processed.Problem);
    }

    [Fact]
    public async Task ImageWithTooManyPixels_IsRejectedBeforeDecoding()
    {
        // A tiny file whose header claims 10000×5000 pixels (a decompression bomb).
        var bomb = PngHeaderOnly(10_000, 5_000);

        var processed = await ImageProcessor.ProcessAsync(bomb, "image/png", CancellationToken.None);

        Assert.Equal(ImageProblem.TooLarge, processed.Problem);
    }

    private static void AssertMostly(SKColor actual, SKColor expected)
    {
        Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, 40);
        Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, 40);
        Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, 40);
    }

    private static byte[] PngHeaderOnly(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(new byte[1 + (width * 4)]); // one row of image data
        }

        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", header), .. Chunk("IDAT", compressed.ToArray()), .. Chunk("IEND", [])];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32([.. typeBytes, .. data]));
        return chunk;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
