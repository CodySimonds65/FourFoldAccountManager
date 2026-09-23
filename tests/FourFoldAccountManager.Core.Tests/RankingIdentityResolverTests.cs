using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class RankingIdentityResolverTests
{
    [Fact]
    public void Resolve_matches_one_username_case_insensitively()
    {
        var result = RankingIdentityResolver.Resolve("dWeEbStIfY", [new RankingEntry("Dweebstify", 98)]);

        Assert.Equal(IdentityResolutionStatus.Matched, result.Status);
        Assert.Equal(98, result.PlayerId);
    }

    [Fact]
    public void Resolve_marks_duplicate_username_matches_ambiguous()
    {
        var result = RankingIdentityResolver.Resolve("Dweebstify", [
            new RankingEntry("Dweebstify", 98),
            new RankingEntry("Dweebstify", 101)
        ]);

        Assert.Equal(IdentityResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.PlayerId);
    }

    [Fact]
    public void Resolve_marks_missing_username_as_not_ranked()
    {
        var result = RankingIdentityResolver.Resolve("Dweebstify", [new RankingEntry("OtherPlayer", 7)]);

        Assert.Equal(IdentityResolutionStatus.Missing, result.Status);
        Assert.Null(result.PlayerId);
    }
}
