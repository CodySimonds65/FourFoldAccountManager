using System.Net;
using System.Net.Http;
using System.IO;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Leaderboard;

public sealed class LeaderboardCoordinatorTests
{
    [Fact]
    public async Task ReadsAnFfdbPageAndKeepsItAcrossRestarts()
    {
        using var temp = new TempDirectory();
        var handler = new RecordingHandler();
        await using (var coordinator = CreateCoordinator(temp.Path, handler))
        {
            var page = await coordinator.RefreshPageAsync(LeaderboardPeriod.Weekly, 2, 25, CancellationToken.None);
            Assert.NotNull(page);
            Assert.Equal(26, Assert.Single(page.Entries).Rank);
            Assert.Equal("pinkmilk", page.Entries[0].Username);
            Assert.Equal(41, page.TotalEntries);
        }
        Assert.Equal("https://ffdb.info/api/leaderboards?period=weekly&page=2",
            Assert.Single(handler.Requests).ToString());

        await using var restarted = CreateCoordinator(temp.Path, new RecordingHandler { Fail = true });
        var cached = await restarted.GetCachedPageAsync(LeaderboardPeriod.Weekly, 2, 25, CancellationToken.None);
        Assert.Equal("pinkmilk", Assert.Single(cached!.Entries).Username);
        Assert.Null(await restarted.RefreshPageAsync(LeaderboardPeriod.Weekly, 2, 25, CancellationToken.None,
            scheduleRetry: false));
    }

    [Fact]
    public async Task OldStateFileWithParticipationFieldsStillLoadsItsCache()
    {
        using var temp = new TempDirectory();
        var paths = new LocalDataPaths(temp.Path);
        await using (var coordinator = CreateCoordinator(temp.Path, new RecordingHandler()))
            await coordinator.RefreshPageAsync(LeaderboardPeriod.Weekly, 2, 25, CancellationToken.None);
        var json = await File.ReadAllTextAsync(paths.LeaderboardStateFilePath);
        await File.WriteAllTextAsync(paths.LeaderboardStateFilePath, json.Insert(1,
            "\"installationId\":\"3f2c1e9a-0000-4000-8000-000000000001\",\"pendingParticipation\":null,"));

        var state = await new LeaderboardClientStateStore(paths).LoadAsync();
        Assert.Single(state.CachedPages);
    }

    private static LeaderboardCoordinator CreateCoordinator(string path, RecordingHandler handler)
    {
        var client = new LeaderboardApiClient(new HttpClient(handler), LeaderboardApiOptions.FromEnvironment()!);
        return new LeaderboardCoordinator(new LeaderboardClientStateStore(new LocalDataPaths(path)), client);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            if (Fail) throw new HttpRequestException("offline");
            var (start, end) = LeaderboardPeriodWindow.GetCurrent(LeaderboardPeriod.Weekly, DateTimeOffset.UtcNow);
            // Shape copied from a live ffdb.info weekly page 2 response.
            var body = $$"""
                {"trackingGeneration":"fourfolddb-xp-2026-10-25","source":"fourfolddb","period":1,"periodStartUtc":"{{start:O}}","periodEndUtc":"{{end:O}}","pageSize":25,"totalEntries":41,"generatedAtUtc":"2026-11-04T19:03:58.900Z","trackingStartedAtUtc":"2026-10-25T22:31:55.062Z","lastCollectedAtUtc":"2026-11-04T19:03:58.805Z","initialPassComplete":true,"trackedPlayers":260,"page":2,"fetchedAtUtc":"2026-11-04T19:04:24.677Z","sourceUnavailable":false,"entries":[{"rank":26,"playerId":235,"username":"pinkmilk","xpGained":197839,"lastSampledAtUtc":"2026-11-04T19:03:58.805Z","isStale":false}]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fourfold-leaderboard-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
