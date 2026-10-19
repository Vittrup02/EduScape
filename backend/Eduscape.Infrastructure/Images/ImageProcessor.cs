using SkiaSharp;

namespace Eduscape.Infrastructure.Images;

public enum ImageProblem
{
    /// <summary>Not a JPEG, PNG or WebP, or the content does not match the declared type.</summary>
    TypeNotAllowed,

    /// <summary>The file cannot be decoded.</summary>
    Invalid,

    /// <summary>More pixels than <see cref="ImageProcessor.MaxPixels"/>.</summary>
    TooLarge,
}

/// <summary>Either the processed image or the reason it was rejected.</summary>
public sealed record ProcessedImage(byte[]? Content, int Width, int Height, ImageProblem? Problem)
{
    public const string ContentType = "image/webp";

    public static ProcessedImage Rejected(ImageProblem problem) => new(null, 0, 0, problem);
}

/// <summary>
/// Turns an uploaded file into a background the platform can safely serve: the type is checked against the file's
/// signature, huge images are rejected before decoding, the picture is turned upright, scaled down and re-encoded as
/// WebP. Re-encoding the pixels drops all metadata such as EXIF (camera, time, GPS position).
/// </summary>
public static class ImageProcessor
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    public const long MaxPixels = 40_000_000;

    /// <summary>Backgrounds are shown at most full-screen; 2400 px is sharp on large and high-density screens.</summary>
    public const int MaxDimension = 2400;

    public const int WebpQuality = 82;

    // Decoding takes up to MaxPixels * 4 bytes; one image at a time keeps memory use within the free hosting tier.
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private static readonly Dictionary<string, SKEncodedImageFormat> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = SKEncodedImageFormat.Jpeg,
        ["image/png"] = SKEncodedImageFormat.Png,
        ["image/webp"] = SKEncodedImageFormat.Webp,
    };

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && AllowedTypes.ContainsKey(contentType);

    /// <summary>Checks the file's magic bytes, so a renamed file of another type is not accepted.</summary>
    public static bool HasSignatureOf(ReadOnlySpan<byte> content, string contentType) =>
        AllowedTypes.TryGetValue(contentType, out var format) && format switch
        {
            SKEncodedImageFormat.Jpeg => content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
            SKEncodedImageFormat.Png => content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            SKEncodedImageFormat.Webp => content.Length >= 12
                && content[..4].SequenceEqual("RIFF"u8)
                && content[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };

    public static async Task<ProcessedImage> ProcessAsync(byte[] content, string contentType, CancellationToken cancellationToken)
    {
        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            return Process(content, contentType);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    private static ProcessedImage Process(byte[] content, string contentType)
    {
        if (!HasSignatureOf(content, contentType))
        {
            return ProcessedImage.Rejected(ImageProblem.TypeNotAllowed);
        }

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return ProcessedImage.Rejected(ImageProblem.Invalid);
        }

        if (codec.EncodedFormat != AllowedTypes[contentType])
        {
            return ProcessedImage.Rejected(ImageProblem.TypeNotAllowed);
        }

        // Checked from the header, before any pixels are decoded (decompression bombs).
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            return ProcessedImage.Rejected(ImageProblem.Invalid);
        }

        if ((long)info.Width * info.Height > MaxPixels)
        {
            return ProcessedImage.Rejected(ImageProblem.TooLarge);
        }

        var origin = codec.EncodedOrigin;
        var sideways = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (uprightWidth, uprightHeight) = sideways ? (info.Height, info.Width) : (info.Width, info.Height);
        var scale = Math.Min(1.0, (double)MaxDimension / Math.Max(uprightWidth, uprightHeight));
        var outputWidth = Math.Max(1, (int)Math.Round(uprightWidth * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(uprightHeight * scale));

        using var bitmap = Decode(codec, scale);
        if (bitmap is null)
        {
            return ProcessedImage.Rejected(ImageProblem.Invalid);
        }

        using var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var (decodedUprightWidth, _) = sideways ? (bitmap.Height, bitmap.Width) : (bitmap.Width, bitmap.Height);
        var drawScale = (float)outputWidth / decodedUprightWidth;
        canvas.SetMatrix(SKMatrix.CreateScale(drawScale, drawScale).PreConcat(Upright(origin, bitmap.Width, bitmap.Height)));

        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        return encoded is null
            ? ProcessedImage.Rejected(ImageProblem.Invalid)
            : new ProcessedImage(encoded.ToArray(), outputWidth, outputHeight, null);
    }

    /// <summary>Decodes at a reduced size when the codec supports it (JPEG), which saves most of the memory.</summary>
    private static SKBitmap? Decode(SKCodec codec, double scale)
    {
        var size = codec.GetScaledDimensions((float)scale);
        var decodeInfo = codec.Info.WithSize(size.Width, size.Height).WithColorType(SKColorType.Rgba8888);
        var bitmap = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
        {
            return bitmap;
        }

        bitmap.Dispose();
        if (size == codec.Info.Size)
        {
            return null;
        }

        // The codec cannot scale while decoding; decode at full size and scale when drawing.
        var fullInfo = codec.Info.WithColorType(SKColorType.Rgba8888);
        bitmap = new SKBitmap(fullInfo);
        result = codec.GetPixels(fullInfo, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    /// <summary>Maps the stored pixels to the upright picture, following the EXIF orientation (which is then dropped).</summary>
    private static SKMatrix Upright(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}
