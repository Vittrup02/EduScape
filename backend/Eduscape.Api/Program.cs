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

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseCors();

// Used by the frontend as a wake-up call (Render free tier sleeps when idle).
app.MapHealthChecks("/health");

app.Run();
