using System.IO.Compression;
using System.Text;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Core.Plugins.Hub;

public sealed record HubCheckResult(string? Error, HubPlugin? Plugin, byte[]? Package);

// The catalog a publish run would upload, the packages that are new in it, and what stopped it.
public sealed record HubBuild(
    HubCatalog? Catalog, IReadOnlyList<(string Name, byte[] Bytes)> Packages, IReadOnlyList<string> Errors);

// What the hub repository's workflows check before a plugin is listed. The plugin's code is never run.
public static class HubSubmission
{
    // Checks one entry against its repository, unpacked at the entry's commit in repositoryFolder, and builds its
    // package. today is the review date recorded for it (yyyy-MM-dd).
    public static HubCheckResult Check(HubEntry entry, string repositoryFolder, HubCatalog? current, string today)
    {
        var folder = repositoryFolder;
        if (entry.Path.Length > 0 &&
            (!PluginPaths.TryResolveInside(repositoryFolder, entry.Path, out folder) || !Directory.Exists(folder)))
        {
            return Fail($"The repository has no folder \"{entry.Path}\" at that commit.");
        }

        var read = PluginManifestReader.Read(folder);
        if (read.Manifest is not { } manifest)
        {
            return Fail(read.Error ?? "plugin.json couldn't be read.");
        }

        if (manifest.Id != entry.Id)
        {
            return Fail($"The file is named for \"{entry.Id}\", but plugin.json says \"{manifest.Id}\".");
        }

        if (entry.AnySite && !manifest.AnySite)
        {
            return Fail("\"anySite\" is true in the entry, but the plugin's plugin.json doesn't ask for it.");
        }

        var listed = current?.Plugins.FirstOrDefault(plugin => plugin.Id == entry.Id);
        if (listed is not null && listed.Commit != entry.Commit && !IsNewer(manifest.Version, listed.Version))
        {
            return Fail(
                $"An update must raise the version: the hub lists {listed.Version} and plugin.json says {manifest.Version}.");
        }

        byte[] package;
        try
        {
            package = PluginPackage.Build(folder);
        }
        catch (InvalidDataException exception)
        {
            return Fail(exception.Message);
        }

        if (package.LongLength > HubLimits.MaximumPackageBytes)
        {
            return Fail("The package is too large.");
        }

        return new HubCheckResult(
            null,
            new HubPlugin(
                manifest.Id, manifest.Name, manifest.ShortLabel, manifest.Version, manifest.Author, manifest.Description,
                manifest.Sites, entry.AnySite && manifest.AnySite,
                manifest.Cards.Select(card => new HubCard(card.Name, card.Scope)).ToArray(),
                entry.Repository, entry.Commit, today, package.LongLength, PluginPackage.Sha256(package)),
            package);

        static HubCheckResult Fail(string reason) => new(reason, null, null);
    }

    // Builds the catalog for every entry. An entry whose commit and clearance are unchanged keeps its record and its
    // published package, so a listed plugin survives its author deleting the repository; only new and changed entries
    // go through check. One failure means no catalog.
    public static HubBuild BuildCatalog(
        IReadOnlyList<HubEntry> entries,
        IReadOnlyList<HubRemoval> removed,
        HubCatalog? current,
        Func<HubEntry, HubCheckResult> check)
    {
        var errors = new List<string>();
        var plugins = new List<HubPlugin>();
        var packages = new List<(string Name, byte[] Bytes)>();
        foreach (var entry in entries.OrderBy(entry => entry.Id, StringComparer.Ordinal))
        {
            if (removed.Any(removal => removal.Id == entry.Id))
            {
                errors.Add($"{entry.Id}: a plugin can't be both listed and in removed.json.");
                continue;
            }

            if (current?.Plugins.FirstOrDefault(plugin => plugin.Id == entry.Id) is { } listed &&
                listed.Commit == entry.Commit && listed.AnySite == entry.AnySite)
            {
                plugins.Add(listed);
                continue;
            }

            var result = check(entry);
            if (result.Error is not null)
            {
                errors.Add($"{entry.Id}: {result.Error}");
                continue;
            }

            plugins.Add(result.Plugin!);
            packages.Add((HubAddresses.PackageName(entry.Id, entry.Commit), result.Package!));
        }

        return new HubBuild(errors.Count == 0 ? new HubCatalog(plugins, removed) : null, packages, errors);
    }

    // What a reviewer needs to see for one entry, as Markdown for the check's summary page.
    public static string Summary(
        string fileName, HubEntry? entry, string? error, HubPlugin? plugin, byte[]? package, HubCatalog? current)
    {
        var text = new StringBuilder().AppendLine($"### {fileName}").AppendLine();
        if (error is not null || entry is null || plugin is null || package is null)
        {
            return text.AppendLine($"**Not accepted:** {error}").ToString();
        }

        var contact = plugin.AnySite ? "any website (cleared by this entry)"
            : plugin.Sites.Count == 0 ? "no websites"
            : string.Join(", ", plugin.Sites.Select(PluginNetworkPolicy.SiteLabel));
        var cards = plugin.Cards.Count == 0
            ? "none"
            : string.Join(", ", plugin.Cards.Select(card =>
                $"{Cell(card.Name)} ({(card.Scope == OverlayAddOnScope.Account ? "per account" : "global")})"));
        text.AppendLine("| | |")
            .AppendLine("|---|---|")
            .AppendLine($"| Name | {Cell(plugin.Name)} ({Cell(plugin.ShortLabel)}) |")
            .AppendLine($"| Author | {Cell(plugin.Author)} |")
            .AppendLine($"| Version | {plugin.Version} |")
            .AppendLine($"| Description | {Cell(plugin.Description)} |")
            .AppendLine($"| Can contact | {contact} |")
            .AppendLine($"| Cards | {cards} |")
            .AppendLine($"| Package | {plugin.Size} bytes |")
            .AppendLine();

        var listed = current?.Plugins.FirstOrDefault(other => other.Id == plugin.Id);
        var repository = entry.Repository.AbsoluteUri;
        text.AppendLine(listed is null
            ? $"New plugin. Review the code: {repository}/tree/{entry.Commit}{(entry.Path.Length > 0 ? "/" + entry.Path : "")}"
            : $"Update from {listed.Version}. Review the changes: {repository}/compare/{listed.Commit}...{entry.Commit}");
        text.AppendLine().AppendLine("Files in the package:").AppendLine();
        using var archive = new ZipArchive(new MemoryStream(package));
        foreach (var file in archive.Entries)
        {
            text.AppendLine($"- `{file.FullName}` ({file.Length} bytes)");
        }

        return text.ToString();

        // Plugin text goes into a Markdown table, so a pipe or a line break in it must not break the row.
        static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private static bool IsNewer(string version, string than) =>
        System.Version.TryParse(version, out var left) && System.Version.TryParse(than, out var right) && left > right;
}
