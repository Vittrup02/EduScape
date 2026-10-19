using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eduscape.Api.Hosting;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eduscape.Tests.Api;

/// <summary>
/// The API running against its own throw-away PostgreSQL database (see <see cref="PostgresDatabaseFixture"/>).
/// Without a configured PostgreSQL only tests that never touch the database can use it.
/// </summary>
public sealed class EduscapeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "correct horse battery";

    public PostgresDatabaseFixture Database { get; } = new();

    public static string NewEmail() => $"teacher-{Guid.NewGuid():N}@example.com";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (Database.ConnectionString is { } connectionString)
        {
            builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", connectionString);
        }

        // Every test calls from the same (test server) address; the rate limits themselves are tested separately.
        builder.UseSetting(RateLimiting.AuthPermitLimitSetting, "100000");
        builder.UseSetting(RateLimiting.JoinPermitLimitSetting, "100000");
        builder.UseSetting(RateLimiting.UploadPermitLimitSetting, "100000");

        // Never the real bucket from user-secrets; upload tests use FakeImageStorage.
        builder.UseSetting("R2:Bucket", string.Empty);
    }

    public Task InitializeAsync() => Database.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Database.DisposeAsync();
    }

    /// <summary>The same API (and database) with some services replaced, e.g. the clock or the PIN generator.</summary>
    public WebApplicationFactory<Program> WithServices(Action<IServiceCollection> configureServices) =>
        WithWebHostBuilder(builder => builder.ConfigureTestServices(configureServices));

    /// <summary>Registers a new teacher and returns a client that sends their access token.</summary>
    /// <param name="app">The API to call; this factory by default.</param>
    public async Task<SignedInTeacher> RegisterTeacherAsync(WebApplicationFactory<Program>? app = null)
    {
        app ??= this;
        var email = NewEmail();
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AccessTokenResponse>()
            ?? throw new InvalidOperationException("Register returned no tokens.");

        await using var db = Database.CreateContext();
        var teacherId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

        return new SignedInTeacher(teacherId, email, tokens, ClientWithToken(tokens.AccessToken, app));
    }

    /// <summary>Logs an existing teacher in and returns a client that sends the new access token.</summary>
    public async Task<HttpClient> LoginAsync(string email, WebApplicationFactory<Program>? app = null)
    {
        var response = await (app ?? this).CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AccessTokenResponse>()
            ?? throw new InvalidOperationException("Login returned no tokens.");
        return ClientWithToken(tokens.AccessToken, app);
    }

    public HttpClient ClientWithToken(string accessToken, WebApplicationFactory<Program>? app = null)
    {
        var client = (app ?? this).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Adds a playable room: one scene with an exit door.</summary>
    public async Task<EscapeRoom> AddRoomAsync(Guid? ownerTeacherId, string title, bool isTemplate = false)
    {
        var room = new EscapeRoom(title, Subject.Mathematics, GradeLevel.Grade7, ownerTeacherId, isTemplate);
        room.AddScene("Scene", "builtin:classroom")
            .AddObject(new ExitDoor(new Position(50, 50), "builtin:exit-door", null));
        await using var db = Database.CreateContext();
        db.EscapeRooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }
}

public sealed record SignedInTeacher(Guid Id, string Email, AccessTokenResponse Tokens, HttpClient Client);
