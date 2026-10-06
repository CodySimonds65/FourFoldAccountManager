using System.Globalization;
using System.Text.RegularExpressions;

namespace FourFoldAccountManager.Core.LiveFeed;

// Turns one WebSocket frame (a Mirror batch) into the allowlisted events. Anything not on the allowlist is skipped
// without reading past its id (an RPC: past its function hash). Allowlisted messages are parsed strictly: every field
// in order, the payload consumed exactly, values in range. One that fails is a Failure, never an event.
public static partial class GameMessageDecoder
{
    // Exact signatures and type names from the decompiled game. Mirror hashes these strings, so they must match
    // character for character. After a game update, see the spec's "After a game update" procedure.
    public const string OpenHudSignature =
        "System.Void BattlePlayerCommands2::TargetOpenHUD(Mirror.NetworkConnectionToClient,System.UInt32[],System.Boolean)";

    public const string CloseHudSignature =
        "System.Void BattlePlayerCommands2::TargetCloseHUD(Mirror.NetworkConnectionToClient,System.Boolean)";

    public const string BattleResultSignature =
        "System.Void BattlePlayerCommands2::TargetShowBattleResult(Mirror.NetworkConnectionToClient,System.Boolean,System.Int32,System.Int32,System.Int32,System.Int32,System.String,System.String,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32)";

    public const string SkillResultSignature =
        "System.Void BattlePlayerCommands2::TargetSkillResult(Mirror.NetworkConnectionToClient,System.Boolean,System.String)";

    public const string LoadSceneSignature =
        "System.Void SimpleScenePlayer::TargetLoadScene(Mirror.NetworkConnectionToClient,System.String,System.String,UnityEngine.Vector2,System.Int32,System.Boolean)";

    public const string RpcMessageType = "Mirror.RpcMessage";
    public const string AuthResponseType = "Game.Auth.AuthResponseMessage";
    public const string ModerationDisconnectType = "Game.Auth.ModerationDisconnectMessage";

    private const int MaxKeptText = 256;
    private const int MaxSkippedText = 4096;
    private const int MaxEnemies = 16;
    private const string MissedSuffix = " missed.";

    private static readonly ushort RpcMessageId = MirrorHash.MessageId(RpcMessageType);
    private static readonly ushort AuthResponseId = MirrorHash.MessageId(AuthResponseType);
    private static readonly ushort ModerationDisconnectId = MirrorHash.MessageId(ModerationDisconnectType);
    private static readonly ushort OpenHud = MirrorHash.FunctionHash(OpenHudSignature);
    private static readonly ushort CloseHud = MirrorHash.FunctionHash(CloseHudSignature);
    private static readonly ushort BattleResultHash = MirrorHash.FunctionHash(BattleResultSignature);
    private static readonly ushort SkillResultHash = MirrorHash.FunctionHash(SkillResultSignature);
    private static readonly ushort LoadScene = MirrorHash.FunctionHash(LoadSceneSignature);

    private enum Outcome
    {
        Ignored,
        Recognized,
        Failed,
        Malformed
    }

    public static BatchResult DecodeBatch(ReadOnlySpan<byte> batch)
    {
        var events = new List<GameEvent>();
        var failures = 0;
        var malformed = 0;
        var reader = new MirrorReader(batch);
        try
        {
            // The batch timestamp. Events use the frame's arrival time, the app's clock, instead.
            reader.ReadDouble();
            while (reader.Remaining > 0)
            {
                var length = reader.ReadVarUInt();
                if (length > (ulong)reader.Remaining)
                {
                    throw new MirrorFormatException();
                }

                switch (DecodeMessage(reader.ReadBytes((int)length), out var gameEvent))
                {
                    case Outcome.Recognized:
                        events.Add(gameEvent!);
                        break;
                    case Outcome.Failed:
                        failures++;
                        break;
                    case Outcome.Malformed:
                        malformed++;
                        break;
                }
            }
        }
        catch (MirrorFormatException)
        {
            malformed++;
        }

        return new BatchResult(events, failures, malformed);
    }

    private static Outcome DecodeMessage(ReadOnlySpan<byte> message, out GameEvent? gameEvent)
    {
        gameEvent = null;
        var reader = new MirrorReader(message);
        ushort id;
        ushort hash = 0;
        try
        {
            id = reader.ReadUShort();
            if (id == RpcMessageId)
            {
                reader.ReadVarUInt(); // netId of the object the call is on
                reader.ReadByte();    // component index
                hash = reader.ReadUShort();
                if (hash != OpenHud && hash != CloseHud && hash != BattleResultHash && hash != SkillResultHash &&
                    hash != LoadScene)
                {
                    return Outcome.Ignored;
                }
            }
            else if (id != AuthResponseId && id != ModerationDisconnectId)
            {
                return Outcome.Ignored;
            }
        }
        catch (MirrorFormatException)
        {
            return Outcome.Malformed;
        }

        try
        {
            if (id == RpcMessageId)
            {
                var payload = reader.ReadSegmentAndSize();
                End(ref reader);
                gameEvent = ParseRpc(hash, payload);
            }
            else
            {
                gameEvent = id == AuthResponseId ? ParseAuthResponse(ref reader) : ParseModerationDisconnect(ref reader);
                End(ref reader);
            }

            return Outcome.Recognized;
        }
        catch (MirrorFormatException)
        {
            return Outcome.Failed;
        }
    }

