using Eduscape.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds a small demo escape room in Development so the game can be tested without the editor.
/// Runs from EF Core's seeding hook (<c>dotnet ef database update</c> / <c>Migrate</c>) and is idempotent.
/// </summary>
public static class DemoRoomSeeder
{
    /// <summary>Fixed id so tests and local tooling can find the demo room.</summary>
    public static readonly Guid DemoRoomId = new("01990000-0000-7000-8000-00000000de30");

    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        if (context.Set<EscapeRoom>().Any(r => r.Id == DemoRoomId))
        {
            return;
        }

        context.Add(BuildDemoRoom());
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken cancellationToken)
    {
        if (await context.Set<EscapeRoom>().AnyAsync(r => r.Id == DemoRoomId, cancellationToken))
        {
            return;
        }

        context.Add(BuildDemoRoom());
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>How long a team waits after a wrong answer in the demo: long enough to notice, short enough for tests.</summary>
    public const int DemoCooldownSeconds = 5;

    /// <summary>
    /// Two scenes: a letter, a code lock, a numeric lock with tolerance (after the code lock) and a door in the first;
    /// a multiple-choice lock and the exit door in the second. The doors open once their scene's locks are solved.
    /// Uses only built-in assets (see frontend/src/assets/ASSETS.md).
    /// </summary>
    public static EscapeRoom BuildDemoRoom()
    {
        var room = new EscapeRoom(
            "Demo: Flugten fra matematiklokalet",
            Subject.Mathematics,
            GradeLevel.Grade7,
            isTemplate: true,
            id: DemoRoomId);
        room.SetWrongAnswerCooldown(DemoCooldownSeconds);

        var classroom = room.AddScene("Klasselokalet", "builtin:classroom");
        var library = room.AddScene("Biblioteket", "builtin:library");

        classroom.AddObject(new Letter(new Position(15, 70), "builtin:letter", "Tallet står på tavlen: 3 · 4 + 5"));

        var codeLock = classroom.AddObject(new Lock(
            new Position(40, 45),
            "builtin:code-lock",
            null,
            "Hvad giver regnestykket på tavlen?",
            AnswerType.Code,
            "17",
            hint: "Gang først, læg til bagefter."));

        var divisionLock = classroom.AddObject(new Lock(
            new Position(65, 50),
            "builtin:padlock",
            null,
            "Hvad er 10 / 3? Skriv svaret med to decimaler.",
            AnswerType.Text,
            "3,33",
            tolerance: 0.01m,
            hint: "10 delt med 3 er lidt over 3."));
        divisionLock.SetUnlockedBy(codeLock);

        classroom.AddObject(new Door(new Position(88, 55), "builtin:door", null, library.Id));

        library.AddObject(new Lock(
            new Position(35, 40),
            "builtin:padlock",
            null,
            "Hvilket af tallene er et primtal?",
            AnswerType.MultipleChoice,
            "17",
            hint: "Et primtal kan kun deles med 1 og sig selv.",
            options: ["9", "15", "17", "21"]));

        library.AddObject(new ExitDoor(new Position(85, 50), "builtin:exit-door", null));

        return room;
    }
}
