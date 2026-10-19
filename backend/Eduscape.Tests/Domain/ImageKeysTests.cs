using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class ImageKeysTests
{
    [Theory]
    [InlineData("builtin:classroom")]
    [InlineData("builtin:code-lock")]
    [InlineData("uploads/0123456789abcdef0123456789abcdef.webp")]
    public void IsValid_AcceptsBuiltInAndProcessedUploads(string key) => Assert.True(ImageKeys.IsValid(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("classroom")]
    [InlineData("builtin:")]
    [InlineData("builtin:Class Room")]
    [InlineData("incoming/0123456789abcdef0123456789abcdef")]
    [InlineData("uploads/0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("uploads/../secret.webp")]
    [InlineData("https://example.com/cat.webp")]
    public void IsValid_RejectsAnythingElse(string? key) => Assert.False(ImageKeys.IsValid(key));

    [Fact]
    public void NewUploadKey_IsValidAndUnique()
    {
        var first = ImageKeys.NewUploadKey();

        Assert.True(ImageKeys.IsUpload(first));
        Assert.NotEqual(first, ImageKeys.NewUploadKey());
    }
}
