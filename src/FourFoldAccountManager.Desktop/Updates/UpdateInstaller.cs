using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FourFoldAccountManager.Desktop.Updates;

public sealed class UpdateInstaller
{
    private const string ApplyUpdateArgument = "--apply-update";
    private const string HelperNamePrefix = "FourFoldAccountManager-updater-";

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

        var helperPath = Path.Combine(Path.GetTempPath(), $"{HelperNamePrefix}{Guid.NewGuid():N}.exe");
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
                UpdateFiles.TryDelete(helperPath);
                return UpdateInstallResult.Failed;
            }

            return UpdateInstallResult.Started;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            UpdateFiles.TryDelete(helperPath);
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

    // A helper cannot delete its own running exe, so the next normal start clears what earlier updates left.
    // A helper that is still running keeps its exe locked, and that keeps its extracted native libraries too.
    public static void DeleteStaleHelpers(string? temporaryDirectory = null)
    {
        var directory = temporaryDirectory ?? Path.GetTempPath();
        try
        {
            foreach (var helperPath in Directory.EnumerateFiles(directory, HelperNamePrefix + "*.exe"))
            {
                UpdateFiles.TryDelete(helperPath);
            }

            // The single-file build extracts its native libraries into one folder per exe name.
            var extractionRoot = Path.Combine(directory, ".net");
            if (!Directory.Exists(extractionRoot))
            {
                return;
            }

            foreach (var folder in Directory.EnumerateDirectories(extractionRoot, HelperNamePrefix + "*"))
            {
                if (File.Exists(Path.Combine(directory, Path.GetFileName(folder) + ".exe")))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
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
            UpdateFiles.TryDelete(stagingPath);
            UpdateFiles.TryDelete(backupPath);
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

            UpdateFiles.TryDelete(backupPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreBackup(targetExecutablePath, backupPath);
            return false;
        }
        finally
        {
            UpdateFiles.TryDelete(stagingPath);
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }

        var replaced = ReplaceTargetFile(sourcePath, targetPath);
        if (replaced || File.Exists(targetPath))
        {
            TryLaunch(targetPath);
        }

        UpdateFiles.TryDelete(sourcePath);
        UpdateFiles.TryDelete(Environment.ProcessPath);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryLaunch(string targetPath)
    {
        try
        {
            // Not through the shell: an exe that still carries its browser download mark makes the shell ask
            // "Open File - Security Warning" in this hidden helper, which then waits forever.
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false
            })?.Dispose();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
