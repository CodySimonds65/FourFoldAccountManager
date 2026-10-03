namespace FourFoldAccountManager.Core.Plugins.Hub;

// Where the hub lives. Every address is built here from fixed parts plus an id and a commit that were already
// validated (PluginManifestReader.IsValidId, HubCatalogJson.IsCommit); nothing is ever taken from the catalog as a URL.
public static class HubAddresses
{
    public const string Repository = "CodySimonds65/FourFoldAccountManager-plugin-hub";

    private const string ReleaseBase = "https://github.com/" + Repository + "/releases/download/catalog/";

    public static Uri Catalog { get; } = new(ReleaseBase + "catalog.json");

    public static string PackageName(string id, string commit) => $"{id}-{commit}.zip";

    public static Uri Package(string id, string commit) => new(ReleaseBase + PackageName(id, commit));

    // GitHub's zip of a repository at one commit. repository is https://github.com/<owner>/<repo>.
    public static Uri RepositoryArchive(Uri repository, string commit) =>
        new($"https://codeload.github.com{repository.AbsolutePath}/zip/{commit}");
}
