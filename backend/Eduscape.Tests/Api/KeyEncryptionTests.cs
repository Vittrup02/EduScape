using System.Net;
using System.Net.Http.Json;
using Eduscape.Api.Auth;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eduscape.Tests.Api;

public sealed class KeyEncryptionTests : IDisposable
{
    private readonly TestCertificates _certificates = new();

    public void Dispose() => _certificates.Dispose();

    [Fact]
    public void Production_WithoutCertificate_FailsToStartWithClearMessage()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting(KeyEncryptionOptions.CertificatePathSetting, string.Empty));

        var exception = Assert.Throws<OptionsValidationException>(() => app.CreateClient());

        Assert.Contains("DataProtection__CertificatePath", exception.Message);
    }

    [Fact]
    public async Task Production_WithCertificate_Starts()
    {
        // No database here; the re-encryption job is tested on its own below.
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting(KeyEncryptionOptions.CertificatePathSetting, _certificates.NewRsaPem())
            .ConfigureTestServices(RemoveEncryptionJob));

        var response = await app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_WithoutCertificate_Starts()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting(KeyEncryptionOptions.CertificatePathSetting, string.Empty));

        var response = await app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void LoadCertificate_WithMissingFile_SaysSo()
    {
        var path = _certificates.MissingPath();

        var exception = Assert.Throws<InvalidOperationException>(() => KeyEncryption.LoadCertificate(path));

        Assert.Contains("does not exist", exception.Message);
        Assert.Contains(path, exception.Message);
    }

    [Fact]
    public void LoadCertificate_WithoutPrivateKey_AsksForTheKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => KeyEncryption.LoadCertificate(_certificates.NewPemWithoutPrivateKey()));

        Assert.Contains("BEGIN PRIVATE KEY", exception.Message);
    }

    [Fact]
    public void LoadCertificate_WithEcKey_AsksForRsa()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => KeyEncryption.LoadCertificate(_certificates.NewEcPem()));

        Assert.Contains("RSA", exception.Message);
    }

    [Fact]
    public void LoadCertificate_ReadsCertificateAndPrivateKey()
    {
        using var certificate = KeyEncryption.LoadCertificate(_certificates.NewRsaPem());

        Assert.True(certificate.HasPrivateKey);
    }

    [PostgresFact]
    public async Task Keys_AreEncryptedInTheDatabase_AndTeachersStaySignedInAfterRestart()
    {
        await using var api = await TestApi.StartAsync();
        var certificate = _certificates.NewRsaPem();

        SignedInTeacher teacher;
        using (var beforeRestart = api.WithCertificate(certificate))
        {
            teacher = await api.Factory.RegisterTeacherAsync(beforeRestart);
        }

        // A new host has an empty in-memory key ring and must read (and decrypt) the keys from the database.
        using var afterRestart = api.WithCertificate(certificate);
        await AssertSignedInAsync(api.Factory, afterRestart, teacher);
        await AssertAllKeysEncryptedAsync(api.Factory);
    }

    [PostgresFact]
    public async Task AfterRotation_KeysOfThePreviousCertificateCanStillBeRead()
    {
        await using var api = await TestApi.StartAsync();
        var oldCertificate = _certificates.NewRsaPem();
        var newCertificate = _certificates.NewRsaPem();

        SignedInTeacher teacher;
        using (var beforeRotation = api.WithCertificate(oldCertificate))
        {
            teacher = await api.Factory.RegisterTeacherAsync(beforeRotation);
        }

        using var afterRotation = api.WithCertificate(newCertificate, previous: oldCertificate);
        await AssertSignedInAsync(api.Factory, afterRotation, teacher);
    }

    [PostgresFact]
    public async Task UnencryptedKeys_AreEncrypted_WithoutSigningTeachersOut()
    {
        await using var api = await TestApi.StartAsync();
        var certificate = _certificates.NewRsaPem();

        // Before this feature: Development without a certificate stores the key unencrypted.
        SignedInTeacher teacher;
        using (var unencrypted = api.Factory.WithWebHostBuilder(_ => { }))
        {
            teacher = await api.Factory.RegisterTeacherAsync(unencrypted);
        }

        await using var db = api.Factory.Database.CreateContext();
        Assert.Contains(await db.DataProtectionKeys.Select(k => k.Xml).ToListAsync(), xml => xml!.Contains("requiresEncryption"));

        using var encrypted = api.WithCertificate(certificate);
        await using (var scope = encrypted.Services.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<PlaintextKeyEncryption>();
            Assert.Equal(1, await job.RunOnceAsync(CancellationToken.None));
            Assert.Equal(0, await job.RunOnceAsync(CancellationToken.None));
        }

        await AssertAllKeysEncryptedAsync(api.Factory);

        // The same key, now encrypted, still unprotects the teacher's tokens in a fresh host.
        using var afterRestart = api.WithCertificate(certificate);
        await AssertSignedInAsync(api.Factory, afterRestart, teacher);
    }

    private static async Task AssertSignedInAsync(EduscapeApiFactory factory, WebApplicationFactory<Program> app, SignedInTeacher teacher)
    {
        var rooms = await factory.ClientWithToken(teacher.Tokens.AccessToken, app).GetAsync("/api/teacher/rooms");
        Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);

        var refresh = await app.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = teacher.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    private static void RemoveEncryptionJob(IServiceCollection services) =>
        services.Remove(services.Single(s => s.ImplementationType == typeof(PlaintextKeyEncryptionService)));

    private static async Task AssertAllKeysEncryptedAsync(EduscapeApiFactory factory)
    {
        await using var db = factory.Database.CreateContext();
        var keys = await db.DataProtectionKeys.Select(k => k.Xml).ToListAsync();

        Assert.NotEmpty(keys);
        Assert.All(keys, xml =>
        {
            Assert.Contains("encryptedSecret", xml);
            Assert.DoesNotContain("requiresEncryption", xml);
            Assert.DoesNotContain("unencrypted", xml);
        });
    }

    /// <summary>An API with its own database, so each test starts with an empty key ring.</summary>
    private sealed class TestApi : IAsyncDisposable
    {
        public EduscapeApiFactory Factory { get; } = new();

        public static async Task<TestApi> StartAsync()
        {
            var api = new TestApi();
            await api.Factory.InitializeAsync();
            return api;
        }

        /// <summary>A new host (a "restart": its own in-memory key ring) on the same database. Tests run the job themselves.</summary>
        public WebApplicationFactory<Program> WithCertificate(string path, string? previous = null) =>
            Factory.WithWebHostBuilder(builder => builder
                .UseSetting(KeyEncryptionOptions.CertificatePathSetting, path)
                .UseSetting(KeyEncryptionOptions.PreviousCertificatePathsSetting, previous ?? string.Empty)
                .ConfigureTestServices(RemoveEncryptionJob));

        public ValueTask DisposeAsync() => new(((IAsyncLifetime)Factory).DisposeAsync());
    }
}
