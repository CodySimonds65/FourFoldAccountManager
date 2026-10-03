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
        var username = document.QuerySelector(".social-profile-identity h1")?.TextContent.Trim();
        var cards = document.QuerySelectorAll(".social-class-panels .social-loadout-card");
        if (string.IsNullOrWhiteSpace(username) || cards.Length == 0)
        {
            throw new InvalidDataException("The player profile is missing its identity or class progression.");
        }

        var classes = new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? activeClassName = null;

        foreach (var card in cards)
        {
            var head = card.QuerySelector(".social-loadout-head");
            var className = head?.QuerySelector("h3")?.TextContent.Trim();
            if (string.IsNullOrWhiteSpace(className) || !seen.Add(className))
            {
                throw new InvalidDataException("The player profile has duplicate or unnamed classes.");
            }

            if (head!.QuerySelectorAll("span").Any(label =>
                    string.Equals(label.TextContent.Trim(), "Active class", StringComparison.OrdinalIgnoreCase)))
            {
                if (activeClassName is not null)
                {
                    throw new InvalidDataException("The player profile marks multiple active classes.");
                }

                activeClassName = className;
            }

            var levelText = head.QuerySelector("strong")?.TextContent.Replace("Level", "").Trim();
            var expText = ReadMeta(card, "EXP");
            var xpParts = expText?.Split('/', 2);
            var stats = ReadStats(card);

            if (!TryParsePositiveInt(levelText, out var level) || xpParts is not { Length: 2 } ||
                !TryParseNonnegativeLong(xpParts[0], out var currentXp) ||
                !TryParseNonnegativeLong(xpParts[1], out var nextLevelXp) ||
                nextLevelXp == 0 || currentXp >= nextLevelXp)
            {
                invalid.Add(className);
                continue;
            }

            // The social profile page no longer shows a per-class updated time.
            classes.Add(className, new ClassProfileSnapshot(level, currentXp, nextLevelXp, null)
            {
                ClassName = className,
                Hp = stats.Hp,
                Sp = stats.Sp,
                Attack = stats.Attack,
                Magic = stats.Magic,
                Skill = stats.Skill,
                Speed = stats.Speed,
                Defense = stats.Defense,
                Resistance = stats.Resistance,
                Luck = stats.Luck,
                Equipment = ReadEquipment(card)
            });
        }

        return new PlayerProgressSnapshot(username, activeClassName, classes, invalid);
    }

    private static ProfileStats ReadStats(IElement card) => new(
        ParseDisplayedStat(ReadMeta(card, "HP"), useMaximum: true),
        ParseDisplayedStat(ReadMeta(card, "SP"), useMaximum: true),
        ParseDisplayedStat(ReadMeta(card, "ATT"), useMaximum: false),
        ParseDisplayedStat(ReadMeta(card, "MAG"), useMaximum: false),
        ParseDisplayedStat(ReadMeta(card, "SKL"), useMaximum: false),
        ParseDisplayedStat(ReadMeta(card, "SPD"), useMaximum: false),
        ParseDisplayedStat(ReadMeta(card, "DEF"), useMaximum: false),
        ParseDisplayedStat(ReadMeta(card, "RES"), useMaximum: false),
        ParseDisplayedStat(ReadMeta(card, "LCK"), useMaximum: false));

    private static IReadOnlyDictionary<string, string> ReadEquipment(IElement card)
    {
        var equipment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in new[] { "Armor", "Helmet", "Hair", "Weapon" })
        {
            var value = ReadMeta(card, label);
            if (!string.IsNullOrWhiteSpace(value))
            {
                equipment[label] = value;
            }
        }

        return equipment;
    }

    // Every value is a <strong> whose label is the text or <span> right before it.
    private static string? ReadMeta(IElement card, string label) =>
        card.QuerySelectorAll(".social-loadout-vitals strong, .social-equipment-grid strong, .social-stat-strip strong")
            .FirstOrDefault(value => value.PreviousSibling?.TextContent.Trim() == label)?.TextContent.Trim();

    private static bool TryParsePositiveInt(string? value, out int result) =>
        int.TryParse(value?.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0;

    private static bool TryParseNonnegativeLong(string? value, out long result) =>
        long.TryParse(value?.Trim().Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;

    private static long? ParseDisplayedStat(string? value, bool useMaximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (useMaximum && (parts.Length != 2 || !TryParseNonnegativeLong(parts[0], out _)))
        {
            return null;
        }

        return TryParseNonnegativeLong(useMaximum ? parts[1] : parts[0], out var parsed) ? parsed : null;
    }

    private sealed record ProfileStats(
        long? Hp,
        long? Sp,
        long? Attack,
        long? Magic,
        long? Skill,
        long? Speed,
        long? Defense,
        long? Resistance,
        long? Luck);
}
