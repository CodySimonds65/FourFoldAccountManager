namespace FourFoldAccountManager.Core.LiveFeed;

// What the decoder makes of the allowlisted messages. Only the fields the feed keeps are here; the rest are read to
// check the length and dropped.
public abstract record GameEvent;

public sealed record BattleStarted(int EnemyCount) : GameEvent;

public sealed record BattleEnded(bool Victory) : GameEvent;

public sealed record StatGains(int MaxHp, int MaxSp, int Hp, int Sp, int Att, int Mag, int Skl, int Spd, int Def, int Res, int Lck);

public sealed record BattleResult(
    bool LeveledUp,
    int SilverGained,
    int ExpGained,
    int ExpNeededToNextLevel,
    int ReachedLevel,
    string ClassName,
    string? UnlockedSkillName,
    StatGains Gains) : GameEvent;

public enum SkillOutcome
{
    Hit,
    Miss,
    Rejected
}

public sealed record SkillResult(string? SkillName, SkillOutcome Outcome, int TargetsAffected, string? Reason) : GameEvent;

public sealed record SceneLoaded(string Scene) : GameEvent;

// Failed logins are recognized too: they still prove the message id matches, which the canary needs.
public sealed record LoggedIn(bool Success) : GameEvent;

public sealed record ModerationDisconnected(string? Reason) : GameEvent;

// Failures: allowlisted messages that failed strict parsing; they mean the protocol no longer matches. Malformed:
// framing that couldn't be read at all; counted, but not taken as proof of a mismatch.
public sealed record BatchResult(IReadOnlyList<GameEvent> Events, int Failures, int Malformed);

public static class LiveFeedRules
{
    // The game's own rule (ClickToMove.IsBattleSceneName): battle instances are named with "battle".
    public static bool IsBattleScene(string scene) => scene.Trim().Contains("battle", StringComparison.OrdinalIgnoreCase);
}
