using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Eduscape.Infrastructure.Persistence;

/// <summary>Recognises database errors the application handles, without leaking Npgsql types into callers.</summary>
public static class DatabaseErrors
{
    /// <summary>Unique index on the PIN of sessions that are not closed.</summary>
    public const string ActiveSessionPinIndex = "IX_Sessions_Pin_Active";

    public static bool IsUniqueViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;
}
