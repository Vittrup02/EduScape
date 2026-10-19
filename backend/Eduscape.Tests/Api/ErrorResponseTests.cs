using System.Net;
using System.Net.Http.Json;
using Eduscape.Api.Errors;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Eduscape.Tests.Api;

public class ErrorResponseTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task UnknownRoute_ReturnsNotFoundCode()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ApiErrors.NotFound, error?.Code);
    }

    [Fact]
    public async Task ErrorResponses_KeepCorsHeaders_SoTheFrontendCanReadTheCode()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal("http://localhost:5173", Assert.Single(values));
    }

    [Theory]
    [InlineData(401, ApiErrors.Unauthorized)]
    [InlineData(403, ApiErrors.Forbidden)]
    [InlineData(404, ApiErrors.NotFound)]
    [InlineData(429, ApiErrors.TooManyRequests)]
    [InlineData(400, ApiErrors.BadRequest)]
    [InlineData(405, ApiErrors.BadRequest)]
    [InlineData(500, ApiErrors.InternalError)]
    [InlineData(503, ApiErrors.InternalError)]
    public void FromStatusCode_MapsToGenericCode(int statusCode, string expected)
    {
        Assert.Equal(expected, ApiErrors.FromStatusCode(statusCode));
    }
}
