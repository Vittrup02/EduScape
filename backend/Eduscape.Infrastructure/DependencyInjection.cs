using Eduscape.Infrastructure.Persistence;
using Eduscape.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eduscape.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registers the database. Nothing connects at startup; the connection string
    /// (<c>ConnectionStrings:Default</c>, from user-secrets or environment variables) is only used on first query.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        services.AddDbContext<EduscapeDbContext>(options =>
        {
            // Without a connection string the app still starts (e.g. /health, `dotnet ef migrations add`);
            // database access then fails with Npgsql's "connection string not set" error.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql();
            }
            else
            {
                options.UseNpgsql(connectionString);
            }

            if (environment.IsDevelopment())
            {
                options
                    .UseSeeding(DemoRoomSeeder.Seed)
                    .UseAsyncSeeding(DemoRoomSeeder.SeedAsync);
            }
        });

        return services;
    }
}
