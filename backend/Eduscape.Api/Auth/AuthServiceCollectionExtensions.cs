using System.Threading.RateLimiting;
using Eduscape.Infrastructure.Identity;
using Eduscape.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Eduscape.Api.Auth;

/// <summary>
/// Teacher accounts with ASP.NET Core Identity and its opaque bearer tokens. The frontend (workers.dev) and the API
/// (onrender.com) are different sites, where an auth cookie would be a third-party cookie that Safari blocks;
/// a bearer token in the Authorization header works everywhere and leaves no CSRF surface.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    /// <summary>Identity's limit on e-mail and user name columns.</summary>
    public const int MaxEmailLength = 256;

    public const string RateLimitPolicy = "auth";
    public const string PermitLimitSetting = "RateLimiting:Auth:PermitLimit";

    /// <summary>
    /// Login and registration attempts per IP address and minute. Generous because a whole school shares one public IP;
    /// brute force against a single account is stopped by Identity's lockout.
    /// </summary>
    public const int DefaultPermitLimit = 20;

    public static IServiceCollection AddTeacherAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<Teacher>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // The user name is the e-mail address, which Identity validates; allow every character a valid address may hold.
                options.User.AllowedUserNameCharacters = string.Empty;

                // Length over composition rules (NIST 800-63B): easy for every teacher, hard to guess.
                options.Password.RequiredLength = MinPasswordLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<EduscapeDbContext>();

        // Short-lived access tokens; the refresh token keeps teachers signed in and is revoked on logout (security stamp).
        services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromMinutes(30);
                options.RefreshTokenExpiration = TimeSpan.FromDays(7);
            });
        services.AddAuthorization();

        // Tokens are protected with the Data Protection key ring. Render's file system is ephemeral and the service
        // restarts after sleeping, so the keys live in the database; otherwise every restart would sign everyone out.
        // The key ring is loaded on first use, not at startup.
        services.AddDataProtection()
            .SetApplicationName("eduscape")
            .PersistKeysToDbContext<EduscapeDbContext>();

        var permitLimit = configuration.GetValue(PermitLimitSetting, DefaultPermitLimit);
        services.AddRateLimiter(options =>
        {
            // The status code pages middleware adds the { "code": "TOO_MANY_REQUESTS" } body.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }
}
