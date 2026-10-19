using Eduscape.Domain.Classes;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Infrastructure.Persistence;

public sealed class EduscapeDbContext(DbContextOptions<EduscapeDbContext> options) : DbContext(options)
{
    public DbSet<EscapeRoom> EscapeRooms => Set<EscapeRoom>();

    public DbSet<Scene> Scenes => Set<Scene>();

    public DbSet<RoomObject> RoomObjects => Set<RoomObject>();

    public DbSet<Class> Classes => Set<Class>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Participant> Participants => Set<Participant>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Fixed lists are stored as keys (enum names) that the frontend translates.
        StoreAsKey<Subject>(configurationBuilder);
        StoreAsKey<GradeLevel>(configurationBuilder);
        StoreAsKey<AnswerType>(configurationBuilder);
        StoreAsKey<RoomObjectType>(configurationBuilder);
        StoreAsKey<SessionMode>(configurationBuilder);
        StoreAsKey<SessionStatus>(configurationBuilder);
        StoreAsKey<TeamColor>(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EduscapeDbContext).Assembly);

    private static void StoreAsKey<TEnum>(ModelConfigurationBuilder configurationBuilder)
        where TEnum : struct, Enum =>
        configurationBuilder.Properties<TEnum>().HaveConversion<string>().HaveMaxLength(TextLimits.EnumKey);
}
