using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eduscape.Api.Play;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Api;

/// <summary>Calls to the session and play endpoints, reading responses into the API's own DTOs.</summary>
internal static class SessionApi
{
    // Enums travel as keys ("Team", "Red"), like in the API.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<HttpResponseMessage> StartAsync(
        HttpClient teacher, Guid roomId, string? className = "9.A", SessionMode mode = SessionMode.Team, int teamCount = 3) =>
        teacher.PostAsJsonAsync("/api/teacher/sessions", new StartSessionRequest(roomId, className, mode, teamCount), Json);

    public static async Task<SessionDetailsDto> StartSessionAsync(
        HttpClient teacher, Guid roomId, string? className = "9.A", SessionMode mode = SessionMode.Team, int teamCount = 3)
    {
        var response = await StartAsync(teacher, roomId, className, mode, teamCount);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<SessionDetailsDto>(response);
    }

    public static async Task<SessionDetailsDto> GetSessionAsync(HttpClient teacher, Guid sessionId) =>
        await ReadAsync<SessionDetailsDto>(await teacher.GetAsync($"/api/teacher/sessions/{sessionId}"));

    public static async Task<SessionSummaryDto[]> GetActiveSessionsAsync(HttpClient teacher) =>
        await ReadAsync<SessionSummaryDto[]>(await teacher.GetAsync("/api/teacher/sessions"));

    public static Task<HttpResponseMessage> PostAsync(HttpClient teacher, Guid sessionId, string action) =>
        teacher.PostAsync($"/api/teacher/sessions/{sessionId}/{action}", content: null);

    public static Task<HttpResponseMessage> LookupAsync(HttpClient student, string? pin) =>
        student.PostAsJsonAsync("/api/play/lookup", new LookupRequest(pin), Json);

    public static Task<HttpResponseMessage> JoinAsync(HttpClient student, string? pin, Guid? teamId, string? nickname) =>
        student.PostAsJsonAsync("/api/play/join", new JoinRequest(pin, teamId, nickname), Json);

    public static async Task<JoinResponse> JoinedAsync(HttpClient student, string pin, Guid? teamId, string? nickname)
    {
        var response = await JoinAsync(student, pin, teamId, nickname);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JoinResponse>(response);
    }

    public static Task<HttpResponseMessage> ReconnectAsync(HttpClient student, string? deviceToken) =>
        student.PostAsJsonAsync("/api/play/reconnect", new ReconnectRequest(deviceToken), Json);

    public static Task<HttpResponseMessage> AnswerAsync(HttpClient student, string? deviceToken, Guid lockId, string? answer) =>
        student.PostAsJsonAsync("/api/play/answer", new AnswerRequest(deviceToken, lockId, answer), Json);

    public static async Task<AnswerResponse> AnsweredAsync(HttpClient student, string deviceToken, Guid lockId, string answer) =>
        await ReadAsync<AnswerResponse>(await AnswerAsync(student, deviceToken, lockId, answer));

    public static Task<HttpResponseMessage> OpenDoorAsync(HttpClient student, string? deviceToken, Guid doorId) =>
        student.PostAsJsonAsync("/api/play/doors/open", new OpenDoorRequest(deviceToken, doorId), Json);

    public static Task<HttpResponseMessage> OpenExitDoorAsync(HttpClient student, string? deviceToken, Guid exitDoorId) =>
        student.PostAsJsonAsync("/api/play/exit", new OpenExitDoorRequest(deviceToken, exitDoorId), Json);

    public static Task<HttpResponseMessage> EnterSceneAsync(HttpClient student, string? deviceToken, Guid sceneId) =>
        student.PostAsJsonAsync("/api/play/scene", new EnterSceneRequest(deviceToken, sceneId), Json);

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<T>(Json)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} in the response.");
    }
}

/// <summary>Hands out the given PINs first, then random ones, to provoke collisions.</summary>
internal sealed class QueuedPinGenerator(params string[] pins) : IPinGenerator
{
    private readonly Queue<string> _pins = new(pins);
    private readonly RandomPinGenerator _random = new();

    public string Next() => _pins.TryDequeue(out var pin) ? pin : _random.Next();
}
