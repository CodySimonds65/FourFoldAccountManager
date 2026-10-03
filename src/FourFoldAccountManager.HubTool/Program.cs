using System.Globalization;
using FourFoldAccountManager.Core.Plugins.Hub;

// The plugin hub's command-line tool. The hub repository's workflows run it; it never runs a plugin's code. Its input
// is untrusted: `check` reads an entry file and a repository from a stranger's pull request, so a bad input is refused
// with a reason, never a stack trace. Exit code 0 when all is well, 1 when something is refused, 2 for bad usage.
//   check   --hub <dir> --entry plugins/<id>.json [--catalog <file>] [--work <dir>] [--summary <file>] [--source <dir>]
//   publish --hub <dir> --out <dir> [--catalog <file>] [--work <dir>] [--source <dir>]
// --hub, --entry (check) and --out (publish) have no defaults. --catalog is the catalog that is live now: it must
// exist and be readable, and leaving it out says there is none yet. --source uses a local folder as the repository
// instead of downloading it, for trying the tool by hand.
const long MaximumRepositoryBytes = 50L * 1024 * 1024;

string[] knownOptions = ["--hub", "--entry", "--catalog", "--work", "--summary", "--source", "--out"];
var options = new Dictionary<string, string>(StringComparer.Ordinal);
var wellFormed = args.Length % 2 == 1;
for (var index = 1; wellFormed && index + 1 < args.Length; index += 2)
{
    wellFormed = knownOptions.Contains(args[index]);
    options[args[index]] = args[index + 1];
}

if (args.Length == 0 || args[0] is not ("check" or "publish") || !wellFormed || !options.ContainsKey("--hub") ||
    !options.ContainsKey(args[0] == "check" ? "--entry" : "--out"))
{
    Console.Error.WriteLine(
        "Usage: check --hub <dir> --entry <file> [--catalog <file>] [--work <dir>] [--summary <file>] [--source <dir>]" +
        "  |  publish --hub <dir> --out <dir> [--catalog <file>] [--work <dir>] [--source <dir>]");
    return 2;
}

var hub = options["--hub"];
var work = options.GetValueOrDefault("--work") ?? Path.Combine(Path.GetTempPath(), "fourfold-hub-" + Guid.NewGuid().ToString("N"));
var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
HubCatalog? current = null;
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

try
{
    return await RunAsync();
}
catch (Exception exception)
{
    // Whatever is in the input, the tool says one line and stops: no stack trace, no half-written result.
    Console.Error.WriteLine("hubtool: " + exception.GetType().Name);
    return 1;
}

async Task<int> RunAsync()
{
    http.DefaultRequestHeaders.UserAgent.ParseAdd("FourFoldAccountManager-HubTool");

    // A catalog that is named but can't be used must stop the run: carrying on as if there were none would turn off
    // the version rule and re-list everything from scratch.
    if (options.GetValueOrDefault("--catalog") is { } catalogPath)
    {
        current = File.Exists(catalogPath) ? HubCatalogJson.Parse(File.ReadAllText(catalogPath)) : null;
        if (current is null)
        {
            return Refuse("--catalog must be the live catalog.json: a file that exists and that FourFold can read.");
        }
    }

    // A wrong --hub would read as "no plugins, nothing pulled": a publish would delist every plugin and un-pull every
    // pulled one.
    var removedFile = Path.Combine(hub, "removed.json");
    var pluginsFolder = Path.Combine(hub, "plugins");
    if (!Directory.Exists(pluginsFolder) || !File.Exists(removedFile))
    {
        return Refuse("--hub must be the hub repository's folder, with a plugins folder and a removed.json file in it.");
    }

    var (removed, removedError) = HubEntryReader.ReadRemoved(File.ReadAllText(removedFile));
    if (args[0] == "check")
    {
        var entryFile = Path.Combine(hub, options["--entry"]);
        var fileName = Path.GetFileName(entryFile);
        var (entry, error) = File.Exists(entryFile)
            ? HubEntryReader.Read(fileName, File.ReadAllText(entryFile))
            : (null, "The entry file doesn't exist.");
        error ??= removedError;
        if (error is null && removed!.Any(removal => removal.Id == entry!.Id))
        {
            error = "This plugin is in removed.json. Take it out of that file to list it again.";
        }

        HubCheckResult? result = null;
        if (error is null)
        {
            result = await CheckAsync(entry!);
            error = result.Error;
        }

        var summary = HubSubmission.Summary(fileName, entry, error, result?.Plugin, result?.Package, current);
        Console.WriteLine(summary);
        if (options.GetValueOrDefault("--summary") is { } summaryFile)
        {
            File.AppendAllText(summaryFile, summary + Environment.NewLine);
        }

        return error is null ? 0 : 1;
    }

    var entries = new List<HubEntry>();
    var problems = new List<string>();
    foreach (var file in Directory.GetFiles(pluginsFolder, "*.json"))
    {
        var (entry, error) = HubEntryReader.Read(Path.GetFileName(file), File.ReadAllText(file));
        if (entry is null)
        {
            problems.Add($"plugins/{Path.GetFileName(file)}: {error}");
        }
        else
        {
            entries.Add(entry);
        }
    }

    if (removedError is not null)
    {
        problems.Add(removedError);
    }

    var build = problems.Count > 0
        ? new HubBuild(null, [], problems)
        : HubSubmission.BuildCatalog(entries, removed!, current, entry => CheckAsync(entry).GetAwaiter().GetResult());
    foreach (var problem in build.Errors)
    {
        Console.Error.WriteLine(problem);
    }

    if (build.Catalog is null)
    {
        return 1;
    }

    var outFolder = options["--out"];
    Directory.CreateDirectory(outFolder);
    foreach (var (name, bytes) in build.Packages)
    {
        File.WriteAllBytes(Path.Combine(outFolder, name), bytes);
    }

    File.WriteAllText(Path.Combine(outFolder, "catalog.json"), HubCatalogJson.Write(build.Catalog));
    Console.WriteLine(
        $"{build.Catalog.Plugins.Count} plugin(s) listed, {build.Packages.Count} new package(s), {build.Catalog.Removed.Count} removed.");
    return 0;
}

int Refuse(string reason)
{
    Console.Error.WriteLine(reason);
    return 1;
}

// Gets the entry's repository at its commit onto disk, then checks the plugin in it.
async Task<HubCheckResult> CheckAsync(HubEntry entry)
{
    if (options.GetValueOrDefault("--source") is { } source)
    {
        return HubSubmission.Check(entry, source, current, today);
    }

    var bytes = await HubHttp.DownloadAsync(
        http, HubAddresses.RepositoryArchive(entry.Repository, entry.Commit), MaximumRepositoryBytes, CancellationToken.None);
    if (bytes is null)
    {
        return new HubCheckResult(
            "The repository couldn't be downloaded at that commit. It must be public and under 50 MB.", null, null);
    }

    var folder = Path.Combine(work, entry.Id);
    try
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        using var stream = new MemoryStream(bytes);
        SafeArchive.Extract(
            stream, folder, maximumFiles: 20_000, maximumBytes: 200L * 1024 * 1024, stripTopFolder: true,
            skipLinksAndOddNames: true);
    }
    catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
    {
        // The message is shown only for an InvalidDataException, whose text is fixed; any other exception's text can
        // carry the path of a file the stranger named.
        return new HubCheckResult(
            "The repository's archive couldn't be unpacked" + (exception is InvalidDataException ? ": " + exception.Message : "."),
            null, null);
    }

    return HubSubmission.Check(entry, folder, current, today);
}
