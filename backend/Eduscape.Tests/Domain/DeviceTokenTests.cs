using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class DeviceTokenTests
{
    [Fact]
    public void Create_ReturnsUrlSafe256BitTokens()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => DeviceToken.Create()).ToList();

        Assert.All(tokens, token =>
        {
            Assert.Equal(43, token.Length);
            Assert.True(token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), token);
        });
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void Hash_IsDeterministicLowercaseHex_ThatFitsTheColumn()
    {
        var token = DeviceToken.Create();

        var hash = DeviceToken.Hash(token);

        Assert.Equal(hash, DeviceToken.Hash(token));
        Assert.Equal(TextLimits.DeviceToken, hash.Length);
        Assert.True(hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'), hash);
        Assert.NotEqual(hash, DeviceToken.Hash(DeviceToken.Create()));
    }
}
