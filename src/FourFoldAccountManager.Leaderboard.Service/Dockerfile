FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj src/FourFoldAccountManager.Core/
COPY src/FourFoldAccountManager.Leaderboard.Service/FourFoldAccountManager.Leaderboard.Service.csproj src/FourFoldAccountManager.Leaderboard.Service/
RUN dotnet restore src/FourFoldAccountManager.Leaderboard.Service/FourFoldAccountManager.Leaderboard.Service.csproj

COPY src/FourFoldAccountManager.Core/ src/FourFoldAccountManager.Core/
COPY src/FourFoldAccountManager.Leaderboard.Service/ src/FourFoldAccountManager.Leaderboard.Service/
RUN dotnet publish src/FourFoldAccountManager.Leaderboard.Service/FourFoldAccountManager.Leaderboard.Service.csproj \
    -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
CMD ["sh", "-c", "exec dotnet FourFoldAccountManager.Leaderboard.Service.dll --urls http://0.0.0.0:${PORT:-10000}"]
