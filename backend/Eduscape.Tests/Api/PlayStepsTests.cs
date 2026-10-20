using Eduscape.Api.Play;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Rooms;
using Eduscape.Tests.Infrastructure;

namespace Eduscape.Tests.Api;

/// <summary>Locks with several steps, and order steps, played over the API by a team.</summary>
public class PlayStepsTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private static readonly Position Center = new(50, 50);

    private HttpClient Student => factory.CreateClient();

    [PostgresFact]
    public async Task Steps_AreAnsweredInOrder_AndTheWholeTeamFollows()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session);
        var bo = await JoinAsync(session);

        // Step 1 forgives a small spelling mistake.
        var first = await SessionApi.AnsweredAsync(Student, ida, room.Reaction.Id, "Natriun", step: 0);
        Assert.Equal((AnswerOutcome.Correct, 1, false), (first.Outcome, first.SolvedSteps, first.LockSolved));
        Assert.Equal(new LockProgressDto(room.Reaction.Id, 1, null, 0), Assert.Single((await StateAsync(bo)).Team.Locks));

        // Bo answers the step Ida has already done: nothing changes, and Bo's device catches up.
        var late = await SessionApi.AnsweredAsync(Student, bo, room.Reaction.Id, "Natrium", step: 0);
        Assert.Equal((AnswerOutcome.AlreadySolved, 1), (late.Outcome, late.SolvedSteps));

        Assert.Equal(AnswerOutcome.Correct, (await SessionApi.AnsweredAsync(Student, bo, room.Reaction.Id, "NaCl", step: 1)).Outcome);

        var order = await SessionApi.OrderedAsync(Student, ida, room.Reaction.Id, room.CorrectOrder, step: 2);
        Assert.Equal((AnswerOutcome.Correct, 3, true), (order.Outcome, order.SolvedSteps, order.LockSolved));
        Assert.Contains(room.ExitDoor.Id, order.OpenedDoorIds);

        var state = await StateAsync(bo);
        Assert.Equal([room.Reaction.Id], state.Team.SolvedLockIds);
        Assert.Empty(state.Team.Locks);
    }

    [PostgresFact]
    public async Task OrderStep_WrongOrder_IsWrong()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session);
        await SessionApi.AnsweredAsync(Student, ida, room.Reaction.Id, "Natrium", step: 0);
        await SessionApi.AnsweredAsync(Student, ida, room.Reaction.Id, "NaCl", step: 1);

        var wrong = await SessionApi.OrderedAsync(Student, ida, room.Reaction.Id, [.. room.CorrectOrder.Reverse()], step: 2);

        Assert.Equal((AnswerOutcome.Wrong, 2), (wrong.Outcome, wrong.SolvedSteps));
    }

    [PostgresFact]
    public async Task OrderItems_AreShuffledTheSameWayForTheTeam_AndNeverInTheCorrectOrder()
    {
        var (room, session) = await StartAsync(teamCount: 2);
        var ida = await JoinAsync(session);
        var bo = await JoinAsync(session);
        var other = await JoinAsync(session, team: 1);

        PlayOrderItemDto[] ItemsOf(PlayStateDto state) =>
            state.Room.Scenes.SelectMany(s => s.Objects).Single(o => o.Id == room.Reaction.Id).Steps![2].OrderItems!;

        var idaItems = ItemsOf(await StateAsync(ida)).Select(i => i.Id).ToArray();
        Assert.Equal(room.CorrectOrder.Order(), idaItems.Order());
        Assert.NotEqual(room.CorrectOrder, idaItems);
        Assert.Equal(idaItems, ItemsOf(await StateAsync(bo)).Select(i => i.Id));
        Assert.NotEqual(room.CorrectOrder, ItemsOf(await StateAsync(other)).Select(i => i.Id));
    }

    [Fact]
    public void Shuffle_IsNeverTheCorrectOrder_ForAnyTeam()
    {
        var step = new LockStep(
            "?", AnswerType.Order, "", orderItems: [new(Guid.NewGuid(), "A"), new(Guid.NewGuid(), "B")]);

        for (var i = 0; i < 200; i++)
        {
            Assert.NotEqual(step.OrderItems.Select(o => o.Id), PlayRoomDto.Shuffle(step, Guid.NewGuid()).Select(o => o.Id));
        }
    }

    private async Task<PlayStateDto> StateAsync(string deviceToken) =>
        await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(Student, deviceToken));

    private async Task<string> JoinAsync(SessionDetailsDto session, int team = 0) =>
        (await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[team].Id, null)).DeviceToken;

    private async Task<(StepRoom Room, SessionDetailsDto Session)> StartAsync(int teamCount = 2)
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = new StepRoom(teacher.Id);
        await using (var db = factory.Database.CreateContext())
        {
            db.EscapeRooms.Add(room.Room);
            await db.SaveChangesAsync();
        }

        return (room, await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id, teamCount: teamCount));
    }

    /// <summary>One scene with a three-step lock (a name with typos allowed, a formula, an order) and the exit door.</summary>
    private sealed class StepRoom
    {
        public StepRoom(Guid teacherId)
        {
            Room = new EscapeRoom("Kemi", Subject.Chemistry, GradeLevel.Stx2, teacherId);
            Room.SetWrongAnswerCooldown(0);
            var lab = Room.AddScene("Laboratoriet", "builtin:laboratory");
            OrderItem[] items = [new(Guid.NewGuid(), "Afvej saltet"), new(Guid.NewGuid(), "Opløs i vand"), new(Guid.NewGuid(), "Filtrér")];
            CorrectOrder = [.. items.Select(i => i.Id)];
            Reaction = lab.AddObject(new Lock(Center, "builtin:chest", null,
            [
                new LockStep("Hvad hedder Na?", AnswerType.Text, "Natrium", acceptTypos: true, hint: "Det er et metal."),
                new LockStep("Køkkensalt?", AnswerType.Text, "NaCl"),
                new LockStep("Sæt trinene i rækkefølge", AnswerType.Order, "", orderItems: items),
            ]));
            ExitDoor = lab.AddObject(new ExitDoor(Center, "builtin:exit-door", null));
        }

        public EscapeRoom Room { get; }

        public Lock Reaction { get; }

        public ExitDoor ExitDoor { get; }

        public Guid[] CorrectOrder { get; }
    }
}
