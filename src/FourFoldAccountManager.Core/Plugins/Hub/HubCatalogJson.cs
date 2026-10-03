using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// Reads and writes the hub's catalog.json. Reading never throws: the file comes from the network, so an entry that
// breaks a rule is skipped, and a catalog that can't be used at all reads as null.
public static partial class HubCatalogJson
{
    public const int Version = 1;

    [GeneratedRegex(@"^[0-9a-f]{40}\z")]
    private static partial Regex CommitRegex();

    [GeneratedRegex(@"^[0-9a-f]{64}\z")]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(@"^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}\z")]
    private static partial Regex RepositoryRegex();

    // A full commit id in lowercase: never a branch, a tag or a short id.
    public static bool IsCommit([NotNullWhen(true)] string? value) => value is not null && CommitRegex().IsMatch(value);

    public static bool IsSha256([NotNullWhen(true)] string? value) => value is not null && Sha256Regex().IsMatch(value);

    // Exactly https://github.com/<owner>/<repo>.
    public static bool TryParseRepository(string? value, out Uri repository)
    {
        repository = null!;
        if (value is null || !RepositoryRegex().IsMatch(value) ||
            value.EndsWith("/.", StringComparison.Ordinal) || value.EndsWith("/..", StringComparison.Ordinal))
        {
            return false;
        }

        repository = new Uri(value);
        return true;
    }

    public static HubCatalog? Parse(string? json)
    {
        if (json is null || json.Length > HubLimits.MaximumCatalogBytes)
        {
            return null;
        }

        try
        {
            // A byte order mark is no part of the JSON, and some editors add one.
            using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != Version ||
                !TryGetList(root, "removed", out var removedElement) ||
                !TryGetList(root, "plugins", out var pluginsElement))
            {
                return null;
            }

            // Sets, not lists: a catalog can hold tens of thousands of ids, and a list scan per id takes seconds.
            var removed = new List<HubRemoval>();
            var removedIds = new HashSet<string>(StringComparer.Ordinal);
            if (removedElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in removedElement.EnumerateArray())
                {
                    if (TryReadRemoval(item) is { } removal && removedIds.Add(removal.Id))
                    {
                        removed.Add(removal);
                    }
                }
            }

            var plugins = new List<HubPlugin>();
            var pluginIds = new HashSet<string>(StringComparer.Ordinal);
            if (pluginsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in pluginsElement.EnumerateArray().Take(HubLimits.MaximumCatalogPlugins))
                {
                    if (TryReadPlugin(item) is { } plugin && !removedIds.Contains(plugin.Id) && pluginIds.Add(plugin.Id))
                    {
                        plugins.Add(plugin);
                    }
                }
            }

            return new HubCatalog(Array.AsReadOnly(plugins.ToArray()), Array.AsReadOnly(removed.ToArray()));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // A missing or null list reads as an empty one. Any other type makes the catalog unusable: reading it as empty
    // would drop the pulls, and the app would replace a good cached copy that still carries them.
    private static bool TryGetList(JsonElement root, string name, out JsonElement list)
    {
        list = default;
        return !root.TryGetProperty(name, out list) || list.ValueKind is JsonValueKind.Null or JsonValueKind.Array;
    }

    private static HubRemoval? TryReadRemoval(JsonElement item)
    {
        string id;
        try
        {
            if (item.ValueKind != JsonValueKind.Object ||
                PluginManifestReader.Text(item, "id") is not { } text || !PluginManifestReader.IsValidId(text))
            {
                return null;
            }

            id = text;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return null;
        }

        // A pull must never be lost over its wording: a reason that can't be read or shown is dropped, not the pull.
        try
        {
            var reason = PluginManifestReader.Text(item, "reason") ?? string.Empty;
            return new HubRemoval(
                id, reason.Length > 200 || PluginText.HasUnsafeCharacter(reason) ? string.Empty : reason);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return new HubRemoval(id, string.Empty);
        }
    }

    public static string Write(HubCatalog catalog) => JsonSerializer.Serialize(
        new
        {
            version = Version,
            plugins = catalog.Plugins.Select(plugin => new
            {
                id = plugin.Id,
                name = plugin.Name,
                shortLabel = plugin.ShortLabel,
                version = plugin.Version,
                author = plugin.Author,
                description = plugin.Description,
                sites = plugin.Sites.Select(site => site.GetLeftPart(UriPartial.Authority)),
                anySite = plugin.AnySite,
                cards = plugin.Cards.Select(card => new
                {
                    name = card.Name,
                    scope = card.Scope == OverlayAddOnScope.Account ? "account" : "global"
                }),
                repository = plugin.Repository.AbsoluteUri,
                commit = plugin.Commit,
                reviewed = plugin.Reviewed,
                size = plugin.Size,
                sha256 = plugin.Sha256
            }),
            removed = catalog.Removed.Select(removal => new { id = removal.Id, reason = removal.Reason })
        },
        AtomicJsonFile.Options);

    private static HubPlugin? TryReadPlugin(JsonElement item)
    {
        try
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = PluginManifestReader.Text(item, "id");
            var version = PluginManifestReader.Text(item, "version");
            var commit = PluginManifestReader.Text(item, "commit");
            var sha256 = PluginManifestReader.Text(item, "sha256");
            var description = PluginManifestReader.Text(item, "description") ?? string.Empty;
            if (!PluginManifestReader.IsValidId(id) || !PluginManifestReader.IsVersion(version) ||
                !PluginManifestReader.InRange(PluginManifestReader.Text(item, "name"), 1, 40, out var name) ||
                !PluginManifestReader.InRange(PluginManifestReader.Text(item, "shortLabel"), 1, 8, out var shortLabel) ||
                !PluginManifestReader.InRange(PluginManifestReader.Text(item, "author"), 1, 40, out var author) ||
                description.Length > 200 ||
                new[] { name, shortLabel, author, description }.Any(PluginText.HasUnsafeCharacter) ||
                !PluginManifestReader.TryReadSites(item, out var sites, out _) ||
                !TryParseRepository(PluginManifestReader.Text(item, "repository"), out var repository) ||
                !IsCommit(commit) || !IsSha256(sha256) ||
                !item.TryGetProperty("size", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number ||
                !sizeElement.TryGetInt64(out var size) || size < 1 || size > HubLimits.MaximumPackageBytes ||
                !TryReadCards(item, out var cards))
            {
                return null;
            }

            var reviewed = PluginManifestReader.Text(item, "reviewed");
            return new HubPlugin(
                id, name, shortLabel, version, author, description, sites,
                item.TryGetProperty("anySite", out var anySite) && anySite.ValueKind == JsonValueKind.True,
                cards, repository, commit,
                DateOnly.TryParseExact(reviewed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? reviewed!
                    : string.Empty,
                size, sha256);
        }
        catch (Exception)
        {
            // A host name Uri refuses, say. One bad entry never costs the rest of the catalog.
            return null;
        }
    }

    private static bool TryReadCards(JsonElement item, out IReadOnlyList<HubCard> cards)
    {
        cards = [];
        if (!item.TryGetProperty("cards", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 6)
        {
            return false;
        }

        var result = new List<HubCard>();
        foreach (var card in element.EnumerateArray())
        {
            if (card.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var scope = PluginManifestReader.Text(card, "scope");
            if (!PluginManifestReader.InRange(PluginManifestReader.Text(card, "name"), 1, 24, out var name) ||
                PluginText.HasUnsafeCharacter(name) || scope is not ("account" or "global"))
            {
                return false;
            }

            result.Add(new HubCard(name, scope == "account" ? OverlayAddOnScope.Account : OverlayAddOnScope.Global));
        }

        cards = Array.AsReadOnly(result.ToArray());
        return true;
    }
}
