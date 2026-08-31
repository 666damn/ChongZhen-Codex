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
}

public sealed class ProcessService(BridgeRuntimePaths paths) : IProcessService, IDisposable
{
    private readonly ConcurrentDictionary<int, Process> children = new();

    public bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("ChongZhenSimulator");
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public BridgeProcessHandle StartBridge()
    {
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
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Exited += (_, _) =>
        {
            if (children.TryRemove(process.Id, out var removed)) removed.Dispose();
        };
        if (!process.Start()) throw new InvalidOperationException("Unable to start the local bridge process.");
        children[process.Id] = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new(process.Id);
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
        }
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
    }
}
