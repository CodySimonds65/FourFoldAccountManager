using System.Diagnostics;
using System.IO;
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
}
