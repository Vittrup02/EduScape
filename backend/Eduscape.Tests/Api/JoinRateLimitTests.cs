using System.Net;
using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Domain.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Eduscape.Tests.Api;

/// <summary>
/// The limit applies before the PIN is even checked, so malformed PINs (rejected without a database) are enough here.
/// </summary>
public class JoinRateLimitTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private const string MalformedPin = "nope";

    private readonly WebApplicationFactory<Program> _app =
        factory.WithWebHostBuilder(builder => builder.UseSetting(RateLimiting.JoinPermitLimitSetting, "2"));

    [Fact]
    public async Task Lookup_IsRateLimitedPerIpAddress()
    {
        var client = ClientFrom("203.0.113.1");

        await SessionApi.LookupAsync(client, MalformedPin);
        await SessionApi.LookupAsync(client, MalformedPin);
        var third = await SessionApi.LookupAsync(client, MalformedPin);

        await TeacherAuthTests.AssertErrorAsync(third, HttpStatusCode.TooManyRequests, ApiErrors.TooManyRequests);
    }

    [Fact]
    public async Task LookupAndJoin_ShareTheBudget()
    {
        var client = ClientFrom("203.0.113.2");

        await SessionApi.LookupAsync(client, MalformedPin);
        await SessionApi.JoinAsync(client, MalformedPin, null, "Ida");
        var third = await SessionApi.JoinAsync(client, MalformedPin, null, "Ida");

        await TeacherAuthTests.AssertErrorAsync(third, HttpStatusCode.TooManyRequests, ApiErrors.TooManyRequests);
    }

    [Fact]
    public async Task AnotherIpAddress_HasItsOwnBudget()
    {
        var first = ClientFrom("203.0.113.3");
        var second = ClientFrom("203.0.113.4");
        await SessionApi.LookupAsync(first, MalformedPin);
        await SessionApi.LookupAsync(first, MalformedPin);

        var fromSecond = await SessionApi.LookupAsync(second, MalformedPin);

        // Rejected for the PIN, not for the rate limit.
        await TeacherAuthTests.AssertErrorAsync(fromSecond, HttpStatusCode.BadRequest, DomainErrors.SessionPinInvalid);
    }

    [Fact]
    public async Task Reconnect_IsNotLimitedByTheJoinBudget()
    {
        var client = ClientFrom("203.0.113.5");
        await SessionApi.LookupAsync(client, MalformedPin);
        await SessionApi.LookupAsync(client, MalformedPin);

        for (var i = 0; i < 3; i++)
        {
            var reconnect = await SessionApi.ReconnectAsync(client, deviceToken: null);
            await TeacherAuthTests.AssertErrorAsync(reconnect, HttpStatusCode.NotFound, ApiErrors.DeviceNotRecognized);
        }
    }

    /// <summary>A client behind Render's proxy, which reports the client address in X-Forwarded-For.</summary>
    private HttpClient ClientFrom(string ipAddress)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ipAddress);
        return client;
    }
}
