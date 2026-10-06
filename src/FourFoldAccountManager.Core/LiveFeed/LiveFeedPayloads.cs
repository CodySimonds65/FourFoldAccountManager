using FourFoldAccountManager.Core.Plugins;

namespace FourFoldAccountManager.Core.LiveFeed;

// Exactly what plugins receive from the live feed, as in the spec's Plugin API table. This is the privacy boundary:
// a field that isn't in one of these records can't reach a plugin. Every string passes PublicGameText.
public sealed record LiveBattleStarted(Guid AccountId, int EnemyCount, DateTimeOffset At);

public sealed record LiveBattleEnded(Guid AccountId, bool Victory, DateTimeOffset At);

public sealed record LiveBattleResult(
    Guid AccountId,
    int ExpGained,
    int SilverGained,
    int ExpNeededToNextLevel,
    bool LeveledUp,
    int ReachedLevel,
    string? ClassName,
    string? UnlockedSkillName,
    StatGains StatGains,
    DateTimeOffset At);

public sealed record LiveSkillResult(
    Guid AccountId,
    string? SkillName,
    string Outcome,
    int TargetsAffected,
    string? Reason,
    DateTimeOffset At);

public sealed record LiveLocation(Guid AccountId, string? Scene, bool InBattle, DateTimeOffset At);

public sealed record LiveSession(Guid AccountId, DateTimeOffset At);

public sealed record LiveDisconnect(Guid AccountId, string? Reason, DateTimeOffset At);

public sealed record LiveStatus(string State, string? Reason);

public static class LiveFeedPayloads
{
    // The plugin event for a decoded game event, or null when the event isn't passed to plugins.
    public static (string Name, object Data)? ForPlugins(Guid accountId, GameEvent gameEvent, DateTimeOffset at) =>
        gameEvent switch
        {
            BattleStarted e => ("battle.started", new LiveBattleStarted(accountId, e.EnemyCount, at)),
            BattleEnded e => ("battle.ended", new LiveBattleEnded(accountId, e.Victory, at)),
            BattleResult e => ("battle.result", new LiveBattleResult(accountId, e.ExpGained, e.SilverGained,
                e.ExpNeededToNextLevel, e.LeveledUp, e.ReachedLevel, PluginText.PublicGameText(e.ClassName),
                PluginText.PublicGameText(e.UnlockedSkillName), e.Gains, at)),
            SkillResult e => ("battle.skillResult", new LiveSkillResult(accountId,
                PluginText.PublicGameText(e.SkillName),
                e.Outcome switch
                {
                    SkillOutcome.Hit => "hit",
                    SkillOutcome.Miss => "miss",
                    _ => "rejected"
                },
                e.TargetsAffected, PluginText.PublicGameText(e.Reason), at)),
            SceneLoaded e => ("location.changed", Location(accountId, e.Scene, at)),
            LoggedIn { Success: true } => ("session.loggedIn", new LiveSession(accountId, at)),
            ModerationDisconnected e => ("session.disconnected",
                new LiveDisconnect(accountId, PluginText.PublicGameText(e.Reason), at)),
            _ => null
        };

    public static LiveLocation Location(Guid accountId, string scene, DateTimeOffset at) =>
        new(accountId, PluginText.PublicGameText(scene), LiveFeedRules.IsBattleScene(scene), at);

    // The game socket closed without a reason from the server: a dropped connection, a reload, a closed panel.
    public static LiveDisconnect Disconnected(Guid accountId, DateTimeOffset at) => new(accountId, null, at);
}
