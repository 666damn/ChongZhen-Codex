using System.Collections.Concurrent;
using System.Diagnostics;

namespace ChongZhenCodexInstaller.Services;

public sealed record BridgeProcessHandle(int ProcessId);

public interface IProcessService
{
    bool IsGameRunning();
    BridgeProcessHandle StartBridge();
    bool IsBridgeRunning(BridgeProcessHandle handle);
    Task StopBridgeAsync(BridgeProcessHandle handle, CancellationToken cancellationToken);
    Task RefreshSidecarAsync(CancellationToken cancellationToken);
}

public sealed class ProcessService(BridgeRuntimePaths paths) : IProcessService, IDisposable
{
    private readonly ConcurrentDictionary<int, Process> children = new();
    private readonly string readyStatusPath = Path.Combine(paths.RuntimeRoot, "bridge-ready.json");

    public bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("ChongZhenSimulator");
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public BridgeProcessHandle StartBridge()
    {
        BridgeReadyStatus.Delete(readyStatusPath);
        var start = new ProcessStartInfo
        {
            FileName = paths.NodePath,
            WorkingDirectory = Path.GetDirectoryName(paths.BridgeMainPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--disable-warning=ExperimentalWarning");
        start.ArgumentList.Add(paths.BridgeMainPath);
        start.Environment["CHONGZHEN_BRIDGE_HOME"] = paths.RuntimeRoot;
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        if (!process.Start()) throw new InvalidOperationException("Unable to start the local bridge process.");
        var processId = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        children[processId] = process;
        process.Exited += (_, _) => HandleBridgeExit(processId);
        process.EnableRaisingEvents = true;
        return new(processId);
    }

    public bool IsBridgeRunning(BridgeProcessHandle handle)
    {
        if (!children.TryGetValue(handle.ProcessId, out var process)) return false;
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    public async Task StopBridgeAsync(BridgeProcessHandle handle, CancellationToken cancellationToken)
    {
        if (!children.TryGetValue(handle.ProcessId, out var process)) return;
        try
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            if (children.TryRemove(handle.ProcessId, out var removed)) removed.Dispose();
            BridgeReadyStatus.DeleteIfOwned(readyStatusPath, handle.ProcessId);
        }
    }

    public async Task RefreshSidecarAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = paths.InstalledExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("--refresh-sidecar");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start sidecar refresh process.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException("Sidecar refresh process failed.");
    }

    public void Dispose()
    {
        foreach (var process in children.Values)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch { }
            process.Dispose();
        }
        children.Clear();
        BridgeReadyStatus.Delete(readyStatusPath);
    }

    private void HandleBridgeExit(int processId)
    {
        if (children.TryRemove(processId, out var removed)) removed.Dispose();
        BridgeReadyStatus.DeleteIfOwned(readyStatusPath, processId);
    }
}
