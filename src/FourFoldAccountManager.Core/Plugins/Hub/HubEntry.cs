using System.Text.Json;
using System.Text.RegularExpressions;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// One file in the hub repository's plugins folder: which repository and commit to list, and whether the maintainers
// cleared the plugin for any website. The plugin's id is the file's name.
public sealed record HubEntry(string Id, Uri Repository, string Commit, string Path, bool AnySite);

public static partial class HubEntryReader
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*\z")]
    private static partial Regex PathRegex();

    // fileName is the entry's file name, "<plugin id>.json". Every rejection carries a reason an author can act on.
    public static (HubEntry? Entry, string? Error) Read(string fileName, string json)
    {
        var id = fileName.EndsWith(".json", StringComparison.Ordinal) ? fileName[..^5] : null;
        if (!PluginManifestReader.IsValidId(id))
        {
            return (null, "The file must be named <plugin id>.json, for example cody.goal-tracker.json.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "The entry must be a JSON object.");
            }

            if (!HubCatalogJson.TryParseRepository(PluginManifestReader.Text(root, "repository"), out var repository))
            {
                return (null, "\"repository\" must look like https://github.com/owner/repo.");
            }

            var commit = PluginManifestReader.Text(root, "commit");
            if (!HubCatalogJson.IsCommit(commit))
            {
                return (null, "\"commit\" must be a full 40-character commit in lowercase, not a branch or a tag.");
            }

            var path = PluginManifestReader.Text(root, "path") ?? string.Empty;
            if (path.Length > 0 && (!PathRegex().IsMatch(path) || path.Split('/').Any(part => part is "." or "..")))
            {
                return (null, "\"path\" must be a folder inside the repository, written with forward slashes.");
            }

            var hasAnySite = root.TryGetProperty("anySite", out var anySite);
            if (hasAnySite && anySite.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return (null, "\"anySite\" must be true or false.");
            }

            return (new HubEntry(id, repository, commit, path, hasAnySite && anySite.ValueKind == JsonValueKind.True), null);
        }
        catch (JsonException)
        {
            return (null, "The entry isn't valid JSON.");
        }
    }

    public static (IReadOnlyList<HubRemoval>? Removed, string? Error) ReadRemoved(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (null, "removed.json must be a list.");
            }

            var result = new List<HubRemoval>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var isObject = item.ValueKind == JsonValueKind.Object;
                var id = isObject ? PluginManifestReader.Text(item, "id") : null;
                var reason = isObject ? PluginManifestReader.Text(item, "reason") : null;
                if (!PluginManifestReader.IsValidId(id) || reason is null || reason.Length is 0 or > 200 ||
                    PluginText.HasUnsafeCharacter(reason) || result.Any(other => other.Id == id))
                {
                    return (null, "Each removed.json line needs a plugin \"id\" (once) and a \"reason\" of 1 to 200 characters.");
                }

                result.Add(new HubRemoval(id, reason));
            }

            return (result, null);
        }
        catch (JsonException)
        {
            return (null, "removed.json isn't valid JSON.");
        }
    }
}
