# Build from the repo root:  docker build -f src/LemonadeWars.Server/Dockerfile .
# Railway: set the service's Dockerfile path to src/LemonadeWars.Server/Dockerfile.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish src/LemonadeWars.Server -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
COPY --from=build /repo/game-data ./game-data
ENV GAME_DATA_DIR=/app/game-data
# Railway injects PORT; ASP.NET binds to it.
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
EXPOSE 8080
ENTRYPOINT ["dotnet", "LemonadeWars.Server.dll"]
