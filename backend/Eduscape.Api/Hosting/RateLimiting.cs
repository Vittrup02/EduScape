using System.Threading.RateLimiting;

namespace Eduscape.Api.Hosting;

/// <summary>
/// Per-IP rate limits (ASP.NET Core rate limiting middleware). Render's proxy sets the client address via
/// X-Forwarded-For, see Program.cs. Rejected requests get 429 with the { "code": "TOO_MANY_REQUESTS" } body.
/// </summary>
public static class RateLimiting
{
    public const string AuthPolicy = "auth";
    public const string AuthPermitLimitSetting = "RateLimiting:Auth:PermitLimit";

    /// <summary>
    /// Login and registration attempts per IP address and minute. Generous because a whole school shares one public IP;
    /// brute force against a single account is stopped by Identity's lockout.
    /// </summary>
    public const int DefaultAuthPermitLimit = 20;

    public const string JoinPolicy = "join";
    public const string JoinPermitLimitSetting = "RateLimiting:Join:PermitLimit";

    /// <summary>
    /// Attempts per IP address and minute at anything that takes a PIN (lookup and join). A whole school shares one
    /// public IP and every student makes two such calls, so a class joining at once needs ~60. Guessing is further
    /// limited by the teacher locking the session once everyone is in and by PINs expiring.
    /// </summary>
    public const int DefaultJoinPermitLimit = 100;

    public static IServiceCollection AddEduscapeRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var authPermitLimit = configuration.GetValue(AuthPermitLimitSetting, DefaultAuthPermitLimit);
        var joinPermitLimit = configuration.GetValue(JoinPermitLimitSetting, DefaultJoinPermitLimit);

        services.AddRateLimiter(options =>
        {
            // The status code pages middleware adds the { "code": "TOO_MANY_REQUESTS" } body.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Each policy keeps its own counters, so joining students do not use up the teachers' login budget.
            options.AddPolicy(AuthPolicy, context => PerIpAddressAndMinute(context, authPermitLimit));
            options.AddPolicy(JoinPolicy, context => PerIpAddressAndMinute(context, joinPermitLimit));
        });

        return services;
    }

    private static RateLimitPartition<string> PerIpAddressAndMinute(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}
