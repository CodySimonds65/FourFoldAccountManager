using System.Net;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Collection;

public sealed class FourFoldPublicProfileSourceTests
{
    [Fact]
    public async Task DisabledCollectionMakesNoRequestEvenWithConfiguredRoute()
    {
        var handler = new StubHandler("ignored");
        var source = new FourFoldPublicProfileSource(new LeaderboardCollectionOptions
        {
            ProfileUrlTemplate = "https://example.invalid/player.php?id={playerId}",
            MinimumSampleInterval = TimeSpan.FromMinutes(1)
        }, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.FetchAsync(42, default));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ConfiguredHttpsRouteParsesPublicProfile()
    {
        var html = """
            <div class="social-profile-identity"><h1><span class="social-name">Alice</span></h1></div>
            <div class="social-class-panels"><article class="social-loadout-card">
            <div class="social-loadout-head"><div><span>Active class</span><h3>Warrior</h3><strong>Level 1</strong></div></div>
            <div class="social-loadout-vitals"><span>HP <strong>9 / 1,200</strong></span><span>EXP <strong>5 / 10</strong></span></div>
            <div class="social-equipment-grid"><div><span>Weapon</span><strong>Sword</strong></div></div>
            <div class="social-stat-strip"><span>ATT<strong>7</strong></span></div>
            </article></div>
            """;
        var handler = new StubHandler(html);
        var source = new FourFoldPublicProfileSource(new LeaderboardCollectionOptions
        {
            Enabled = true,
            MinimumSampleInterval = TimeSpan.FromMinutes(1),
            ProfileUrlTemplate = "https://example.invalid/player.php?id={playerId}"
        }, handler);

        var snapshot = await source.FetchAsync(42, default);

        Assert.Equal("Alice", snapshot.Username);
        var warrior = snapshot.Classes["Warrior"];
        Assert.Equal("Warrior", snapshot.ActiveClassName);
        Assert.Equal(1, warrior.Level);
        Assert.Equal(5, warrior.CurrentXp);
        Assert.Equal(10, warrior.NextLevelXp);
        Assert.Equal(1200, warrior.Hp);
        Assert.Equal(7, warrior.Attack);
        Assert.Equal("Sword", warrior.Equipment["Weapon"]);
        Assert.Equal("https://example.invalid/player.php?id=42", handler.LastUrl);
    }

    [Fact]
    public async Task RedirectResponseNeverRequestsTarget()
    {
        var handler = new RedirectHandler();
        var source = new FourFoldPublicProfileSource(new LeaderboardCollectionOptions
        {
            Enabled = true,
            MinimumSampleInterval = TimeSpan.FromMinutes(1),
            ProfileUrlTemplate = "https://example.invalid/player.php?id={playerId}"
        }, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync(42, default));

        Assert.Equal(["https://example.invalid/player.php?id=42"], handler.Urls);
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://redirect.invalid/target") },
                RequestMessage = request
            });
        }
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
