using System.Net;
using System.Net.Http.Json;
using Eduscape.Api.Errors;
using Eduscape.Api.Uploads;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Images;
using Eduscape.Infrastructure.Storage;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Eduscape.Tests.Api.EditorApi;

namespace Eduscape.Tests.Api;

public class UploadTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private readonly FakeImageStorage _storage = new();

    [PostgresFact]
    public async Task Upload_WithoutABucket_IsUnavailable()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await StartAsync(teacher.Client, "image/jpeg", 1000);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.UploadsUnavailable);
    }

    [PostgresFact]
    public async Task Start_ChecksTypeAndSize()
    {
        var app = WithFakeStorage();
        var teacher = await factory.RegisterTeacherAsync(app);

        await AssertErrorAsync(
            await StartAsync(teacher.Client, "image/svg+xml", 1000), HttpStatusCode.BadRequest, ApiErrors.ImageTypeNotAllowed);
        await AssertErrorAsync(
            await StartAsync(teacher.Client, "image/png", ImageProcessor.MaxUploadBytes + 1), HttpStatusCode.BadRequest, ApiErrors.ImageTooLarge);
        await AssertErrorAsync(await StartAsync(teacher.Client, "image/png", 0), HttpStatusCode.BadRequest, ApiErrors.ImageTooLarge);
    }

    [PostgresFact]
    public async Task Upload_StoresACleanCopyUnderAGeneratedName_AndTheRoomCanUseIt()
    {
        var app = WithFakeStorage();
        var teacher = await factory.RegisterTeacherAsync(app);
        var started = await ReadAsync<StartUploadResponse>(await StartAsync(teacher.Client, "image/jpeg", 5000));
        var incoming = UploadEndpoints.IncomingKey(teacher.Id, started.UploadId);
        Assert.Contains(incoming, started.Url);

        // What the browser does with the presigned URL.
        _storage.Objects[incoming] = (TestImages.JpegWithExif(400, 200, orientation: 1), "image/jpeg");
        var completed = await ReadAsync<CompleteUploadResponse>(await CompleteAsync(teacher.Client, started.UploadId, "image/jpeg"));

        Assert.True(ImageKeys.IsUpload(completed.ImageKey));
        Assert.False(_storage.Objects.ContainsKey(incoming));
        var (stored, contentType) = _storage.Objects[completed.ImageKey];
        Assert.Equal("image/webp", contentType);
        Assert.False(TestImages.Contains(stored, TestImages.ExifSecret));
        Assert.Equal($"https://bucket.example/{completed.ImageKey}?get", completed.ImageUrl);

        // The editor saves the key and gets a link to show the background.
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        Assert.True(room.UploadsEnabled);
        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with
        {
            Scenes = [ToRequest(room).Scenes[0] with { BackgroundImageKey = completed.ImageKey }],
        });
        var scene = Assert.Single((await GetRoomAsync(teacher.Client, room.Id)).Scenes);
        Assert.Equal(completed.ImageUrl, scene.BackgroundImageUrl);
    }

    [PostgresFact]
    public async Task Complete_OfAFileThatIsNotWhatItClaims_IsRejectedAndDeleted()
    {
        var app = WithFakeStorage();
        var teacher = await factory.RegisterTeacherAsync(app);
        var started = await ReadAsync<StartUploadResponse>(await StartAsync(teacher.Client, "image/jpeg", 5000));
        var incoming = UploadEndpoints.IncomingKey(teacher.Id, started.UploadId);
        _storage.Objects[incoming] = (TestImages.Png(20, 20), "image/jpeg");

        var response = await CompleteAsync(teacher.Client, started.UploadId, "image/jpeg");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.ImageTypeNotAllowed);
        Assert.Empty(_storage.Objects);
    }

    [PostgresFact]
    public async Task Complete_OfATooLargeFile_IsRejectedAndDeleted()
    {
        var app = WithFakeStorage();
        var teacher = await factory.RegisterTeacherAsync(app);
        var started = await ReadAsync<StartUploadResponse>(await StartAsync(teacher.Client, "image/png", 5000));
        var incoming = UploadEndpoints.IncomingKey(teacher.Id, started.UploadId);
        _storage.Objects[incoming] = (new byte[ImageProcessor.MaxUploadBytes + 1], "image/png");

        var response = await CompleteAsync(teacher.Client, started.UploadId, "image/png");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.ImageTooLarge);
        Assert.Empty(_storage.Objects);
    }

    [PostgresFact]
    public async Task Complete_OfAnotherTeachersUpload_IsNotFound()
    {
        var app = WithFakeStorage();
        var alice = await factory.RegisterTeacherAsync(app);
        var bob = await factory.RegisterTeacherAsync(app);
        var started = await ReadAsync<StartUploadResponse>(await StartAsync(alice.Client, "image/jpeg", 5000));
        var incoming = UploadEndpoints.IncomingKey(alice.Id, started.UploadId);
        _storage.Objects[incoming] = (TestImages.Jpeg(20, 20), "image/jpeg");

        var response = await CompleteAsync(bob.Client, started.UploadId, "image/jpeg");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.UploadNotFound);
        Assert.True(_storage.Objects.ContainsKey(incoming));
    }

    private WebApplicationFactory<Program> WithFakeStorage() =>
        factory.WithServices(services =>
        {
            services.RemoveAll<IImageStorage>();
            services.AddSingleton<IImageStorage>(_storage);
        });

    private static Task<HttpResponseMessage> StartAsync(HttpClient teacher, string contentType, long size) =>
        teacher.PostAsJsonAsync("/api/teacher/uploads", new StartUploadRequest(contentType, size));

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient teacher, Guid uploadId, string contentType) =>
        teacher.PostAsJsonAsync($"/api/teacher/uploads/{uploadId}/complete", new CompleteUploadRequest(contentType));
}
