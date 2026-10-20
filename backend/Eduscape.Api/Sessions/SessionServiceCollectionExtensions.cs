using Eduscape.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eduscape.Api.Sessions;

public static class SessionServiceCollectionExtensions
{
    public static IServiceCollection AddSessions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SessionOptions>()
            .Bind(configuration.GetSection(SessionOptions.SectionName))
            .Validate(options => options.Lifetime > TimeSpan.Zero, "Sessions:Lifetime must be positive.")
            .Validate(options => options.PreviewLifetime > TimeSpan.Zero, "Sessions:PreviewLifetime must be positive.");

        // Registered here so tests can replace the clock (e.g. to let a PIN expire).
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPinGenerator, RandomPinGenerator>();
        services.AddScoped<SessionStarter>();
        services.AddSingleton<IDashboardNotifier, SignalRDashboardNotifier>();
        return services;
    }
}
