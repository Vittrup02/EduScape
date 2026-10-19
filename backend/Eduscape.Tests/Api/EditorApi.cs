using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eduscape.Api.Editor;
using Eduscape.Api.Errors;
using Eduscape.Api.Teachers;
using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Api;

/// <summary>Calls to the editor endpoints, and documents built like the frontend builds them.</summary>
internal static class EditorApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<RoomSummaryDto> CreateRoomAsync(HttpClient teacher, string title = "Brøker")
    {
        var response = await teacher.PostAsJsonAsync(
            "/api/teacher/rooms", new CreateRoomRequest(title, Subject.Mathematics, GradeLevel.Grade6, "Scene 1"), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<RoomSummaryDto>(response);
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient teacher, Guid roomId) =>
        teacher.GetAsync($"/api/teacher/rooms/{roomId}/editor");

    public static async Task<EditorRoomDto> GetRoomAsync(HttpClient teacher, Guid roomId) =>
        await ReadAsync<EditorRoomDto>(await GetAsync(teacher, roomId));

    public static Task<HttpResponseMessage> SaveAsync(HttpClient teacher, Guid roomId, SaveRoomRequest request) =>
        teacher.PutAsJsonAsync($"/api/teacher/rooms/{roomId}/editor", request, Json);

    public static async Task<SaveRoomResponse> SavedAsync(HttpClient teacher, Guid roomId, SaveRoomRequest request) =>
        await ReadAsync<SaveRoomResponse>(await SaveAsync(teacher, roomId, request));

    public static Task<HttpResponseMessage> CopyTemplateAsync(HttpClient teacher, Guid templateId) =>
        teacher.PostAsync($"/api/teacher/templates/{templateId}/copy", content: null);

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient teacher, Guid roomId) =>
        teacher.PostAsync($"/api/teacher/rooms/{roomId}/preview", content: null);

    /// <summary>The loaded room as a save request, unchanged.</summary>
    public static SaveRoomRequest ToRequest(EditorRoomDto room) => new(
        room.Revision,
        room.Title,
        room.Subject,
        room.GradeLevel,
        room.StartSceneId!.Value,
        [.. room.Scenes.Select(s => new SaveSceneRequest(s.Id, s.Name, s.BackgroundImageKey, s.Objects))]);

    public static EditorObjectDto NewLock(double x = 30, double y = 40, string question = "2 + 2?", string answer = "4") =>
        new(Guid.CreateVersion7(), RoomObjectType.Lock, x, y, "builtin:padlock", null,
            Question: question, AnswerType: AnswerType.Code, CorrectAnswer: answer);

    public static EditorObjectDto NewDoor(Guid? targetSceneId, Guid? unlockedById = null) =>
        new(Guid.CreateVersion7(), RoomObjectType.Door, 80, 50, "builtin:door", unlockedById, TargetSceneId: targetSceneId);

    public static EditorObjectDto NewLetter(string text = "Læs side 42") =>
        new(Guid.CreateVersion7(), RoomObjectType.Letter, 10, 80, "builtin:letter", null, Text: text);

    public static EditorObjectDto NewExitDoor() =>
        new(Guid.CreateVersion7(), RoomObjectType.ExitDoor, 50, 50, "builtin:exit-door", null);

    public static SaveSceneRequest NewScene(string name, params EditorObjectDto[] objects) =>
        new(Guid.CreateVersion7(), name, "builtin:library", objects);

    public static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(Json);
        Assert.Equal(code, error?.Code);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<T>(Json)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} in the response.");
    }
}
