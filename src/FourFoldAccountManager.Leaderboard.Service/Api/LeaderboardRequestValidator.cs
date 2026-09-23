using FourFoldAccountManager.Core.Leaderboard;

namespace FourFoldAccountManager.Leaderboard.Service.Api;

public static class LeaderboardRequestValidator
{
    public const int MaximumBodyBytes = 512 * 1024;

    public static bool IsValid(ParticipationHeartbeat? heartbeat)
    {
        if (heartbeat is null || heartbeat.InstallationId == Guid.Empty ||
            heartbeat.LinkedProfiles is null || heartbeat.ActivePlayerIds is null ||
            heartbeat.LinkedProfiles.Count > 1_000 || heartbeat.ActivePlayerIds.Count > 5)
            return false;

        var linked = new HashSet<int>();
        foreach (var profile in heartbeat.LinkedProfiles)
        {
            if (profile is null || profile.PlayerId <= 0 ||
                string.IsNullOrWhiteSpace(profile.Username) || profile.Username.Length > 256 ||
                !linked.Add(profile.PlayerId))
                return false;
        }

        var active = new HashSet<int>();
        foreach (var id in heartbeat.ActivePlayerIds)
            if (id <= 0 || !linked.Contains(id) || !active.Add(id)) return false;

        return true;
    }

    public static bool TryParsePeriod(string name, out LeaderboardPeriod period)
    {
        period = name switch
        {
            "daily" => LeaderboardPeriod.Daily,
            "weekly" => LeaderboardPeriod.Weekly,
            "monthly" => LeaderboardPeriod.Monthly,
            _ => (LeaderboardPeriod)(-1)
        };
        return Enum.IsDefined(period);
    }

    public static bool TryParsePagination(string? pageText, string? sizeText, out int page, out int size)
    {
        page = 1;
        size = 50;
        if (pageText is not null && (!int.TryParse(pageText, out page) || page < 1)) return false;
        if (sizeText is not null && (!int.TryParse(sizeText, out size) || size < 1)) return false;
        size = Math.Min(size, 100);
        return true;
    }
}
