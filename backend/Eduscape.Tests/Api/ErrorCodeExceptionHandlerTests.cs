using System.Text.Json;
using Eduscape.Api.Errors;
using Eduscape.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eduscape.Tests.Api;

public class ErrorCodeExceptionHandlerTests
{
    private readonly ErrorCodeExceptionHandler _handler = new(NullLogger<ErrorCodeExceptionHandler>.Instance);

    [Fact]
    public async Task DomainException_BecomesBadRequestWithItsCode()
    {
        var (status, body) = await HandleAsync(new DomainException(DomainErrors.SessionClosed));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("""{"code":"SESSION_CLOSED"}""", body);
    }

    [Fact]
    public async Task BadHttpRequest_KeepsItsStatusCode()
    {
        var (status, body) = await HandleAsync(
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
        Assert.Equal("""{"code":"BAD_REQUEST"}""", body);
    }

    [Fact]
    public async Task UnexpectedException_BecomesInternalErrorWithoutLeakingTheMessage()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("Connection string: secret"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("""{"code":"INTERNAL_ERROR"}""", body);
    }

    private async Task<(int Status, string Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.StartsWith("application/json", context.Response.ContentType);
        var body = JsonSerializer.Serialize(JsonDocument.Parse(responseBody.ToArray()).RootElement);
        return (context.Response.StatusCode, body);
    }
}
