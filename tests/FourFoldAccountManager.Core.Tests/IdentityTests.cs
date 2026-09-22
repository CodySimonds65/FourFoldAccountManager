using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class IdentityTests
{
    [Fact]
    public void Exact_case_insensitive_name_matches_one_player()
    {
        var result = RankingIdentityResolver.Resolve(" desmond ", [new RankingEntry("Desmond", 83)]);
        Assert.Equal(IdentityResolutionStatus.Matched, result.Status);
        Assert.Equal(83, result.PlayerId);
    }

    [Fact]
    public void Substring_does_not_match()
    {
        var result = RankingIdentityResolver.Resolve("Des", [new RankingEntry("Desmond", 83)]);
        Assert.Equal(IdentityResolutionStatus.Missing, result.Status);
    }

    [Fact]
    public void Distinct_ids_with_same_name_are_ambiguous()
    {
        var result = RankingIdentityResolver.Resolve("desmond", [new RankingEntry("Desmond", 83), new RankingEntry("DESMOND", 99)]);
        Assert.Equal(IdentityResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.PlayerId);
    }

    [Theory]
    [InlineData("83", 83)]
    [InlineData("https://fourfoldonline.com/player.php?id=83", 83)]
    [InlineData("http://fourfoldonline.com/player.php?id=83", null)]
    [InlineData("https://other.example/player.php?id=83", null)]
    [InlineData("https://fourfoldonline.com/ranking.php?id=83", null)]
    [InlineData("0", null)]
    public void Profile_reference_accepts_only_safe_positive_ids(string input, int? expected)
    {
        Assert.Equal(expected, RankingIdentityResolver.ParsePlayerProfileReference(input));
    }

    [Fact]
    public async Task Legacy_account_json_loads_without_ranking_fields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fourfold-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var id = Guid.NewGuid();
            await File.WriteAllTextAsync(Path.Combine(directory, "accounts.json"),
                $"[{{\"id\":\"{id}\",\"label\":\"Local name\",\"isFavorite\":false,\"sortOrder\":0}}]");
            var account = Assert.Single(await new AccountStore(new LocalDataPaths(directory)).LoadAsync());
            Assert.Equal(id, account.Id);
            Assert.Null(account.RankingUsername);
            Assert.Null(account.RankingPlayerId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Store_rejects_blank_public_name_and_nonpositive_id()
    {
        var store = new AccountStore(new LocalDataPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([
            AccountProfile.Create("A") with { RankingUsername = "  " }]));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([
            AccountProfile.Create("A") with { RankingPlayerId = -3 }]));
    }
}
