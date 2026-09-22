using System.Globalization;
using AngleSharp.Html.Parser;

namespace FourFoldAccountManager.Core.Tracking;

public static class RankingHtmlParser
{
    public static IReadOnlyList<RankingEntry> Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlParser().ParseDocument(html);
        var rows = document.QuerySelectorAll("table tbody tr");
        if (rows.Length == 0)
        {
            throw new InvalidDataException("The EXP ranking table is missing.");
        }

        var entries = new List<RankingEntry>(rows.Length);
        foreach (var row in rows)
        {
            var link = row.QuerySelector("td:nth-child(2) a");
            var username = link?.TextContent.Trim();
            var href = link?.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(username) || href is null ||
                !Uri.TryCreate(new Uri("https://fourfoldonline.com/"), href, out var uri) ||
                uri.Host != "fourfoldonline.com" || uri.AbsolutePath != "/player.php" ||
                !TryReadPlayerId(uri.Query, out var playerId))
            {
                throw new InvalidDataException("An EXP ranking row has no valid player link.");
            }

            entries.Add(new RankingEntry(username, playerId));
        }

        return entries;
    }

    internal static bool TryReadPlayerId(string query, out int playerId)
    {
        playerId = 0;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = part.Split('=', 2);
            if (fields.Length == 2 && fields[0] == "id" &&
                int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out playerId) &&
                playerId > 0)
            {
                return true;
            }
        }

        playerId = 0;
        return false;
    }
}
