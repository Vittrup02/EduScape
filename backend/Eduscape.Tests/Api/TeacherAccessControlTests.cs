using System.Net;
using System.Net.Http.Json;
using Eduscape.Api.Errors;
using Eduscape.Api.Teachers;
using Eduscape.Infrastructure.Persistence.Seeding;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Eduscape.Tests.Api;

public class TeacherAccessControlTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private sealed record RoomJson(Guid Id, string Title, string Subject, string GradeLevel);

    [Fact]
    public async Task Rooms_WithoutToken_ReturnsUnauthorizedCode()
    {
        var response = await factory.CreateClient().GetAsync("/api/teacher/rooms");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, ApiErrors.Unauthorized);
    }

    [Fact]
    public async Task Rooms_WithInvalidToken_ReturnsUnauthorizedCode()
    {
        var response = await factory.ClientWithToken("forged-token").GetAsync("/api/teacher/rooms");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, ApiErrors.Unauthorized);
    }

    [Fact]
    public void EveryTeacherEndpoint_RequiresAuthorization()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(TeacherEndpoints.RoutePrefix, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>()));
    }

    [PostgresFact]
    public async Task Rooms_ListsOnlyTheTeachersOwnRooms()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var aliceSecond = await factory.AddRoomAsync(alice.Id, "B: Brøker");
        var aliceFirst = await factory.AddRoomAsync(alice.Id, "A: Algebra");
        await factory.AddRoomAsync(bob.Id, "Bobs rum");

        var rooms = await alice.Client.GetFromJsonAsync<RoomJson[]>("/api/teacher/rooms") ?? [];

        // Sorted by title; no rooms of other teachers and no templates such as the seeded demo room.
        Assert.Equal([aliceFirst.Id, aliceSecond.Id], rooms.Select(r => r.Id));
        Assert.DoesNotContain(rooms, r => r.Id == DemoRoomSeeder.DemoRoomId);

        // Fixed lists are sent as keys for the frontend to translate.
        Assert.Equal("Mathematics", rooms[0].Subject);
        Assert.Equal("Grade7", rooms[0].GradeLevel);
    }

    [PostgresFact]
    public async Task Room_OfAnotherTeacher_IsNotFound()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var bobsRoom = await factory.AddRoomAsync(bob.Id, "Bobs rum");

        var asAlice = await alice.Client.GetAsync($"/api/teacher/rooms/{bobsRoom.Id}");
        var asBob = await bob.Client.GetFromJsonAsync<RoomJson>($"/api/teacher/rooms/{bobsRoom.Id}");

        await TeacherAuthTests.AssertErrorAsync(asAlice, HttpStatusCode.NotFound, ApiErrors.NotFound);
        Assert.Equal("Bobs rum", asBob?.Title);
    }

    [PostgresFact]
    public async Task Template_IsNotFoundAsATeachersRoom()
    {
        var alice = await factory.RegisterTeacherAsync();

        var response = await alice.Client.GetAsync($"/api/teacher/rooms/{DemoRoomSeeder.DemoRoomId}");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.NotFound);
    }
}
