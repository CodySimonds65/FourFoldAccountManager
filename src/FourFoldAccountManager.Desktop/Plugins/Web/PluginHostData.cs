namespace FourFoldAccountManager.Desktop.Plugins.Web;

public sealed record PluginAccountInfo(Guid Id, string Label, string? InGameName, bool IsOpen);

public sealed record PluginClassInfo(string ClassName, int Level, long CurrentXp, long NextLevelXp);

public sealed record PluginXpInfo(
    string? ClassName,
    int? Level,
    long? CurrentXp,
    long? NextLevelXp,
    long? XpUntilNextLevel,
    double? HoursUntilNextLevel,
    double? XpPerHour,
    long SessionXp,
    IReadOnlyList<PluginClassInfo> Classes,
    DateTimeOffset? UpdatedAt,
    bool IsStale);

public sealed record PluginStatsInfo(
    string ClassName,
    int Level,
    long? Hp,
    long? Sp,
    long? Attack,
    long? Magic,
    long? Skill,
    long? Speed,
    long? Luck,
    long? Defense,
    long? Resistance,
    PluginEquipmentInfo Equipment);

public sealed record PluginEquipmentInfo(string? Armor, string? Helmet, string? Hair, string? Weapon);

public sealed record PluginProfileInfo(
    int? PlayerId,
    long? Silver,
    long? Gold,
    string? Location,
    DateTimeOffset? UpdatedAt,
    bool IsStale);

public sealed record PluginLapInfo(int Number, long LapMs, long TotalMs);

public sealed record PluginTimerInfo(string State, long ElapsedMs, IReadOnlyList<PluginLapInfo> Laps);

// What the app shows to community plugins. Nothing here can reach a saved login, a game session or a file.
public interface IPluginHostData
{
    IReadOnlyList<PluginAccountInfo> GetAccounts();

    // Null when the account id isn't one of the user's accounts.
    PluginXpInfo? GetXp(Guid accountId);

    // Null before the account's first profile read.
    PluginStatsInfo? GetStats(Guid accountId);

    // Null when the account id isn't one of the user's accounts.
    PluginProfileInfo? GetProfile(Guid accountId);

    PluginTimerInfo GetTimer();

    // True while this plugin's panel is the one showing in the strip.
    bool IsPanelShowing(string pluginId);

    void OpenInBrowser(Uri uri);
}
