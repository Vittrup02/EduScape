using System.Security.Claims;

namespace Eduscape.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in teacher's id. Only valid on endpoints that require authorization.</summary>
    public static Guid GetTeacherId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The user is not signed in."));
}
