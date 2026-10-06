using System.Diagnostics;
using System.IO;
using FourFoldAccountManager.Desktop.Updates;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Updates;

public sealed class UpdateInstallerTests
{
    // An exe first downloaded with a browser carries a Zone.Identifier stream, and File.Replace keeps it on
    // every update. Relaunching that exe through the shell raises "Open File - Security Warning" inside the
    // hidden helper, where nobody can answer it, so the helper never exits and the app never reopens.
    [Fact]
    public void HelperExitsAfterReplacingAnExeThatCarriesTheMarkOfTheWeb()
    {
        var directory = Directory.CreateTempSubdirectory("FourFoldUpdateInstallerTests-").FullName;
        var quietExe = Path.Combine(Environment.SystemDirectory, "rundll32.exe");
        var update = Path.Combine(directory, "update.download");
        var target = Path.Combine(directory, "Fourfold.exe");
        File.Copy(quietExe, update);
        File.Copy(quietExe, target);
        File.WriteAllText(target + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "FourFoldAccountManager.Desktop.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add(int.MaxValue.ToString());
        startInfo.ArgumentList.Add(update);
        startInfo.ArgumentList.Add(target);

        using var helper = Process.Start(startInfo)!;
        var exited = helper.WaitForExit(TimeSpan.FromSeconds(30));
        if (!exited)
        {
            helper.Kill();
            helper.WaitForExit();
        }

        Assert.True(exited, "The update helper was still running 30 seconds after replacing the exe.");
        Assert.False(File.Exists(update));
        Directory.Delete(directory, recursive: true);
    }

    // The cleanup deletes from the user's temp folder, so it must take only finished helpers: one that is
    // still running keeps its exe locked and must keep the native libraries it extracted as well.
    [Fact]
    public void DeleteStaleHelpersRemovesFinishedHelpersAndLeavesRunningOnesAndOtherFiles()
    {
        var directory = Directory.CreateTempSubdirectory("FourFoldUpdateInstallerTests-").FullName;
        var staleExe = CreateHelper(directory, "stale");
        var runningExe = CreateHelper(directory, "running");
        var orphanFolder = Directory.CreateDirectory(Path.Combine(directory, ".net", "FourFoldAccountManager-updater-orphan")).FullName;
        var otherFile = Path.Combine(directory, "FourFoldAccountManager-notes.exe");
        var otherFolder = Directory.CreateDirectory(Path.Combine(directory, ".net", "SomeOtherApp")).FullName;
        File.WriteAllText(otherFile, "keep");

        using (File.Open(runningExe, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            UpdateInstaller.DeleteStaleHelpers(directory);
        }

        Assert.False(File.Exists(staleExe));
        Assert.False(Directory.Exists(ExtractionFolder(directory, "stale")));
        Assert.False(Directory.Exists(orphanFolder));
        Assert.True(File.Exists(runningExe));
        Assert.True(File.Exists(Path.Combine(ExtractionFolder(directory, "running"), "native.dll")));
        Assert.True(File.Exists(otherFile));
        Assert.True(Directory.Exists(otherFolder));
        Directory.Delete(directory, recursive: true);
    }

    private static string CreateHelper(string directory, string id)
    {
        var helperPath = Path.Combine(directory, $"FourFoldAccountManager-updater-{id}.exe");
        File.WriteAllText(helperPath, "helper");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(ExtractionFolder(directory, id)).FullName, "native.dll"), "native");
        return helperPath;
    }

    private static string ExtractionFolder(string directory, string id) =>
        Path.Combine(directory, ".net", $"FourFoldAccountManager-updater-{id}");
}
