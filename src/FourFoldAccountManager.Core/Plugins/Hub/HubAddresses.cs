namespace FourFoldAccountManager.Core.Plugins.Hub;

// Where the hub lives. Every address is built here from fixed parts plus an id, a commit and (for an archive) a
// repository, and all three are checked here, the last stop before a request: a bad one throws ArgumentException.
// Nothing is ever taken from the catalog as a URL.
public static class HubAddresses
{
    public const string Repository = "CodySimonds65/FourFoldAccountManager-plugin-hub";

    private const string ReleaseBase = "https://github.com/" + Repository + "/releases/download/catalog/";

    public static Uri Catalog { get; } = new(ReleaseBase + "catalog.json");

    public static string PackageName(string id, string commit) =>
        PluginManifestReader.IsValidId(id) && HubCatalogJson.IsCommit(commit)
            ? $"{id}-{commit}.zip"
            : throw new ArgumentException("The id must be a valid plugin id and the commit a full lowercase commit id.");

    public static Uri Package(string id, string commit) => new(ReleaseBase + PackageName(id, commit));

    // GitHub's zip of a repository at one commit. repository is https://github.com/<owner>/<repo>.
    public static Uri RepositoryArchive(Uri repository, string commit) =>
        HubCatalogJson.TryParseRepository(repository.AbsoluteUri, out _) && HubCatalogJson.IsCommit(commit)
            ? new Uri($"https://codeload.github.com{repository.AbsolutePath}/zip/{commit}")
            : throw new ArgumentException(
                "The repository must be https://github.com/<owner>/<repo> and the commit a full lowercase commit id.");
}