    private static GameEvent ParseRpc(ushort hash, ReadOnlySpan<byte> payload)
    {
        var reader = new MirrorReader(payload);
        GameEvent result;
        if (hash == OpenHud)
        {
            result = ParseOpenHud(ref reader);
        }
        else if (hash == CloseHud)
        {
            reader.ReadBool(); // victory: always false from the server, not kept
            result = new BattleEnded();
        }
        else if (hash == BattleResultHash)
        {
            result = ParseBattleResult(ref reader);
        }
        else if (hash == SkillResultHash)
        {
            result = ParseSkillResult(ref reader);
        }
        else
        {
            result = ParseLoadScene(ref reader);
        }

        End(ref reader);
        return result;
    }

    private static BattleStarted ParseOpenHud(ref MirrorReader reader)
    {
        var count = reader.ReadArrayCount(MaxEnemies);
        for (var i = 0; i < count; i++)
        {
            reader.ReadVarUInt(); // an enemy's netId: read to check the length, not kept
        }

        reader.ReadBool(); // moveActionMenuUp, not kept
        return new BattleStarted(Math.Max(count, 0));
    }

    private static BattleResult ParseBattleResult(ref MirrorReader reader)
    {
        // Wire order from BattlePlayerCommands2.TargetShowBattleResult.
        var leveledUp = reader.ReadBool();
        var silver = AtLeastZero(reader.ReadVarInt32());
        var exp = AtLeastZero(reader.ReadVarInt32());
        var needed = AtLeastZero(reader.ReadVarInt32());
        var level = reader.ReadVarInt32();
        if (level is < 1 or > 999)
        {
            throw new MirrorFormatException();
        }

        var className = reader.ReadString(MaxKeptText) ?? string.Empty;
        var unlocked = reader.ReadString(MaxKeptText);
        var maxSp = AtLeastZero(reader.ReadVarInt32());
        var skl = AtLeastZero(reader.ReadVarInt32());
        var maxHp = AtLeastZero(reader.ReadVarInt32());
        var att = AtLeastZero(reader.ReadVarInt32());
        var mag = AtLeastZero(reader.ReadVarInt32());
        var spd = AtLeastZero(reader.ReadVarInt32());
        var def = AtLeastZero(reader.ReadVarInt32());
        var res = AtLeastZero(reader.ReadVarInt32());
        var lck = AtLeastZero(reader.ReadVarInt32());
        var hp = AtLeastZero(reader.ReadVarInt32());
        var sp = AtLeastZero(reader.ReadVarInt32());
        return new BattleResult(leveledUp, silver, exp, needed, level, className,
            string.IsNullOrEmpty(unlocked) ? null : unlocked,
            new StatGains(maxHp, maxSp, hp, sp, att, mag, skl, spd, def, res, lck));
    }

    // The texts come from SkillCombatService.BuildResultMessage and the rejections in BattlePlayerCommands2.
    private static SkillResult ParseSkillResult(ref MirrorReader reader)
    {
        var ok = reader.ReadBool();
        var text = reader.ReadString(MaxKeptText) ?? string.Empty;
        if (!ok)
        {
            return new SkillResult(null, SkillOutcome.Rejected, 0, text.Length == 0 ? null : text);
        }

        if (text.Length > MissedSuffix.Length && text.EndsWith(MissedSuffix, StringComparison.Ordinal))
        {
            return new SkillResult(text[..^MissedSuffix.Length], SkillOutcome.Miss, 0, null);
        }

        var match = AffectedText().Match(text);
        if (!match.Success)
        {
            throw new MirrorFormatException();
        }

        return new SkillResult(match.Groups["name"].Value, SkillOutcome.Hit,
            int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture), null);
    }

    private static SceneLoaded ParseLoadScene(ref MirrorReader reader)
    {
        var scene = reader.ReadString(MaxKeptText);
        if (string.IsNullOrWhiteSpace(scene))
        {
            throw new MirrorFormatException();
        }

        reader.SkipString(MaxSkippedText); // spawnId
        reader.ReadBytes(8);               // serverOffset (Vector2): instance coordinates, not kept
        reader.ReadVarInt32();             // transitionSerial
        reader.ReadBool();                 // useWorldPortalFade
        return new SceneLoaded(scene);
    }

    private static LoggedIn ParseAuthResponse(ref MirrorReader reader)
    {
        var success = reader.ReadBool();
        reader.ReadVarInt();               // accountId: the game's database id, never kept
        reader.SkipString(MaxSkippedText); // username, never kept
        reader.ReadBool();                 // isAdmin, never kept
        reader.SkipString(MaxSkippedText); // error text
        return new LoggedIn(success);
    }

    private static ModerationDisconnected ParseModerationDisconnect(ref MirrorReader reader) =>
        new(reader.ReadString(MaxKeptText));

    private static void End(ref MirrorReader reader)
    {
        if (reader.Remaining != 0)
        {
            throw new MirrorFormatException();
        }
    }

    private static int AtLeastZero(int value) => value >= 0 ? value : throw new MirrorFormatException();

    [GeneratedRegex(@"^(?<name>.+) affected (?<count>[0-9]{1,3}) targets?\.( [0-9]{1,3} targets? resisted\.)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex AffectedText();
}
