using Eduscape.Domain.Common;

namespace Eduscape.Domain.Classes;

/// <summary>
/// A label such as "9.A" owned by a teacher, used to group sessions in the history.
/// Deliberately holds no student list (data minimisation).
/// </summary>
public sealed class Class
{
    public Class(Guid ownerTeacherId, string name)
    {
        OwnerTeacherId = ownerTeacherId;
        Name = Guard.Required(name, TextLimits.Name);
    }

    // For EF Core
    private Class() => Name = null!;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid OwnerTeacherId { get; private set; }

    public string Name { get; private set; }

    public void Rename(string name) => Name = Guard.Required(name, TextLimits.Name);
}
