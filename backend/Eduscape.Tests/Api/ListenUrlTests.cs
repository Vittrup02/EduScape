using Eduscape.Api.Hosting;

namespace Eduscape.Tests.Api;

public class ListenUrlTests
{
    [Theory]
    [InlineData("10000", "http://0.0.0.0:10000")]
    [InlineData("8080", "http://0.0.0.0:8080")]
    [InlineData("1", "http://0.0.0.0:1")]
    [InlineData("65535", "http://0.0.0.0:65535")]
    public void FromPort_ListensOnAllInterfaces(string port, string expected)
    {
        Assert.Equal(expected, ListenUrl.FromPort(port));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromPort_WithoutPort_KeepsDefaults(string? port)
    {
        Assert.Null(ListenUrl.FromPort(port));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("-1")]
    [InlineData("80.5")]
    public void FromPort_InvalidPort_Throws(string port)
    {
        Assert.Throws<InvalidOperationException>(() => ListenUrl.FromPort(port));
    }
}
