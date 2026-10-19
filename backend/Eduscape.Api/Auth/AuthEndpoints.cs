using System.Security.Claims;
using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Eduscape.Api.Auth;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// Register, login, refresh and logout for teachers. Successful register/login/refresh respond with Identity's
/// <c>AccessTokenResponse</c> (<c>tokenType, accessToken, expiresIn, refreshToken</c>), written by the bearer token handler.
/// Built on <see cref="SignInManager{TUser}"/> like <c>MapIdentityApi</c>, but with error codes and rate limiting.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimiting.AuthPolicy);
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimiting.AuthPolicy);
        auth.MapPost("/refresh", RefreshAsync);
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, UserManager<Teacher> userManager, SignInManager<Teacher> signInManager)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (email.Length > AuthServiceCollectionExtensions.MaxEmailLength)
        {
            return Error(StatusCodes.Status400BadRequest, ApiErrors.EmailInvalid);
        }

        if (password.Length > AuthServiceCollectionExtensions.MaxPasswordLength)
        {
            return Error(StatusCodes.Status400BadRequest, ApiErrors.PasswordTooLong);
        }

        var teacher = new Teacher(email);
        var result = await userManager.CreateAsync(teacher, password);
        if (!result.Succeeded)
        {
            return Error(StatusCodes.Status400BadRequest, ToErrorCode(result.Errors));
        }

        // Signed in straight away, so registering is one step for the teacher.
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInAsync(teacher, isPersistent: false);
        return TypedResults.Empty;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, SignInManager<Teacher> signInManager)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (email.Length == 0 || password.Length is 0 or > AuthServiceCollectionExtensions.MaxPasswordLength)
        {
            return Error(StatusCodes.Status401Unauthorized, ApiErrors.InvalidCredentials);
        }

        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return TypedResults.Empty;
        }

        return result.IsLockedOut
            ? Error(StatusCodes.Status429TooManyRequests, ApiErrors.LoginLockedOut)
            : Error(StatusCodes.Status401Unauthorized, ApiErrors.InvalidCredentials);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        SignInManager<Teacher> signInManager,
        IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
        TimeProvider timeProvider)
    {
        var protector = bearerTokenOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = string.IsNullOrEmpty(request.RefreshToken) ? null : protector.Unprotect(request.RefreshToken);

        // The security stamp check rejects refresh tokens issued before the teacher logged out.
        if (ticket?.Properties.ExpiresUtc is not { } expiresUtc
            || timeProvider.GetUtcNow() >= expiresUtc
            || await signInManager.ValidateSecurityStampAsync(ticket.Principal) is not { } teacher)
        {
            return Error(StatusCodes.Status401Unauthorized, ApiErrors.Unauthorized);
        }

        var principal = await signInManager.CreateUserPrincipalAsync(teacher);
        return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }

    /// <summary>
    /// Revokes every refresh token of the teacher (on all devices, which suits shared school computers).
    /// Access tokens are not stored anywhere and expire on their own within 30 minutes.
    /// </summary>
    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, UserManager<Teacher> userManager)
    {
        if (await userManager.GetUserAsync(user) is { } teacher)
        {
            await userManager.UpdateSecurityStampAsync(teacher);
        }

        return TypedResults.NoContent();
    }

    private static string ToErrorCode(IEnumerable<IdentityError> errors)
    {
        var codes = errors.Select(e => e.Code).ToHashSet();
        if (codes.Contains(nameof(IdentityErrorDescriber.InvalidEmail)) || codes.Contains(nameof(IdentityErrorDescriber.InvalidUserName)))
        {
            return ApiErrors.EmailInvalid;
        }

        if (codes.Contains(nameof(IdentityErrorDescriber.DuplicateEmail)) || codes.Contains(nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return ApiErrors.EmailTaken;
        }

        return codes.Contains(nameof(IdentityErrorDescriber.PasswordTooShort))
            ? ApiErrors.PasswordTooShort
            : ApiErrors.BadRequest;
    }

    private static IResult Error(int statusCode, string code) =>
        TypedResults.Json(new ApiError(code), statusCode: statusCode);
}
