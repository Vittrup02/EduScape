using System.Net;
using System.Net.Http.Json;
using Eduscape.Api.Auth;
using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Tests.Api;

public class TeacherAuthTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private const string Password = EduscapeApiFactory.Password;

    [PostgresFact]
    public async Task Register_CreatesTeacher_AndSignsIn()
    {
        var email = EduscapeApiFactory.NewEmail();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await ReadTokensAsync(response);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));

        // Stored in this test's database, not in whatever ConnectionStrings:Default the API would use otherwise.
        await using var db = factory.Database.CreateContext();
        var teacher = await db.Users.SingleAsync(u => u.Email == email);
        Assert.NotEqual(Password, teacher.PasswordHash);

        var rooms = await factory.ClientWithToken(tokens.AccessToken).GetAsync("/api/teacher/rooms");
        Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);
    }

    [PostgresFact]
    public async Task Register_WithTakenEmail_ReturnsEmailTaken_IgnoringCase()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await RegisterAsync(teacher.Email.ToUpperInvariant(), Password);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.EmailTaken);
    }

    [PostgresFact]
    public async Task Register_WithShortPassword_ReturnsPasswordTooShort()
    {
        var response = await RegisterAsync(EduscapeApiFactory.NewEmail(), new string('x', AuthServiceCollectionExtensions.MinPasswordLength - 1));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.PasswordTooShort);
    }

    [PostgresFact]
    public async Task Register_WithTooLongPassword_ReturnsPasswordTooLong()
    {
        var response = await RegisterAsync(EduscapeApiFactory.NewEmail(), new string('x', AuthServiceCollectionExtensions.MaxPasswordLength + 1));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.PasswordTooLong);
    }

    [PostgresFact]
    public async Task Register_WithInvalidEmail_ReturnsEmailInvalid()
    {
        var response = await RegisterAsync("not-an-email", Password);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.EmailInvalid);
    }

    [PostgresFact]
    public async Task Register_AllowsDanishLettersInEmail()
    {
        var response = await RegisterAsync($"søren.æblø-{Guid.NewGuid():N}@example.com", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [PostgresFact]
    public async Task Login_WithCorrectPassword_ReturnsWorkingToken()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await LoginAsync($"  {teacher.Email.ToUpperInvariant()} ", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await ReadTokensAsync(response);
        var rooms = await factory.ClientWithToken(tokens.AccessToken).GetAsync("/api/teacher/rooms");
        Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);
    }

    [PostgresFact]
    public async Task Login_WithWrongPasswordOrUnknownEmail_GivesTheSameAnswer()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var wrongPassword = await LoginAsync(teacher.Email, "wrong password!");
        var unknownEmail = await LoginAsync(EduscapeApiFactory.NewEmail(), Password);

        await AssertErrorAsync(wrongPassword, HttpStatusCode.Unauthorized, ApiErrors.InvalidCredentials);
        await AssertErrorAsync(unknownEmail, HttpStatusCode.Unauthorized, ApiErrors.InvalidCredentials);
    }

    [PostgresFact]
    public async Task Login_AfterFiveWrongPasswords_IsLockedOut_EvenWithTheRightPassword()
    {
        var teacher = await factory.RegisterTeacherAsync();
        for (var attempt = 1; attempt < 5; attempt++)
        {
            await AssertErrorAsync(await LoginAsync(teacher.Email, "wrong password!"), HttpStatusCode.Unauthorized, ApiErrors.InvalidCredentials);
        }

        await AssertErrorAsync(await LoginAsync(teacher.Email, "wrong password!"), HttpStatusCode.TooManyRequests, ApiErrors.LoginLockedOut);
        await AssertErrorAsync(await LoginAsync(teacher.Email, Password), HttpStatusCode.TooManyRequests, ApiErrors.LoginLockedOut);
    }

    [PostgresFact]
    public async Task Refresh_ReturnsNewWorkingTokens()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await RefreshAsync(teacher.Tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await ReadTokensAsync(response);
        var rooms = await factory.ClientWithToken(tokens.AccessToken).GetAsync("/api/teacher/rooms");
        Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);
    }

    [PostgresFact]
    public async Task Logout_RevokesRefreshTokens()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var logout = await teacher.Client.PostAsync("/api/auth/logout", content: null);
        var refresh = await RefreshAsync(teacher.Tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await AssertErrorAsync(refresh, HttpStatusCode.Unauthorized, ApiErrors.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_ReturnsUnauthorized()
    {
        var response = await RefreshAsync("not-a-refresh-token");

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ApiErrors.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().PostAsync("/api/auth/logout", content: null);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ApiErrors.Unauthorized);
    }

    [PostgresFact]
    public async Task Login_IsRateLimitedPerIpAddress()
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.UseSetting(RateLimiting.AuthPermitLimitSetting, "2"))
            .CreateClient();
        var email = EduscapeApiFactory.NewEmail();

        await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var third = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        await AssertErrorAsync(third, HttpStatusCode.TooManyRequests, ApiErrors.TooManyRequests);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private static async Task<AccessTokenResponse> ReadTokensAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<AccessTokenResponse>()
        ?? throw new InvalidOperationException("No tokens in the response.");

    internal static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(code, error?.Code);
    }
}
