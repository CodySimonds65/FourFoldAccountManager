using System.Globalization;

namespace FourFoldAccountManager.Core.Tracking;

public enum IdentityResolutionStatus
{
    Matched,
    Missing,
    Ambiguous
}

public sealed record IdentityResolution(IdentityResolutionStatus Status, int? PlayerId);

public static class RankingIdentityResolver
{
    public static IdentityResolution Resolve(string username, IReadOnlyList<RankingEntry> ranking)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(ranking);
        var matches = ranking.Where(row =>
                string.Equals(row.Username.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(row => row.PlayerId).Distinct().ToArray();
        return matches.Length switch
        {
            0 => new IdentityResolution(IdentityResolutionStatus.Missing, null),
            1 => new IdentityResolution(IdentityResolutionStatus.Matched, matches[0]),
            _ => new IdentityResolution(IdentityResolutionStatus.Ambiguous, null)
        };
    }

    public static int? ParsePlayerProfileReference(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        input = input.Trim();
        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return numericId > 0 ? numericId : null;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "fourfoldonline.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.AbsolutePath != "/player.php" ||
            uri.Fragment.Length > 0)
        {
            return null;
        }

        return RankingHtmlParser.TryReadPlayerId(uri.Query, out var playerId) ? playerId : null;
    }
}
