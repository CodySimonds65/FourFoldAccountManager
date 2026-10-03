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
    private const int MaximumListedFiles = 100;

    // Every character Markdown could act on in the summary. "$" is here because GitHub draws $...$ as maths, which
    // could restyle the text between two values on one line.
    private const string MarkdownCharacters = "\\`*_[]<>&|~!()#$";

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

        // The reader accepts any digits, but a number past 2,147,483,647 can't be compared, so no update to it could
        // ever pass the "must raise" rule below.
        if (!System.Version.TryParse(manifest.Version, out _))
        {
            return Fail("Each number in the version must be at most 2147483647, for example 1.4.0.");
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail("The plugin's files couldn't be read.");
        }

        if (package.LongLength > HubLimits.MaximumPackageBytes)
        {
            return Fail("The package is too large.");
        }

        if (Unpackable(package, manifest) is { } unpackProblem)
        {
            return Fail(unpackProblem);
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

    // The package is built from the author's folder, but what users get is what the app unpacks from it. Unpacks it the
    // way the app does, into a throwaway folder, and returns why it wouldn't install as the same plugin, or null. A
    // manifest whose panel is a dot-file, say, passes the folder's own check and then has no panel in the package.
    private static string? Unpackable(byte[] package, PluginManifest manifest)
    {
        var staging = Path.Combine(Path.GetTempPath(), "fourfold-hub-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            PluginPackage.Extract(package, staging);
            var unpacked = PluginManifestReader.Read(staging);
            if (unpacked.Manifest is not { } installed)
            {
                return "Once packaged, the plugin can't be installed: " + unpacked.Error;
            }

            return installed.Id == manifest.Id && installed.Version == manifest.Version
                ? null
                : "Once packaged, the plugin isn't the same id and version.";
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return "The package can't be unpacked: " +
                   (exception is InvalidDataException ? exception.Message : "a file couldn't be written.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover folder in the temporary folder costs nothing; it must not hide the result.
            }
        }
    }

    // Builds the catalog for every entry. An entry whose repository, commit and clearance are unchanged keeps its
    // record and its published package, so a listed plugin survives its author deleting the repository; only new and
    // changed entries go through check. One failure means no catalog, and then no packages either.
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
                listed.Commit == entry.Commit && listed.AnySite == entry.AnySite && listed.Repository == entry.Repository)
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

        if (errors.Count > 0)
        {
            return new HubBuild(null, [], errors);
        }

        // The app reads at most this much of a catalog and refuses a larger one, which would leave the hub dead for
        // every user, so a catalog it would refuse is never built. The app counts characters and the download is capped
        // in bytes, so both are checked.
        var catalog = new HubCatalog(plugins, removed);
        if (plugins.Count > HubLimits.MaximumCatalogPlugins)
        {
            errors.Add($"The catalog would list {plugins.Count} plugins, and FourFold reads at most {HubLimits.MaximumCatalogPlugins}.");
        }
        else
        {
            var json = HubCatalogJson.Write(catalog);
            if (json.Length > HubLimits.MaximumCatalogBytes || Encoding.UTF8.GetByteCount(json) > HubLimits.MaximumCatalogBytes)
            {
                errors.Add($"The catalog would be larger than the {HubLimits.MaximumCatalogBytes} bytes FourFold reads.");
            }
        }

        return errors.Count == 0 ? new HubBuild(catalog, packages, errors) : new HubBuild(null, [], errors);
    }

    // What a reviewer needs to see for one entry, as Markdown for the check's summary page. A maintainer reads this
    // before merging, and most of it is a stranger's text: Plain makes each such value harmless, and every line starts
    // with text written here, never with a stranger's value.
    public static string Summary(
        string fileName, HubEntry? entry, string? error, HubPlugin? plugin, byte[]? package, HubCatalog? current)
    {
        var text = new StringBuilder().AppendLine($"### {Plain(fileName)}").AppendLine();
        if (error is not null || entry is null || plugin is null || package is null)
        {
            return text.AppendLine($"**Not accepted:** {Plain(error ?? "The entry couldn't be checked.", 500)}").ToString();
        }

        if (plugin.AnySite)
        {
            text.AppendLine("**WARNING: this entry asks for ANY WEBSITE access. Merging it grants that.**").AppendLine();
        }

        var contact = plugin.AnySite ? "any website (requested by this entry)"
            : plugin.Sites.Count == 0 ? "no websites"
            : string.Join(", ", plugin.Sites.Select(site => Site(PluginNetworkPolicy.SiteLabel(site))));
        var cards = plugin.Cards.Count == 0
            ? "none"
            : string.Join(", ", plugin.Cards.Select(card =>
                $"{Plain(card.Name)} ({(card.Scope == OverlayAddOnScope.Account ? "per account" : "global")})"));
        text.AppendLine("| | |")
            .AppendLine("|---|---|")
            .AppendLine($"| Name | {Plain(plugin.Name)} ({Plain(plugin.ShortLabel)}) |")
            .AppendLine($"| Author | {Plain(plugin.Author)} |")
            .AppendLine($"| Version | {plugin.Version} |")
            .AppendLine($"| Description | {Plain(plugin.Description, 200)} |")
            .AppendLine($"| Can contact | {contact} |")
            .AppendLine($"| Cards | {cards} |")
            .AppendLine($"| Package | {plugin.Size} bytes |")
            .AppendLine();

        var listed = current?.Plugins.FirstOrDefault(other => other.Id == plugin.Id);
        var repository = entry.Repository.AbsoluteUri;
        var sameRepository = listed is not null && listed.Repository == entry.Repository;
        text.AppendLine(sameRepository
            ? $"Update from {listed!.Version}. Review the changes: {repository}/compare/{listed.Commit}...{entry.Commit}"
            : $"{(listed is null ? "New plugin" : $"Update from {listed.Version}, from another repository")}. " +
              $"Review the code: {repository}/tree/{entry.Commit}{(entry.Path.Length > 0 ? "/" + entry.Path : "")}");
        if (listed is not null)
        {
            // What an update escalates is what a reviewer must not miss, so it is spelled out, not left to the table.
            var shown = plugin.Sites.Select(PluginNetworkPolicy.SiteLabel).ToArray();
            var before = listed.Sites.Select(PluginNetworkPolicy.SiteLabel).ToArray();
            var changes = new List<string>();
            if (plugin.AnySite != listed.AnySite)
            {
                changes.Add(plugin.AnySite ? "Any-website access gained." : "Any-website access dropped.");
            }

            if (shown.Except(before).ToArray() is { Length: > 0 } added)
            {
                changes.Add("Sites added: " + string.Join(", ", added.Select(Site)) + ".");
            }

            if (before.Except(shown).ToArray() is { Length: > 0 } removed)
            {
                changes.Add("Sites removed: " + string.Join(", ", removed.Select(Site)) + ".");
            }

            if (listed.Repository != plugin.Repository)
            {
                changes.Add($"Repository changed from {listed.Repository.AbsoluteUri} to {repository}.");
            }

            if (listed.Author != plugin.Author)
            {
                changes.Add($"Author changed from {Plain(listed.Author)} to {Plain(plugin.Author)}.");
            }

            text.AppendLine();
            if (changes.Count == 0)
            {
                text.AppendLine("No change to access, sites, repository or author.");
            }
            else
            {
                text.AppendLine("Changed:").AppendLine();
                foreach (var change in changes)
                {
                    text.AppendLine("- " + change);
                }
            }
        }

        text.AppendLine().AppendLine("Files in the package:").AppendLine();
        using var archive = new ZipArchive(new MemoryStream(package));
        foreach (var file in archive.Entries.Take(MaximumListedFiles))
        {
            text.AppendLine($"- File: {Plain(file.FullName)} ({file.Length} bytes)");
        }

        if (archive.Entries.Count > MaximumListedFiles)
        {
            text.AppendLine($"- \u2026and {archive.Entries.Count - MaximumListedFiles} more");
        }

        return text.ToString();
    }

    // A site's label is a host name, but it comes from a stranger's plugin.json, so it is text like the rest.
    private static string Site(string label) => Plain(label, 300);

    // Text from a plugin, an entry or a repository, made safe to put in the summary, which GitHub draws as Markdown:
    // anything that could start a new line or hide text (control, formatting and separator characters) becomes "?",
    // every character Markdown could act on gets a backslash, and a long value is cut and ends in "...".
    public static string Plain(string value, int maximum = 100)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString();
            var piece = PluginText.HasUnsafeCharacter(text) ? "?"
                : rune.IsBmp && MarkdownCharacters.Contains((char)rune.Value) ? "\\" + text
                : text;
            if (result.Length + piece.Length > maximum)
            {
                return result.Append('\u2026').ToString();
            }

            result.Append(piece);
        }

        return result.ToString();
    }

    private static bool IsNewer(string version, string than) =>
        System.Version.TryParse(version, out var left) && System.Version.TryParse(than, out var right) && left > right;
}
