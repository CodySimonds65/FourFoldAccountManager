using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FourFoldAccountManager.Desktop.Updates;

public sealed class UpdateInstaller : IUpdateInstaller
{
    private const string ApplyUpdateArgument = "--apply-update";

    public UpdateInstallResult TryStart(string verifiedUpdatePath, string currentExecutablePath, int parentProcessId)
    {
        if (string.Equals(Path.GetFileName(currentExecutablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateInstallResult.UnsupportedHost;
        }

        if (parentProcessId <= 0 ||
            !Path.IsPathFullyQualified(verifiedUpdatePath) ||
            !Path.IsPathFullyQualified(currentExecutablePath) ||
            !File.Exists(verifiedUpdatePath) ||
            !File.Exists(currentExecutablePath) ||
            !string.Equals(Path.GetExtension(currentExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            PathsAreEqual(verifiedUpdatePath, currentExecutablePath))
        {
            return UpdateInstallResult.Failed;
        }

        var helperPath = Path.Combine(Path.GetTempPath(), $"FourFoldAccountManager-updater-{Guid.NewGuid():N}.exe");
        try
        {
            File.Copy(currentExecutablePath, helperPath, overwrite: false);
            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(ApplyUpdateArgument);
            startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(verifiedUpdatePath);
            startInfo.ArgumentList.Add(currentExecutablePath);

            using var helper = Process.Start(startInfo);
            if (helper is null)
            {
                DeleteIfPresent(helperPath);
                return UpdateInstallResult.Failed;
            }

            return UpdateInstallResult.Started;
        }
        catch (IOException)
        {
            DeleteIfPresent(helperPath);
            return UpdateInstallResult.Failed;
        }
        catch (UnauthorizedAccessException)
        {
            DeleteIfPresent(helperPath);
            return UpdateInstallResult.Failed;
        }
        catch (InvalidOperationException)
        {
            DeleteIfPresent(helperPath);
            return UpdateInstallResult.Failed;
        }
    }

    public static bool TryRunReplacementMode(IReadOnlyList<string> args)
    {
        if (args.Count != 4 || !string.Equals(args[0], ApplyUpdateArgument, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentProcessId) ||
            parentProcessId <= 0 ||
            !Path.IsPathFullyQualified(args[2]) ||
            !Path.IsPathFullyQualified(args[3]) ||
            !File.Exists(args[2]) ||
            !File.Exists(args[3]) ||
            PathsAreEqual(args[2], args[3]))
        {
            return false;
        }

        new UpdateInstaller().WaitForParentAndReplace(parentProcessId, args[2], args[3]);
        return true;
    }

    public static bool ReplaceTargetFile(string verifiedUpdatePath, string targetExecutablePath)
    {
        if (!Path.IsPathFullyQualified(verifiedUpdatePath) ||
            !Path.IsPathFullyQualified(targetExecutablePath) ||
            !File.Exists(verifiedUpdatePath) ||
            PathsAreEqual(verifiedUpdatePath, targetExecutablePath))
        {
            return false;
        }

        var stagingPath = targetExecutablePath + ".new";
        var backupPath = targetExecutablePath + ".backup";
        try
        {
            DeleteIfPresent(stagingPath);
            DeleteIfPresent(backupPath);
            File.Copy(verifiedUpdatePath, stagingPath, overwrite: false);

            if (File.Exists(targetExecutablePath))
            {
                try
                {
                    File.Replace(stagingPath, targetExecutablePath, backupPath, ignoreMetadataErrors: true);
                }
                catch (Exception exception) when (exception is IOException or PlatformNotSupportedException or NotSupportedException)
                {
                    File.Copy(targetExecutablePath, backupPath, overwrite: true);
                    File.Move(stagingPath, targetExecutablePath, overwrite: true);
                }
            }
            else
            {
                File.Move(stagingPath, targetExecutablePath, overwrite: false);
            }

            DeleteIfPresent(backupPath);
            return true;
        }
        catch (IOException)
        {
            RestoreBackup(targetExecutablePath, backupPath);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            RestoreBackup(targetExecutablePath, backupPath);
            return false;
        }
        finally
        {
            DeleteIfPresent(stagingPath);
        }
    }

    private void WaitForParentAndReplace(int parentProcessId, string sourcePath, string targetPath)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.HasExited)
            {
                parent.WaitForExit();
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        var replaced = ReplaceTargetFile(sourcePath, targetPath);
        if (!replaced && File.Exists(targetPath))
        {
            TryLaunch(targetPath);
        }
        else if (replaced)
        {
            TryLaunch(targetPath);
        }

        DeleteIfPresent(sourcePath);
        DeleteIfPresent(Environment.ProcessPath);
    }

    private static bool PathsAreEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void RestoreBackup(string targetPath, string backupPath)
    {
        try
        {
            if (!File.Exists(targetPath) && File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath, overwrite: false);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryLaunch(string targetPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void DeleteIfPresent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

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
