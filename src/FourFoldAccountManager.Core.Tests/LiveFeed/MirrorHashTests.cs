using FourFoldAccountManager.Core.LiveFeed;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.LiveFeed;

public sealed class MirrorHashTests
{
    // The ints the decompiled game passes to SendTargetRPCInternal. If a signature string or the hash drifts, the feed
    // would silently never match these calls; after a game update, refresh both from the new decompile.
    [Theory]
    [InlineData("System.Void BattlePlayerCommands2::TargetOpenHUD(Mirror.NetworkConnectionToClient,System.UInt32[],System.Boolean)", 524349864)]
    [InlineData("System.Void BattlePlayerCommands2::TargetCloseHUD(Mirror.NetworkConnectionToClient,System.Boolean)", -274237404)]
    [InlineData("System.Void BattlePlayerCommands2::TargetShowBattleResult(Mirror.NetworkConnectionToClient,System.Boolean,System.Int32,System.Int32,System.Int32,System.Int32,System.String,System.String,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32)", -2013908526)]
    [InlineData("System.Void BattlePlayerCommands2::TargetSkillResult(Mirror.NetworkConnectionToClient,System.Boolean,System.String)", 884216395)]
    [InlineData("System.Void SimpleScenePlayer::TargetLoadScene(Mirror.NetworkConnectionToClient,System.String,System.String,UnityEngine.Vector2,System.Int32,System.Boolean)", 565832018)]
    public void SignaturesHashToTheGamesOwnValues(string signature, int decompiledHash)
    {
        Assert.Equal(decompiledHash, MirrorHash.StableHash(signature));
        Assert.Equal((ushort)(decompiledHash & 0xFFFF), MirrorHash.FunctionHash(signature));
    }
}
