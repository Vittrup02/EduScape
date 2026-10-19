using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Tests.Infrastructure;

public class MigrationTests
{
    [Fact]
    public void Model_HasNoChangesMissingAMigration()
    {
        // No database needed: compares the EF model with the latest migration snapshot.
        var options = new DbContextOptionsBuilder<EduscapeDbContext>().UseNpgsql().Options;
        using var context = new EduscapeDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The EF model has changed. Run `dotnet ef migrations add <Name> --project Eduscape.Infrastructure --startup-project Eduscape.Api`.");
    }
}
