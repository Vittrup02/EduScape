namespace Eduscape.Api.Play;

public static class PlayServiceCollectionExtensions
{
    public static IServiceCollection AddPlay(this IServiceCollection services)
    {
        services.AddScoped<TeamActions>();
        return services;
    }
}
