using FourFoldAccountManager.Core.LiveFeed;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.LiveFeed;

public sealed class GameMessageDecoderTests
{
    private static byte[] BattleResultPayload() =>
        new MirrorWriter()
            .Bool(true)            // leveledUp
            .VarInt(37)            // silverGained
            .VarInt(1250)          // expGained
            .VarInt(380)           // expNeededToNextLevel
            .VarInt(12)            // reachedLevel
            .String("Savage")      // className
            .String("Whirlwind")   // unlockedSkillName
            .VarInt(4)             // gainedMaxSP
            .VarInt(2)             // gainedSKL
            .VarInt(56)            // gainedMaxHP
            .VarInt(3)             // gainedATT
            .VarInt(1)             // gainedMAG
            .VarInt(2)             // gainedSPD
            .VarInt(1)             // gainedDEF
            .VarInt(0)             // gainedRES
            .VarInt(1)             // gainedLCK
            .VarInt(120)           // gainedHP
            .VarInt(9)             // gainedSP
            .ToArray();

    // Wrong XP or silver numbers would reach every tracker plugin.
    [Fact]
    public void ABattleResultDecodesToItsExactValues()
    {
        var frame = MirrorWriter.Batch(
            MirrorWriter.Rpc(GameMessageDecoder.BattleResultSignature, BattleResultPayload()));

        var result = GameMessageDecoder.DecodeBatch(frame);

        Assert.Equal(
            new BattleResult(true, 37, 1250, 380, 12, "Savage", "Whirlwind",
                new StatGains(MaxHp: 56, MaxSp: 4, Hp: 120, Sp: 9, Att: 3, Mag: 1, Skl: 2, Spd: 2, Def: 1, Res: 0, Lck: 1)),
            Assert.Single(result.Events));
        Assert.Equal(0, result.Failures);
    }

    // A payload that doesn't end exactly where the signature says it should is a different message wearing an
    // allowlisted id (a 16-bit collision after a game update), so it must never become an event.
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void APayloadOfTheWrongLengthIsAFailureNotAnEvent(int lengthChange)
    {
        var payload = BattleResultPayload();
        var altered = lengthChange < 0 ? payload[..^1] : [.. payload, 0x00];
        var frame = MirrorWriter.Batch(MirrorWriter.Rpc(GameMessageDecoder.BattleResultSignature, altered));

        var result = GameMessageDecoder.DecodeBatch(frame);

        Assert.Empty(result.Events);
        Assert.Equal(1, result.Failures);
    }

    // Chat, friends and every other message stay unread: nothing about other players reaches the feed.
    [Fact]
    public void MessagesOutsideTheAllowlistProduceNothing()
    {
        var chat = MirrorWriter.Message("Game.Chat.SceneChatMessage", new MirrorWriter()
            .String("shikaakwa_b3").String("").String("scene").String("SomeoneElse").String("hello")
            .Bool(false).Bool(false).Bool(false).ToArray());
        var unknownRpc = MirrorWriter.Rpc(
            "System.Void PlayerSpeech::RpcSay(System.String)", new MirrorWriter().String("hi").ToArray());

        var result = GameMessageDecoder.DecodeBatch(MirrorWriter.Batch(chat, unknownRpc));

        Assert.Empty(result.Events);
        Assert.Equal(0, result.Failures);
    }

    // The game server's traffic is untrusted input; nothing in it may crash or hang the app.
    [Fact]
    public void GarbageNeverThrows()
    {
        var random = new Random(20261006);
        string[] signatures =
        [
            GameMessageDecoder.OpenHudSignature, GameMessageDecoder.CloseHudSignature,
            GameMessageDecoder.BattleResultSignature, GameMessageDecoder.SkillResultSignature,
            GameMessageDecoder.LoadSceneSignature
        ];
        for (var i = 0; i < 2000; i++)
        {
            var noise = new byte[random.Next(0, 300)];
            random.NextBytes(noise);
            GameMessageDecoder.DecodeBatch(noise);
            GameMessageDecoder.DecodeBatch(MirrorWriter.Batch(MirrorWriter.Rpc(signatures[i % signatures.Length], noise)));
            GameMessageDecoder.DecodeBatch(MirrorWriter.Batch(
                MirrorWriter.Message(GameMessageDecoder.AuthResponseType, noise)));
        }
    }

    // A digit from another script must not slip past the skill-text pattern into int.Parse and crash the feed.
    [Fact]
    public void ANonAsciiDigitInSkillTextIsAFailureNotACrash()
    {
        var payload = new MirrorWriter().Bool(true).String("Fireball affected ٣ targets.").ToArray();

        var result = GameMessageDecoder.DecodeBatch(
            MirrorWriter.Batch(MirrorWriter.Rpc(GameMessageDecoder.SkillResultSignature, payload)));

        Assert.Empty(result.Events);
        Assert.Equal(1, result.Failures);
    }
}
