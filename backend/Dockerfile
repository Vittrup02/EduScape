# syntax=docker/dockerfile:1
# Build context: /backend (Render: Root Directory = backend)

# --- Build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so the layer is cached until dependencies change
COPY Eduscape.Api/Eduscape.Api.csproj Eduscape.Api/
COPY Eduscape.Domain/Eduscape.Domain.csproj Eduscape.Domain/
COPY Eduscape.Infrastructure/Eduscape.Infrastructure.csproj Eduscape.Infrastructure/
RUN dotnet restore Eduscape.Api/Eduscape.Api.csproj

COPY Eduscape.Api/ Eduscape.Api/
COPY Eduscape.Domain/ Eduscape.Domain/
COPY Eduscape.Infrastructure/ Eduscape.Infrastructure/
RUN dotnet publish Eduscape.Api/Eduscape.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# --- Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render overrides PORT; the app listens on 0.0.0.0:$PORT (see Hosting/ListenUrl.cs).
# ASPNETCORE_ENVIRONMENT is left unset, so it defaults to Production (no demo seeding, no user-secrets).
# EF migrations are never run here; the CI job `migrate` applies them (see README "Migrationer mod Neon").
ENV PORT=8080
EXPOSE 8080

# Non-root user that ships with the .NET images
USER $APP_UID

ENTRYPOINT ["dotnet", "Eduscape.Api.dll"]
