using Eduscape.Api.Errors;
using Eduscape.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Allowed frontend origins come from configuration (appsettings / environment variables),
// so local development and production can use different values without code changes.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddHealthChecks();
builder.Services.AddExceptionHandler<ErrorCodeExceptionHandler>();

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

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

// Used by the frontend as a wake-up call (Render free tier sleeps when idle).
app.MapHealthChecks("/health");

app.Run();
