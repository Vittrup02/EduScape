using Eduscape.Domain.Classes;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eduscape.Infrastructure.Persistence.Configurations;

internal sealed class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("Classes");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(TextLimits.Name);
        builder.HasIndex(c => c.OwnerTeacherId);

        builder.HasOne<Teacher>()
            .WithMany()
            .HasForeignKey(c => c.OwnerTeacherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Pin).HasMaxLength(Session.PinLength).IsFixedLength();

        // A PIN is unique among sessions that are not closed; closed sessions release it.
        builder.HasIndex(s => s.Pin)
            .IsUnique()
            .HasFilter("\"Status\" <> 'Closed'")
            .HasDatabaseName("IX_Sessions_Pin_Active");

        // Finding expired sessions to close, which releases their PINs.
        builder.HasIndex(s => new { s.Status, s.ExpiresAt });

        // Sessions are the teacher's history, so rooms and classes cannot be deleted from under them.
        builder.HasOne<EscapeRoom>()
            .WithMany()
            .HasForeignKey(s => s.EscapeRoomId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Class>()
            .WithMany()
            .HasForeignKey(s => s.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Teams)
            .WithOne()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Ignore(t => t.SolvedLockIds);
        builder.Ignore(t => t.OpenedDoorIds);

        builder.HasMany(t => t.LockProgress)
            .WithOne()
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.OpenedDoors)
            .WithOne()
            .HasForeignKey(d => d.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Participants)
            .WithOne()
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamLockProgressConfiguration : IEntityTypeConfiguration<TeamLockProgress>
{
    public void Configure(EntityTypeBuilder<TeamLockProgress> builder)
    {
        builder.ToTable("TeamLockProgress");

        // One row per team and lock: a lock solved twice at the same time is still one registration.
        builder.HasKey(p => new { p.TeamId, p.LockId });
        builder.Ignore(p => p.IsSolved);

        builder.HasOne<Lock>()
            .WithMany()
            .HasForeignKey(p => p.LockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamOpenedDoorConfiguration : IEntityTypeConfiguration<TeamOpenedDoor>
{
    public void Configure(EntityTypeBuilder<TeamOpenedDoor> builder)
    {
        builder.ToTable("TeamOpenedDoors");
        builder.HasKey(d => new { d.TeamId, d.DoorId });

        builder.HasOne<Door>()
            .WithMany()
            .HasForeignKey(d => d.DoorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.ToTable("Participants");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Nickname).HasMaxLength(TextLimits.Nickname);
        builder.Property(p => p.DeviceTokenHash).HasMaxLength(TextLimits.DeviceToken);
        builder.HasIndex(p => p.DeviceTokenHash).IsUnique();
    }
}
