using Eduscape.Infrastructure;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;

namespace Eduscape.Tests.Infrastructure;

/// <summary>
/// Creates a throw-away database (eduscape_test_&lt;guid&gt;) on the server from <c>ConnectionStrings:Default</c>
/// (API user-secrets locally, <c>ConnectionStrings__Default</c> in CI), migrates it like
/// <c>dotnet ef database update</c> in Development, and drops it afterwards.
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public static readonly string? ServerConnectionString = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables()
        .Build()
        .GetConnectionString(DependencyInjection.ConnectionStringName);

    // Kept alive for the fixture's lifetime: the DbContext options resolve services (e.g. logging) from it.
    private ServiceProvider? _provider;
    private DbContextOptions<EduscapeDbContext>? _options;

    /// <summary>Connection string of the throw-away database; null when no PostgreSQL is configured.</summary>
    public string? ConnectionString { get; private set; }

    public EduscapeDbContext CreateContext() =>
        new(_options ?? throw new InvalidOperationException("The test database is not initialised."));

    public async Task InitializeAsync()
    {
        if (ServerConnectionString is null)
        {
            return;
        }

        var connectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString)
        {
            Database = $"eduscape_test_{Guid.NewGuid():N}",
        }.ConnectionString;
        ConnectionString = connectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString,
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        _provider = new ServiceCollection()
            .AddInfrastructure(configuration, environment)
            .BuildServiceProvider();
        _options = _provider.CreateScope().ServiceProvider.GetRequiredService<DbContextOptions<EduscapeDbContext>>();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_options is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using (var context = CreateContext())
        {
            await context.Database.EnsureDeletedAsync();
        }

        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}

/// <summary>
/// A test that needs PostgreSQL. Skipped locally when no connection string is configured;
/// on CI it always runs, so a missing database fails the build instead of passing silently.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        if (PostgresDatabaseFixture.ServerConnectionString is null && !isCi)
        {
            Skip = "No PostgreSQL configured. Set ConnectionStrings:Default with `dotnet user-secrets` on Eduscape.Api.";
        }
    }
}
