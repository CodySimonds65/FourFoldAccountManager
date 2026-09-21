namespace FourFoldAccountManager.Desktop.Updates;

public sealed record UpdateAsset(string Name, Uri DownloadUrl, long Size);

public sealed record UpdateRelease(
    Version Version,
    string TagName,
    string Name,
    string Notes,
    IReadOnlyList<UpdateAsset> Assets);

public enum UpdateInstallResult
{
    Started,
    UnsupportedHost,
    Failed
}

public interface IUpdateReleaseClient
{
    Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken);
}

public interface IUpdateDownloader
{
    Task<string?> DownloadAndVerifyAsync(UpdateRelease release, CancellationToken cancellationToken);
}

public interface IUpdateInstaller
{
    UpdateInstallResult TryStart(string verifiedUpdatePath, string currentExecutablePath, int parentProcessId);
}
