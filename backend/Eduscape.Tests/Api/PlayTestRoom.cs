using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Api;

/// <summary>
/// A small room with answers and hints that appear nowhere else, so tests can check they never reach the client.
/// Scene 1: code lock (with hint), text lock with tolerance (after the code lock), door to scene 2 (opens once both
/// are solved). Scene 2: the exit door, open as soon as the team gets there. Answers can be retried at once unless a
/// test asks for a wait after wrong answers.
/// </summary>
internal sealed class PlayTestRoom
{
    public const string CodeAnswer = "XQZ7";
    public const string Hint = "secret-hint-6e1f";
    public const string TextAnswer = "3,1416";
    public const decimal Tolerance = 0.0001m;

    public PlayTestRoom(Guid ownerTeacherId, int cooldownSeconds = 0)
    {
        Room = new EscapeRoom("Play test room", Subject.Mathematics, GradeLevel.Grade7, ownerTeacherId);
        Room.SetWrongAnswerCooldown(cooldownSeconds);
        var first = Room.AddScene("First", "builtin:classroom");
        var second = Room.AddScene("Second", "builtin:library");

        CodeLock = first.AddObject(new Lock(
            new Position(20, 50), "builtin:code-lock", null, "Which code?", AnswerType.Code, CodeAnswer, hint: Hint));
        TextLock = first.AddObject(new Lock(
            new Position(50, 50), "builtin:padlock", null, "What is pi?", AnswerType.Text, TextAnswer, tolerance: Tolerance));
        TextLock.SetUnlockedBy(CodeLock);
        Door = first.AddObject(new Door(new Position(80, 50), "builtin:door", null, second.Id));
        ExitDoor = second.AddObject(new ExitDoor(new Position(80, 50), "builtin:exit-door", null));
    }

    public EscapeRoom Room { get; }

    public Lock CodeLock { get; }

    public Lock TextLock { get; }

    public Door Door { get; }

    public ExitDoor ExitDoor { get; }

    public static async Task<PlayTestRoom> AddAsync(EduscapeApiFactory factory, Guid ownerTeacherId, int cooldownSeconds = 0)
    {
        var testRoom = new PlayTestRoom(ownerTeacherId, cooldownSeconds);
        await using var db = factory.Database.CreateContext();
        db.EscapeRooms.Add(testRoom.Room);
        await db.SaveChangesAsync();
        return testRoom;
    }
}
