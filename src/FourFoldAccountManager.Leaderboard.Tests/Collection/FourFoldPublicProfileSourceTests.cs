using System.Net;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Collection;

public sealed class FourFoldPublicProfileSourceTests
{
    [Fact]
    public async Task MissingApprovedRouteMakesNoRequest()
    {
        var handler = new StubHandler("ignored");
        var source = new FourFoldPublicProfileSource(new HttpClient(handler), new LeaderboardCollectionOptions
        {
            Enabled = true,
            MinimumSampleInterval = TimeSpan.FromMinutes(1)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.FetchAsync(42, default));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DisabledCollectionMakesNoRequestEvenWithConfiguredRoute()
    {
        var handler = new StubHandler("ignored");
        var source = new FourFoldPublicProfileSource(new HttpClient(handler), new LeaderboardCollectionOptions
        {
            ProfileUrlTemplate = "https://example.invalid/player.php?id={playerId}",
            MinimumSampleInterval = TimeSpan.FromMinutes(1)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.FetchAsync(42, default));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ConfiguredHttpsRouteParsesPublicProfile()
    {
        var html = """
            <h1 class="hero-title">Alice</h1>
            <div class="class-grid"><div class="class-card"><div class="class-body">
            <h3>Warrior</h3>
            <div class="meta-item"><strong>Level</strong> 1</div>
            <div class="meta-item"><strong>EXP</strong> 5 / 10</div>
            </div></div></div>
            """;
        var handler = new StubHandler(html);
        var source = new FourFoldPublicProfileSource(new HttpClient(handler), new LeaderboardCollectionOptions
        {
            Enabled = true,
            MinimumSampleInterval = TimeSpan.FromMinutes(1),
            ProfileUrlTemplate = "https://example.invalid/player.php?id={playerId}"
        });

        var snapshot = await source.FetchAsync(42, default);

        Assert.Equal("Alice", snapshot.Username);
        Assert.Equal(5, snapshot.Classes["Warrior"].CurrentXp);
        Assert.Equal("https://example.invalid/player.php?id=42", handler.LastUrl);
    }

    private sealed class StubHandler(string html) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUrl { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html),
                RequestMessage = request
            });
        }
    }
}
