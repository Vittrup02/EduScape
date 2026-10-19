using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eduscape.Api.Auth;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

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

        // Every test logs in from the same (test server) address; the rate limit itself is tested separately.
        builder.UseSetting(AuthServiceCollectionExtensions.PermitLimitSetting, "100000");
    }

    public Task InitializeAsync() => Database.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Database.DisposeAsync();
    }

    /// <summary>Registers a new teacher and returns a client that sends their access token.</summary>
    public async Task<SignedInTeacher> RegisterTeacherAsync()
    {
        var email = NewEmail();
        var response = await CreateClient().PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AccessTokenResponse>()
            ?? throw new InvalidOperationException("Register returned no tokens.");

        await using var db = Database.CreateContext();
        var teacherId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

        return new SignedInTeacher(teacherId, email, tokens, ClientWithToken(tokens.AccessToken));
    }

    public HttpClient ClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task<EscapeRoom> AddRoomAsync(Guid? ownerTeacherId, string title)
    {
        var room = new EscapeRoom(title, Subject.Mathematics, GradeLevel.Grade7, ownerTeacherId);
        await using var db = Database.CreateContext();
        db.EscapeRooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }
}

public sealed record SignedInTeacher(Guid Id, string Email, AccessTokenResponse Tokens, HttpClient Client);
