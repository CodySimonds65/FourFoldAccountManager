using System.Net.Http;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests;

public sealed class PlayerProfileServiceTests
{
    [Fact]
    public async Task ReadAsync_resolves_unique_username_and_caches_profile_id()
    {
        var transport = new FakeTransport
        {
            Ranking = [new RankingEntry("Dweebstify", 98)],
            Profile = Profile("Dweebstify")
        };
        var service = new PlayerProfileService(transport);
        var accountId = Guid.NewGuid();

        var first = await service.ReadAsync(accountId, "dweebstify", null, CancellationToken.None);
        var second = await service.ReadAsync(accountId, "dweebstify", null, CancellationToken.None);

        Assert.Equal(PlayerProfileReadStatus.Success, first.Status);
        Assert.Equal(98, first.PlayerId);
        Assert.Equal(PlayerProfileReadStatus.Success, second.Status);
        Assert.Equal(1, transport.RankingCalls);
        Assert.Equal(2, transport.ProfileCalls);
    }

    [Fact]
    public async Task ReadAsync_uses_supplied_id_without_ranking_lookup()
    {
        var transport = new FakeTransport { Profile = Profile("Dweebstify") };
        var service = new PlayerProfileService(transport);

        var result = await service.ReadAsync(Guid.NewGuid(), "Dweebstify", 98, CancellationToken.None);

        Assert.Equal(PlayerProfileReadStatus.Success, result.Status);
        Assert.Equal(0, transport.RankingCalls);
        Assert.Equal(1, transport.ProfileCalls);
    }

    [Fact]
    public async Task ReadAsync_preserves_cached_snapshot_when_refresh_fails()
    {
        var transport = new FakeTransport
        {
            Ranking = [new RankingEntry("Dweebstify", 98)],
            Profile = Profile("Dweebstify")
        };
        var service = new PlayerProfileService(transport);
        var accountId = Guid.NewGuid();
        await service.ReadAsync(accountId, "Dweebstify", null, CancellationToken.None);
        transport.ThrowOnProfile = true;

        var result = await service.ReadAsync(accountId, "Dweebstify", null, CancellationToken.None);

        Assert.Equal(PlayerProfileReadStatus.ProfileUnavailable, result.Status);
        Assert.NotNull(result.Snapshot);
        Assert.Equal("Dweebstify", result.Snapshot!.Username);
        Assert.Equal(98, result.PlayerId);
    }

    [Fact]
    public async Task ReadAsync_rejects_profile_username_mismatch_and_invalidate_clears_cache()
    {
        var transport = new FakeTransport
        {
            Ranking = [new RankingEntry("Dweebstify", 98)],
            Profile = Profile("OtherPlayer")
        };
        var service = new PlayerProfileService(transport);
        var accountId = Guid.NewGuid();

        var mismatch = await service.ReadAsync(accountId, "Dweebstify", null, CancellationToken.None);
        service.Invalidate(accountId);

        transport.Profile = Profile("Dweebstify");
        var resolved = await service.ReadAsync(accountId, "Dweebstify", null, CancellationToken.None);

        Assert.Equal(PlayerProfileReadStatus.ProfileMismatch, mismatch.Status);
        Assert.Equal(PlayerProfileReadStatus.Success, resolved.Status);
        Assert.Equal(2, transport.RankingCalls);
    }

    private static PlayerProgressSnapshot Profile(string username) =>
        new(username, "Arctic Soldier", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Arctic Soldier"] = new(25, 1_000, 3_250, "today")
            {
                Hp = 100, Sp = 20, Attack = 10, Magic = 10, Skill = 10,
                Speed = 10, Defense = 10, Resistance = 10, Luck = 10
            }
        }, []);

    private sealed class FakeTransport : IPlayerProfileTransport
    {
        public IReadOnlyList<RankingEntry> Ranking { get; init; } = [];
        public PlayerProgressSnapshot? Profile { get; set; }
        public bool ThrowOnProfile { get; set; }
        public int RankingCalls { get; private set; }
        public int ProfileCalls { get; private set; }

        public Task<IReadOnlyList<RankingEntry>> GetRankingAsync(CancellationToken cancellationToken)
        {
            RankingCalls++;
            return Task.FromResult(Ranking);
        }

        public Task<PlayerProgressSnapshot> GetProfileAsync(int playerId, CancellationToken cancellationToken)
        {
            ProfileCalls++;
            if (ThrowOnProfile) throw new HttpRequestException("offline");
            return Task.FromResult(Profile ?? throw new InvalidOperationException("No profile configured."));
        }
    }
}
