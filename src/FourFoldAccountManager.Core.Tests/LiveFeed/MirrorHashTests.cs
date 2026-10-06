using FourFoldAccountManager.Core.LiveFeed;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.LiveFeed;

public sealed class MirrorHashTests
{
    // Pins the decoder's own signature constants to the ints the decompiled game passes to SendTargetRPCInternal. If a
    // signature or the hash drifts, the feed would silently never match these calls; after a game update, refresh both
    // from the new decompile.
    [Theory]
    [InlineData(GameMessageDecoder.OpenHudSignature, 524349864)]
    [InlineData(GameMessageDecoder.CloseHudSignature, -274237404)]
    [InlineData(GameMessageDecoder.BattleResultSignature, -2013908526)]
    [InlineData(GameMessageDecoder.SkillResultSignature, 884216395)]
    [InlineData(GameMessageDecoder.LoadSceneSignature, 565832018)]
    public void SignaturesHashToTheGamesOwnValues(string signature, int decompiledHash)
    {
        Assert.Equal(decompiledHash, MirrorHash.StableHash(signature));
        Assert.Equal((ushort)(decompiledHash & 0xFFFF), MirrorHash.FunctionHash(signature));
    }
}
