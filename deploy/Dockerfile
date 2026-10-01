# syntax=docker/dockerfile:1
# BoardEmpire match server. Build from the repository root:
#   podman build -f deploy/Dockerfile -t boardempire-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Restore first so the NuGet layer is reused until a project file changes.
COPY src/Game.Core/Game.Core.csproj src/Game.Core/
COPY src/Game.Protocol/Game.Protocol.csproj src/Game.Protocol/
COPY src/Game.Net/Game.Net.csproj src/Game.Net/
COPY src/Game.Server/Game.Server.csproj src/Game.Server/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Game.Server/Game.Server.csproj

COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Game.Server/Game.Server.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0
EXPOSE 8080
# The aspnet image ships an unprivileged "app" user.
USER app
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
    CMD wget -qO- http://127.0.0.1:8080/healthz >/dev/null || exit 1
ENTRYPOINT ["dotnet", "Game.Server.dll"]
