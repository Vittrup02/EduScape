using Eduscape.Api.Hosting;
using Microsoft.Extensions.Configuration;

namespace Eduscape.Tests.Api;

public class CorsOriginsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void ArrayForm_ReturnsEachOrigin()
    {
        var config = Config(
            ("Cors:AllowedOrigins:0", "http://localhost:5173"),
            ("Cors:AllowedOrigins:1", "https://eduscape.pages.dev"));

        Assert.Equal(["http://localhost:5173", "https://eduscape.pages.dev"], CorsOrigins.FromConfiguration(config));
    }

    [Fact]
    public void CommaSeparatedValue_IsSplit()
    {
        var config = Config(("Cors:AllowedOrigins", "https://a.example,https://b.example"));

        Assert.Equal(["https://a.example", "https://b.example"], CorsOrigins.FromConfiguration(config));
    }

    [Fact]
    public void Whitespace_TrailingSlashes_AndEmptyEntries_AreIgnored()
    {
        var config = Config(("Cors:AllowedOrigins", " https://a.example/ , ,https://b.example ,"));

        Assert.Equal(["https://a.example", "https://b.example"], CorsOrigins.FromConfiguration(config));
    }

    [Fact]
    public void CommaSeparatedValue_OverridesArrayFromAppsettings()
    {
        var config = Config(
            ("Cors:AllowedOrigins:0", "http://localhost:5173"),
            ("Cors:AllowedOrigins", "https://eduscape.pages.dev"));

        Assert.Equal(["https://eduscape.pages.dev"], CorsOrigins.FromConfiguration(config));
    }

    [Fact]
    public void Missing_ReturnsEmpty()
    {
        Assert.Empty(CorsOrigins.FromConfiguration(Config()));
    }
}
