using System.ComponentModel;
using System.Diagnostics;

namespace ChongZhenCodexInstaller.Services;

public sealed class InstalledRuntimeProcessStopper
{
    public void Stop(BridgeRuntimePaths paths)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(paths.InstalledExecutablePath),
            Path.GetFullPath(paths.NodePath),
        };
        var names = targets.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.Id == Environment.ProcessId) continue;
                    string? executable;
                    try { executable = process.MainModule?.FileName; }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception) { continue; }
                    if (string.IsNullOrWhiteSpace(executable) || !targets.Contains(Path.GetFullPath(executable))) continue;
                    if (!process.HasExited) process.Kill(true);
                    if (!process.WaitForExit(10_000))
                        throw new TimeoutException($"Installed runtime process did not exit: {process.Id}");
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and inspection.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
