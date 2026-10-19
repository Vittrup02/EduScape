using Microsoft.AspNetCore.Identity;

namespace Eduscape.Infrastructure.Identity;

/// <summary>
/// A teacher account (ASP.NET Core Identity user). Students never have accounts.
/// The domain refers to teachers only by id (e.g. <c>EscapeRoom.OwnerTeacherId</c>), so it stays free of Identity.
/// </summary>
public sealed class Teacher : IdentityUser<Guid>
{
    public Teacher()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Teacher(string email)
        : this()
    {
        Email = email;
        UserName = email;
    }
}
