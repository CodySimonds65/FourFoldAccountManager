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

        var classes = new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var invalid = new List<string>();
        var invalidStats = new List<string>();
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
            var stats = ReadStats(card, out var statInvalid);
            if (statInvalid)
            {
                invalidStats.Add(className);
            }

            if (!TryParsePositiveInt(levelText, out var level) || xpParts is not { Length: 2 } ||
                !TryParseNonnegativeLong(xpParts[0], out var currentXp) ||
                !TryParseNonnegativeLong(xpParts[1], out var nextLevelXp) ||
                nextLevelXp == 0 || currentXp >= nextLevelXp)
            {
                invalid.Add(className);
                continue;
            }

            classes.Add(className, new ClassProfileSnapshot(level, currentXp, nextLevelXp, ReadMeta(card, "Updated"))
            {
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

        return new PlayerProgressSnapshot(username, activeClassName, classes, invalid, invalidStats);
    }

    private static ProfileStats ReadStats(IElement card, out bool invalid)
    {
        invalid = false;
        var hp = TryParseDisplayedStat(ReadMeta(card, "HP"), useMaximum: true, out var hpValue);
        var sp = TryParseDisplayedStat(ReadMeta(card, "SP"), useMaximum: true, out var spValue);
        var attack = TryParseDisplayedStat(ReadMeta(card, "ATT"), useMaximum: false, out var attackValue);
        var magic = TryParseDisplayedStat(ReadMeta(card, "MAG"), useMaximum: false, out var magicValue);
        var skill = TryParseDisplayedStat(ReadMeta(card, "SKL"), useMaximum: false, out var skillValue);
        var speed = TryParseDisplayedStat(ReadMeta(card, "SPD"), useMaximum: false, out var speedValue);
        var defense = TryParseDisplayedStat(ReadMeta(card, "DEF"), useMaximum: false, out var defenseValue);
        var resistance = TryParseDisplayedStat(ReadMeta(card, "RES"), useMaximum: false, out var resistanceValue);
        var luck = TryParseDisplayedStat(ReadMeta(card, "LCK"), useMaximum: false, out var luckValue);
        invalid = !(hp && sp && attack && magic && skill && speed && defense && resistance && luck);
        return new ProfileStats(hpValue, spValue, attackValue, magicValue, skillValue, speedValue,
            defenseValue, resistanceValue, luckValue);
    }

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

    private static bool TryParseDisplayedStat(string? value, bool useMaximum, out long? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (useMaximum && parts.Length != 2)
        {
            return false;
        }

        if (useMaximum && !TryParseNonnegativeLong(parts[0], out _))
        {
            return false;
        }

        var numericValue = useMaximum ? parts[1] : parts[0];
        if (!TryParseNonnegativeLong(numericValue, out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
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
