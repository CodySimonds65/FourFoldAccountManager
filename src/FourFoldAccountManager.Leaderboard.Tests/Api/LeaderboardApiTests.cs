using System.Net;
using System.Net.Http.Json;
using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Api;

public sealed class LeaderboardApiTests
{
    [Fact]
    public async Task PublicReadsAreLimitedToSixtyPerMinutePerIp()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/leaderboards/daily")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.GetAsync("/v1/leaderboards/daily")).StatusCode);
    }

    [Fact]
    public async Task CapacityFailureReturnsServiceUnavailable()
    {
        using var fixture = new ApiFactory();
        fixture.Store.RejectEnrollment = true;
        using var client = fixture.CreateClient();
        var response = await client.PutAsJsonAsync("/v1/participation",
            new ParticipationHeartbeat(Guid.NewGuid(), true, [new LeaderboardProfile(1, "Alice")], [1]));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OptOutPassesDisabledEnrollmentToStore()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var id = Guid.NewGuid();
        var response = await client.PutAsJsonAsync("/v1/participation",
            new ParticipationHeartbeat(id, false, [], []));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(id, fixture.Store.LastHeartbeat?.InstallationId);
        Assert.False(fixture.Store.LastHeartbeat?.SharingEnabled);
    }

    [Theory]
    [InlineData("yearly")]
    [InlineData("Daily")]
    public async Task RejectsUnknownOrNoncanonicalPeriods(string period)
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/v1/leaderboards/{period}")).StatusCode);
    }

    [Fact]
    public async Task RejectsPageBelowOne()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/v1/leaderboards/daily?page=0")).StatusCode);
    }

    [Fact]
    public async Task CapsPageSizeAndServesAnonymousReads()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var response = await client.GetAsync("/v1/leaderboards/weekly?page=2&pageSize=200");
        var page = await response.Content.ReadFromJsonAsync<LeaderboardPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(LeaderboardPeriod.Weekly, page.Period);
        Assert.Equal(2, page.Page);
        Assert.Equal(100, page.PageSize);
        Assert.True(fixture.Store.LastStaleAfter >= TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    public async Task RejectsInvalidOrUnlinkedActivePlayerIds(int linkedId, int activeId)
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var payload = new ParticipationHeartbeat(Guid.NewGuid(), true,
            [new LeaderboardProfile(linkedId, "Alice")], [activeId]);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
        Assert.Null(fixture.Store.LastHeartbeat);
    }

    [Fact]
    public async Task RejectsMoreThanFiveActiveProfiles()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var profiles = Enumerable.Range(1, 6).Select(id => new LeaderboardProfile(id, $"Player{id}")).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation",
                new ParticipationHeartbeat(Guid.NewGuid(), true, profiles, profiles.Select(x => x.PlayerId).ToArray())))
            .StatusCode);
    }

    [Fact]
    public async Task RejectsDuplicateLinkedProfiles()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var payload = new ParticipationHeartbeat(Guid.NewGuid(), true,
            [new(1, "Alice"), new(1, "Alice")], [1]);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
    }

    [Fact]
    public async Task RejectsMoreThanOneThousandLinkedProfiles()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var profiles = Enumerable.Range(1, 1_001).Select(id => new LeaderboardProfile(id, $"Player{id}")).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation",
                new ParticipationHeartbeat(Guid.NewGuid(), true, profiles, []))).StatusCode);
    }

    [Fact]
    public async Task RejectsDuplicateActivePlayerIds()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation",
                new ParticipationHeartbeat(Guid.NewGuid(), true, [new(1, "Alice")], [1, 1]))).StatusCode);
    }

    [Fact]
    public async Task RejectsUsernameOverDialogLimit()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation",
                new ParticipationHeartbeat(Guid.NewGuid(), true, [new(1, new string('A', 257))], [1]))).StatusCode);
    }

    [Fact]
    public async Task RejectsEmptyInstallationId()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation",
                new ParticipationHeartbeat(Guid.Empty, true, [new(1, "Alice")], [1]))).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsBlankUsernames(string username)
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/v1/participation",
                new ParticipationHeartbeat(Guid.NewGuid(), true, [new(1, username)], [1]))).StatusCode);
    }

    [Fact]
    public async Task RejectsClientSubmittedXp()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var json = "{\"installationId\":\"" + Guid.NewGuid() + "\",\"sharingEnabled\":true," +
                   "\"linkedProfiles\":[{\"playerId\":1,\"username\":\"Alice\",\"xp\":999}]," +
                   "\"activePlayerIds\":[1]}";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsync("/v1/participation", content)).StatusCode);
    }

    [Fact]
    public async Task RejectsOversizedParticipationPayload()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        using var content = new StringContent("{\"extra\":\"" + new string('x', 512 * 1024) + "\"}",
            System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await client.PutAsync("/v1/participation", content)).StatusCode);
        Assert.Null(fixture.Store.LastHeartbeat);
    }

    [Fact]
    public async Task ParticipationWritesAreRateLimitedByIp()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        var payload = new ParticipationHeartbeat(Guid.NewGuid(), false, [], []);
        HttpStatusCode last = default;
        for (var i = 0; i < 31; i++)
            last = (await client.PutAsJsonAsync("/v1/participation", payload)).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task TrustedCloudflareClientIpsReceiveSeparateRateLimitBucketsOnRender()
    {
        var previous = Environment.GetEnvironmentVariable("RENDER");
        Environment.SetEnvironmentVariable("RENDER", "true");
        try
        {
            using var fixture = new ApiFactory(trustCloudflareIp: true);
            using var first = fixture.CreateClient();
            using var second = fixture.CreateClient();
            first.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.10");
            second.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.11");
            var payload = new ParticipationHeartbeat(Guid.NewGuid(), false, [], []);

            for (var i = 0; i < 30; i++)
                Assert.Equal(HttpStatusCode.NoContent,
                    (await first.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await first.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await second.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RENDER", previous);
        }
    }

    [Fact]
    public async Task ConfiguredCloudflareTrustIsIgnoredOutsideRender()
    {
        var previous = Environment.GetEnvironmentVariable("RENDER");
        Environment.SetEnvironmentVariable("RENDER", null);
        try
        {
            using var fixture = new ApiFactory(trustCloudflareIp: true);
            using var first = fixture.CreateClient();
            using var second = fixture.CreateClient();
            first.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.10");
            second.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.11");
            var payload = new ParticipationHeartbeat(Guid.NewGuid(), false, [], []);

            for (var i = 0; i < 30; i++)
                Assert.Equal(HttpStatusCode.NoContent,
                    (await first.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await second.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RENDER", previous);
        }
    }

    [Fact]
    public async Task CloudflareHeaderIsIgnoredUntilExplicitlyEnabled()
    {
        using var fixture = new ApiFactory();
        using var first = fixture.CreateClient();
        using var second = fixture.CreateClient();
        first.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.10");
        second.DefaultRequestHeaders.Add("CF-Connecting-IP", "198.51.100.11");
        var payload = new ParticipationHeartbeat(Guid.NewGuid(), false, [], []);

        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.NoContent,
                (await first.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await second.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
    }

    [Theory]
    [InlineData("198.51.100.10, 198.51.100.11")]
    [InlineData("1")]
    public async Task MalformedCloudflareIpFallsBackToSocketIp(string badValue)
    {
        using var renderMarker = new RenderMarkerScope("true");
        using var fixture = new ApiFactory(trustCloudflareIp: true);
        using var malformed = fixture.CreateClient();
        using var noHeader = fixture.CreateClient();
        malformed.DefaultRequestHeaders.Add("CF-Connecting-IP", badValue);
        var payload = new ParticipationHeartbeat(Guid.NewGuid(), false, [], []);

        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.NoContent,
                (await malformed.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await noHeader.PutAsJsonAsync("/v1/participation", payload)).StatusCode);
    }

    [Fact]
    public async Task LivenessDoesNotRequireDatabaseButReadinessDoes()
    {
        using var fixture = new ApiFactory();
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private sealed class RenderMarkerScope : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable("RENDER");

        public RenderMarkerScope(string? value) => Environment.SetEnvironmentVariable("RENDER", value);

        public void Dispose() => Environment.SetEnvironmentVariable("RENDER", _previous);
    }

    private sealed class ApiFactory(bool trustCloudflareIp = false) : WebApplicationFactory<Program>
    {
        public RecordingStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Leaderboard"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1",
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["RateLimiting:TrustCloudflareConnectingIp"] = trustCloudflareIp.ToString()
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILeaderboardStore>();
                services.AddSingleton<ILeaderboardStore>(Store);
            });
        }
    }

    private sealed class RecordingStore : ILeaderboardStore
    {
        public bool RejectEnrollment { get; set; }
        public ParticipationHeartbeat? LastHeartbeat { get; private set; }
        public TimeSpan LastStaleAfter { get; private set; }

        public Task ApplyHeartbeatAsync(ParticipationHeartbeat heartbeat, DateTimeOffset receivedAtUtc, CancellationToken ct)
        {
            if (RejectEnrollment) throw new LeaderboardCapacityExceededException("Leaderboard capacity exceeded");
            LastHeartbeat = heartbeat;
            return Task.CompletedTask;
        }

        public Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize,
            DateTimeOffset nowUtc, TimeSpan staleAfter, CancellationToken ct)
        {
            LastStaleAfter = staleAfter;
            return Task.FromResult(new LeaderboardPage(period, nowUtc.Date, nowUtc.Date.AddDays(1),
                page, pageSize, 0, nowUtc, []));
        }

        public Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesAsync(DateTimeOffset activeAfterUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActiveLeaderboardProfile>>([]);
        public Task<PlayerSampleState?> GetPlayerStateAsync(int playerId, CancellationToken ct) => Task.FromResult<PlayerSampleState?>(null);
        public Task SaveObservationAsync(PlayerObservation observation, PlayerSampleState? expectedState,
            TimeSpan activeLeaseDuration, CancellationToken ct) => Task.CompletedTask;
        public Task MarkNeedsBaselineAsync(int playerId, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteInactiveInstallationsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) => Task.CompletedTask;
    }
}
