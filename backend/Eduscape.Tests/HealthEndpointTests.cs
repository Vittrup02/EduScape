using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Eduscape.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_AllowsLocalFrontendOrigin()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal("http://localhost:5173", Assert.Single(values));
    }

    [Fact]
    public async Task Health_AllowsOriginsFromCommaSeparatedSetting()
    {
        // Same shape as the Cors__AllowedOrigins environment variable on Render
        var client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins", "https://a.example, https://b.example/"))
            .CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://b.example");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal("https://b.example", Assert.Single(values));
    }
}
