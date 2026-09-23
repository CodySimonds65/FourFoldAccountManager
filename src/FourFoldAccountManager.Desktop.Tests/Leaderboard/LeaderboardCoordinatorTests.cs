using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Leaderboard;

public sealed class LeaderboardCoordinatorTests
{
    [Fact]
    public async Task OldSettingsDefaultToPrivateAndOptInSurvivesRestart()
    {
        using var temp = new TempDirectory();
        var paths = new LocalDataPaths(temp.Path);
        var settings = new SettingsStore(paths);
        await settings.SaveAsync(await settings.LoadAsync());
        var oldJson = JsonNode.Parse(await File.ReadAllTextAsync(paths.SettingsFilePath))!.AsObject();
        oldJson.Remove("shareLinkedAccounts");
        await File.WriteAllTextAsync(paths.SettingsFilePath, oldJson.ToJsonString());
        Assert.False((await settings.LoadAsync()).ShareLinkedAccounts);
        await settings.SaveAsync((await settings.LoadAsync()) with { ShareLinkedAccounts = true });
        Assert.True((await settings.LoadAsync()).ShareLinkedAccounts);
    }

    [Fact]
    public async Task InstallationIdPersistsAndWireRequestContainsOnlyPublicIdentity()
    {
        using var temp = new TempDirectory();
        var handler = new RecordingHandler();
        await using var first = CreateCoordinator(temp.Path, handler, sharing: true);
        await first.InitializeAsync();
        await first.SyncParticipationAsync([new LeaderboardProfile(42, "Player")], [42], CancellationToken.None);
        await first.FlushPendingAsync();
        var firstRequest = handler.Bodies.Single();
        using var json = JsonDocument.Parse(firstRequest);
        var id = json.RootElement.GetProperty("installationId").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(42, json.RootElement.GetProperty("activePlayerIds")[0].GetInt32());
        Assert.DoesNotContain("accountId", firstRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("label", firstRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", firstRequest, StringComparison.OrdinalIgnoreCase);
        await using var second = CreateCoordinator(temp.Path, new RecordingHandler(), sharing: true);
        await second.InitializeAsync();
        Assert.Equal(id, second.InstallationId);
    }

    [Fact]
    public async Task OptOutPersistsLocallyBeforeRequestAndRetriesEmptyHeartbeat()
    {
        using var temp = new TempDirectory();
        var settings = new SettingsStore(new LocalDataPaths(temp.Path));
        await settings.SaveAsync((await settings.LoadAsync()) with { ShareLinkedAccounts = true });
        var handler = new RecordingHandler { Fail = true };
        await using var coordinator = CreateCoordinator(temp.Path, handler, sharing: true,
            async (enabled, ct) => await settings.SaveAsync((await settings.LoadAsync(ct)) with { ShareLinkedAccounts = enabled }, ct));
        await coordinator.InitializeAsync();
        await coordinator.SetSharingEnabledAsync(false, CancellationToken.None);
        Assert.False((await settings.LoadAsync()).ShareLinkedAccounts);
        await coordinator.FlushPendingAsync();
        Assert.True(coordinator.HasPendingParticipation);
        handler.Fail = false;
        await coordinator.FlushPendingAsync();
        Assert.False(coordinator.HasPendingParticipation);
        using var json = JsonDocument.Parse(handler.Bodies.Last());
        Assert.False(json.RootElement.GetProperty("sharingEnabled").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("activePlayerIds").EnumerateArray());
    }

    [Fact]
    public async Task KeepsCacheAndLocalTrackingAvailableWhenServiceFails()
    {
        using var temp = new TempDirectory();
        var handler = new RecordingHandler();
        await using var coordinator = CreateCoordinator(temp.Path, handler, sharing: false);
        await coordinator.InitializeAsync();
        var first = await coordinator.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, CancellationToken.None);
        Assert.Single(first.Entries);
        handler.BadJson = true;
        await coordinator.RefreshPageAsync(LeaderboardPeriod.Daily, 1, 50, CancellationToken.None);
        var cached = await coordinator.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, CancellationToken.None);
        Assert.Single(cached.Entries);
        await using var restarted = CreateCoordinator(temp.Path, new RecordingHandler { Fail = true }, sharing: false);
        await restarted.InitializeAsync();
        var persisted = await restarted.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, CancellationToken.None);
        Assert.Single(persisted.Entries);
    }

    [Fact]
    public async Task NextSyncIncludesNewlyLinkedProfileAndOnlyOpenPlayerIds()
    {
        using var temp = new TempDirectory();
        var handler = new RecordingHandler();
        await using var coordinator = CreateCoordinator(temp.Path, handler, sharing: true);
        await coordinator.InitializeAsync();
        await coordinator.SyncParticipationAsync([new LeaderboardProfile(1, "First")], [1], CancellationToken.None);
        await coordinator.FlushPendingAsync();
        await coordinator.SyncParticipationAsync(
            [new LeaderboardProfile(1, "First"), new LeaderboardProfile(2, "Second")], [2, 999], CancellationToken.None);
        await coordinator.FlushPendingAsync();
        using var json = JsonDocument.Parse(handler.Bodies.Last());
        Assert.Equal(2, json.RootElement.GetProperty("linkedProfiles").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("activePlayerIds")[0].GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("activePlayerIds").GetArrayLength());
    }

    [Fact]
    public async Task TrackerReturnsOnlyCurrentlyOpenResolvedProfiles()
    {
        using var temp = new TempDirectory();
        await using var tracker = new XpTrackerCoordinator(new LocalDataPaths(temp.Path), _ => Task.CompletedTask);
        var accountId = Guid.NewGuid();
        tracker.Start(accountId, "Player", 42);
        Assert.Equal((accountId, 42, "Player"), Assert.Single(tracker.GetActiveLeaderboardProfiles()));
        tracker.Stop(accountId);
        Assert.Empty(tracker.GetActiveLeaderboardProfiles());
    }

    [Fact]
    public async Task UnchangedTrackerPollDoesNotSendExtraHeartbeat()
    {
        using var temp = new TempDirectory();
        var handler = new RecordingHandler();
        await using var coordinator = CreateCoordinator(temp.Path, handler, sharing: true);
        await coordinator.InitializeAsync();
        await coordinator.SyncParticipationAsync([new LeaderboardProfile(42, "Player")], [42], CancellationToken.None);
        await coordinator.FlushPendingAsync();
        var count = handler.Bodies.Count;
        await coordinator.SyncParticipationAsync([new LeaderboardProfile(42, "Player")], [42], CancellationToken.None);
        await coordinator.FlushPendingAsync();
        Assert.Equal(count, handler.Bodies.Count);
    }

    [Fact]
    public async Task DefaultPrivateStateNeverContactsParticipationEndpoint()
    {
        using var temp = new TempDirectory();
        var handler = new RecordingHandler();
        await using var coordinator = CreateCoordinator(temp.Path, handler, sharing: false);
        await coordinator.InitializeAsync();
        await coordinator.SyncParticipationAsync([new LeaderboardProfile(42, "Player")], [42], CancellationToken.None);
        await coordinator.FlushPendingAsync();
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task RestartClearsPendingActivityBeforeRetryingHeartbeat()
    {
        using var temp = new TempDirectory();
        var stateStore = new LeaderboardClientStateStore(new LocalDataPaths(temp.Path));
        var installationId = Guid.NewGuid();
        await stateStore.SaveAsync(new LeaderboardClientState(installationId, [],
            new ParticipationHeartbeat(installationId, true, [new LeaderboardProfile(42, "Player")], [42])));
        var handler = new RecordingHandler();
        await using var coordinator = CreateCoordinator(temp.Path, handler, sharing: true);
        await coordinator.InitializeAsync();
        var persisted = await stateStore.LoadAsync();
        Assert.Empty(persisted.PendingParticipation!.ActivePlayerIds);
        await coordinator.FlushPendingAsync();
        using var request = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Empty(request.RootElement.GetProperty("activePlayerIds").EnumerateArray());
        Assert.Equal(installationId, request.RootElement.GetProperty("installationId").GetGuid());
    }

    [Fact]
    public async Task SavingSecondPageKeepsFirstPageAvailableOffline()
    {
        using var temp = new TempDirectory();
        await using (var online = CreateCoordinator(temp.Path, new RecordingHandler(), sharing: false))
        {
            await online.InitializeAsync();
            Assert.Equal(1, (await online.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, CancellationToken.None)).Page);
            Assert.Equal(2, (await online.GetPageAsync(LeaderboardPeriod.Daily, 2, 50, CancellationToken.None)).Page);
        }
        await using var offline = CreateCoordinator(temp.Path, new RecordingHandler { Fail = true }, sharing: false);
        await offline.InitializeAsync();
        var first = await offline.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, CancellationToken.None);
        Assert.Equal("Player 1", Assert.Single(first.Entries).Username);
        var second = await offline.GetPageAsync(LeaderboardPeriod.Daily, 2, 50, CancellationToken.None);
        Assert.Equal("Player 2", Assert.Single(second.Entries).Username);
    }

    [Fact]
    public async Task FailingLeaderboardDoesNotPreventLocalTrackerStart()
    {
        using var temp = new TempDirectory();
        await using var coordinator = CreateCoordinator(temp.Path, new RecordingHandler { Fail = true }, sharing: true);
        await coordinator.InitializeAsync();
        await coordinator.SyncParticipationAsync([new LeaderboardProfile(42, "Player")], [42], CancellationToken.None);
        await coordinator.FlushPendingAsync();
        await using var tracker = new XpTrackerCoordinator(new LocalDataPaths(temp.Path), _ => Task.CompletedTask);
        var accountId = Guid.NewGuid();
        tracker.Start(accountId, "Player", 42);
        Assert.Equal(accountId, Assert.Single(tracker.GetStates()).AccountId);
        Assert.Equal(42, Assert.Single(tracker.GetActiveLeaderboardProfiles()).PlayerId);
        Assert.True(coordinator.HasPendingParticipation);
    }

    private static LeaderboardCoordinator CreateCoordinator(string path, RecordingHandler handler, bool sharing,
        Func<bool, CancellationToken, Task>? persist = null)
    {
        var client = new LeaderboardApiClient(new HttpClient(handler),
            new LeaderboardApiOptions(new Uri("https://leaderboard.example/")));
        return new LeaderboardCoordinator(new LeaderboardClientStateStore(new LocalDataPaths(path)),
            client, sharing, persist ?? ((_, _) => Task.CompletedTask));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public bool Fail { get; set; }
        public bool BadJson { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(ct));
            if (Fail) throw new HttpRequestException("offline");
            if (request.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.NoContent);
            var page = request.RequestUri!.Query.Contains("page=2&", StringComparison.Ordinal) ? 2 : 1;
            var body = BadJson ? "{broken" : $$"""
                {"period":0,"periodStartUtc":"2026-09-23T00:00:00Z","periodEndUtc":"2026-09-24T00:00:00Z","page":{{page}},"pageSize":50,"totalEntries":2,"generatedAtUtc":"2026-09-23T12:00:00Z","entries":[{"rank":{{page}},"playerId":{{41 + page}},"username":"Player {{page}}","xpGained":100,"lastSampledAtUtc":"2026-09-23T12:00:00Z","isStale":false}]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fourfold-leaderboard-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
