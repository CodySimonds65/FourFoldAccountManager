using System.IO;

namespace FourFoldAccountManager.Desktop.Updates;

public sealed class UpdateCoordinator
{
    private readonly Version _currentVersion;
    private readonly string _currentExecutablePath;
    private readonly int _currentProcessId;
    private readonly IUpdateReleaseClient _releaseClient;
    private readonly IUpdateDownloader _downloader;
    private readonly IUpdateInstaller _installer;
    private readonly Func<UpdateRelease, Task<bool>> _prompt;
    private readonly Action<string>? _failureNotice;
    private int _hasChecked;

    public UpdateCoordinator(
        Version currentVersion,
        string currentExecutablePath,
        int currentProcessId,
        IUpdateReleaseClient releaseClient,
        IUpdateDownloader downloader,
        IUpdateInstaller installer,
        Func<UpdateRelease, Task<bool>> prompt,
        Action<string>? failureNotice = null)
    {
        _currentVersion = currentVersion;
        _currentExecutablePath = currentExecutablePath;
        _currentProcessId = currentProcessId;
        _releaseClient = releaseClient;
        _downloader = downloader;
        _installer = installer;
        _prompt = prompt;
        _failureNotice = failureNotice;
    }

    public async Task CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _hasChecked, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var release = await _releaseClient.GetLatestAsync(cancellationToken);
            if (release is null || release.Version <= _currentVersion)
            {
                return;
            }

            if (!await _prompt(release))
            {
                return;
            }

            var downloadedPath = await _downloader.DownloadAndVerifyAsync(release, cancellationToken);
            if (downloadedPath is null)
            {
                return;
            }

            var result = _installer.TryStart(downloadedPath, _currentExecutablePath, _currentProcessId);
            if (result != UpdateInstallResult.Started)
            {
                DeleteIfPresent(downloadedPath);
                _failureNotice?.Invoke("The update could not be started. You can download the latest release manually.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _failureNotice?.Invoke("The update could not be installed. You can download the latest release manually.");
        }
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
