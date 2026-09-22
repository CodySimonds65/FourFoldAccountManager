using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace FourFoldAccountManager.Core.Tracking;

public static class PlayerProfileHtmlParser
{
    public static PlayerProgressSnapshot Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlParser().ParseDocument(html);
        var username = document.QuerySelector(".hero-title")?.TextContent.Trim();
        var cards = document.QuerySelectorAll(".class-grid .class-card .class-body");
        if (string.IsNullOrWhiteSpace(username) || cards.Length == 0)
        {
            throw new InvalidDataException("The player profile is missing its identity or class progression.");
        }

        var classes = new Dictionary<string, ClassXpSnapshot>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? activeClassName = null;

        foreach (var card in cards)
        {
            var className = card.QuerySelector("h3")?.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(className) || !seen.Add(className))
            {
                throw new InvalidDataException("The player profile has duplicate or unnamed classes.");
            }

            if (card.QuerySelectorAll(".badge").Any(badge => badge.TextContent.Trim() == "Active Class"))
            {
                if (activeClassName is not null)
                {
                    throw new InvalidDataException("The player profile marks multiple active classes.");
                }

                activeClassName = className;
            }

            var levelText = ReadMeta(card, "Level");
            var expText = ReadMeta(card, "EXP");
            var xpParts = expText?.Split('/', 2);
            if (!TryParsePositiveInt(levelText, out var level) || xpParts is not { Length: 2 } ||
                !TryParseNonnegativeLong(xpParts[0], out var currentXp) ||
                !TryParseNonnegativeLong(xpParts[1], out var nextLevelXp) ||
                nextLevelXp == 0 || currentXp >= nextLevelXp)
            {
                invalid.Add(className);
                continue;
            }

            classes.Add(className, new ClassXpSnapshot(level, currentXp, nextLevelXp, ReadMeta(card, "Updated")));
        }

        return new PlayerProgressSnapshot(username, activeClassName, classes, invalid);
    }

    private static string? ReadMeta(IElement card, string label)
    {
        var item = card.QuerySelectorAll(".meta-item")
            .FirstOrDefault(element => element.QuerySelector("strong")?.TextContent.Trim() == label);
        return item is null ? null : item.TextContent.Trim()[label.Length..].Trim();
    }

    private static bool TryParsePositiveInt(string? value, out int result) =>
        int.TryParse(value?.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0;

    private static bool TryParseNonnegativeLong(string? value, out long result) =>
        long.TryParse(value?.Trim().Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;
}
