namespace Eduscape.Api.Play;

public static class PlayServiceCollectionExtensions
{
    public static IServiceCollection AddPlay(this IServiceCollection services)
    {
        services.AddScoped<TeamActions>();

        // One Render instance holds every connection, so no backplane is needed.
        services.AddSignalR();
        services.AddSingleton<ITeamNotifier, SignalRTeamNotifier>();
        return services;
    }
}
