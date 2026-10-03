using System.IO;

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

internal static class UpdateFiles
{
    // File.Delete is already a no-op when the file is missing.
    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
