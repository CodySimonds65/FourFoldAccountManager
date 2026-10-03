using System.Globalization;
using FourFoldAccountManager.Core.Plugins.Hub;

// The plugin hub's command-line tool. The hub repository's workflows run it; it never runs a plugin's code.
//   check   --hub <dir> --entry plugins/<id>.json [--catalog <file>] [--work <dir>] [--summary <file>] [--source <dir>]
//   publish --hub <dir> --out <dir> [--catalog <file>] [--work <dir>] [--source <dir>]
// --catalog is the catalog that is live now, when there is one. --source uses a local folder as the repository
// instead of downloading it, for trying the tool by hand.
const long MaximumRepositoryBytes = 50L * 1024 * 1024;

if (args.Length == 0 || args[0] is not ("check" or "publish") || args.Length % 2 == 0)
{
    Console.Error.WriteLine("Usage: check --hub <dir> --entry <file> [...]  |  publish --hub <dir> --out <dir> [...]");
    return 2;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 1; index + 1 < args.Length; index += 2)
{
    options[args[index]] = args[index + 1];
}

var hub = Option("--hub") ?? ".";
var work = Option("--work") ?? Path.Combine(Path.GetTempPath(), "fourfold-hub-" + Guid.NewGuid().ToString("N"));
var current = Option("--catalog") is { } catalogPath && File.Exists(catalogPath)
    ? HubCatalogJson.Parse(File.ReadAllText(catalogPath))
    : null;
var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("FourFoldAccountManager-HubTool");

if (args[0] == "check")
{
    var entryFile = Path.Combine(hub, Option("--entry") ?? string.Empty);
    var fileName = Path.GetFileName(entryFile);
    var (entry, error) = File.Exists(entryFile)
        ? HubEntryReader.Read(fileName, File.ReadAllText(entryFile))
        : (null, "The entry file doesn't exist.");
    var (removed, removedError) = ReadRemoved();
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
    if (Option("--summary") is { } summaryFile)
    {
        File.AppendAllText(summaryFile, summary + Environment.NewLine);
    }

    return error is null ? 0 : 1;
}

var outFolder = Option("--out") ?? "out";
Directory.CreateDirectory(outFolder);
var entries = new List<HubEntry>();
var problems = new List<string>();
var pluginsFolder = Path.Combine(hub, "plugins");
foreach (var file in Directory.Exists(pluginsFolder) ? Directory.GetFiles(pluginsFolder, "*.json") : [])
{
    var (entry, error) = HubEntryReader.Read(Path.GetFileName(file), File.ReadAllText(file));
    if (entry is null)
    {
        problems.Add($"{Path.GetFileName(file)}: {error}");
    }
    else
    {
        entries.Add(entry);
    }
}

var (pulled, pulledError) = ReadRemoved();
if (pulledError is not null)
{
    problems.Add(pulledError);
}

var build = problems.Count > 0
    ? new HubBuild(null, [], problems)
    : HubSubmission.BuildCatalog(entries, pulled!, current, entry => CheckAsync(entry).GetAwaiter().GetResult());
foreach (var problem in build.Errors)
{
    Console.Error.WriteLine(problem);
}

if (build.Catalog is null)
{
    return 1;
}

foreach (var (name, bytes) in build.Packages)
{
    File.WriteAllBytes(Path.Combine(outFolder, name), bytes);
}

File.WriteAllText(Path.Combine(outFolder, "catalog.json"), HubCatalogJson.Write(build.Catalog));
Console.WriteLine(
    $"{build.Catalog.Plugins.Count} plugin(s) listed, {build.Packages.Count} new package(s), {build.Catalog.Removed.Count} removed.");
return 0;

string? Option(string name) => options.GetValueOrDefault(name);

(IReadOnlyList<HubRemoval>? Removed, string? Error) ReadRemoved()
{
    var path = Path.Combine(hub, "removed.json");
    return File.Exists(path) ? HubEntryReader.ReadRemoved(File.ReadAllText(path)) : ([], null);
}

// Gets the entry's repository at its commit onto disk, then checks the plugin in it.
async Task<HubCheckResult> CheckAsync(HubEntry entry)
{
    if (Option("--source") is { } source)
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
        SafeArchive.Extract(stream, folder, maximumFiles: 20_000, maximumBytes: 200L * 1024 * 1024, stripTopFolder: true);
    }
    catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
    {
        return new HubCheckResult("The repository's archive couldn't be unpacked: " + exception.Message, null, null);
    }

    return HubSubmission.Check(entry, folder, current, today);
}
