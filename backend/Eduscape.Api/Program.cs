using System.Text.Json.Serialization;
using Eduscape.Api.Auth;
using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Api.Play;
using Eduscape.Api.Sessions;
using Eduscape.Api.Teachers;
using Eduscape.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Render (and the Docker image) provide the port in PORT; locally launchSettings.json applies.
if (ListenUrl.FromPort(builder.Configuration["PORT"]) is { } listenUrl)
{
    builder.WebHost.UseUrls(listenUrl);
}

// Allowed frontend origins come from configuration (appsettings / environment variables),
// so local development and production can use different values without code changes.
var allowedOrigins = CorsOrigins.FromConfiguration(builder.Configuration);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddHealthChecks();
builder.Services.AddExceptionHandler<ErrorCodeExceptionHandler>();

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddTeacherAuth();
builder.Services.AddSessions(builder.Configuration);
builder.Services.AddPlay();
builder.Services.AddEduscapeRateLimiting(builder.Configuration);

// Fixed lists (subject, grade level, ...) are sent as keys that the frontend translates.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Render's proxy appends the client address to X-Forwarded-For. Only the last entry (ForwardLimit = 1) is trusted,
// so a client cannot spoof its IP for rate limiting by sending the header itself. IPs are never logged.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// CORS first, so error responses also carry CORS headers and the frontend can read their code.
app.UseCors();

// Error responses carry only a code ({ "code": "NOT_FOUND" }); the frontend translates it.
// The empty branch is never reached: ErrorCodeExceptionHandler handles every exception.
app.UseExceptionHandler(_ => { });
app.UseStatusCodePages(context =>
{
    var response = context.HttpContext.Response;
    return response.WriteAsJsonAsync(new ApiError(ApiErrors.FromStatusCode(response.StatusCode)));
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Used by the frontend as a wake-up call (Render free tier sleeps when idle).
app.MapHealthChecks("/health");

app.MapAuthEndpoints();
app.MapTeacherEndpoints();
app.MapTeacherSessionEndpoints();
app.MapPlayEndpoints();
app.MapHub<PlayHub>(PlayHub.Path);

app.Run();
