using ChongZhenCodexInstaller.Services;
using System.Diagnostics;

namespace ChongZhenCodexInstaller.Tests;

public sealed class ProcessServiceTests
{
    [Fact]
    public void ReadyStatusCleanupDoesNotDeleteANewerBridgeStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cz-ready-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "bridge-ready.json");
        try
        {
            File.WriteAllText(path, "{\"pid\":456}");
            BridgeReadyStatus.DeleteIfOwned(path, 123);
            Assert.True(File.Exists(path));

            BridgeReadyStatus.DeleteIfOwned(path, 456);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InstalledRuntimeStopperWaitsForExactWatcherAndNodeProcessesToExit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cz-runtime-stopper-{Guid.NewGuid():N}");
        var paths = BridgeRuntimePaths.FromRoot(root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.NodePath)!);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), paths.InstalledExecutablePath);
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), paths.NodePath);
        var processes = new[]
        {
            Process.Start(new ProcessStartInfo(paths.InstalledExecutablePath, "/d /c ping 127.0.0.1 -t") { CreateNoWindow = true }),
            Process.Start(new ProcessStartInfo(paths.NodePath, "/d /c ping 127.0.0.1 -t") { CreateNoWindow = true }),
        };
        try
        {
            Assert.All(processes, process => Assert.NotNull(process));
            new InstalledRuntimeProcessStopper().Stop(paths);
            Assert.All(processes, process => Assert.True(process!.HasExited));
        }
        finally
        {
            foreach (var process in processes)
            {
                if (process is null) continue;
                if (!process.HasExited) process.Kill(true);
                process.Dispose();
            }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DisposeRemovesStaleBridgeReadyStatus()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cz-process-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var readyPath = Path.Combine(root, "bridge-ready.json");
        File.WriteAllText(readyPath, "{\"pid\":123}");
        try
        {
            using (var service = new ProcessService(BridgeRuntimePaths.FromRoot(root))) { }
            Assert.False(File.Exists(readyPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
