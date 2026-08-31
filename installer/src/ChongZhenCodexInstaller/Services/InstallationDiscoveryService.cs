using System.Diagnostics;
using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public interface IInstallationDiscoveryService
{
    Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}

public sealed class InstallationDiscoveryService(ICoordinatorStateStore stateStore) : IInstallationDiscoveryService
{
    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var locator = new GameLocator(GameLocatorOptions.FromSystem(stateStore.Load()?.GamePath));
        var candidates = await locator.FindAsync(cancellationToken);
        if (candidates.Count == 0) throw new DirectoryNotFoundException("未找到《历史模拟器：崇祯》的有效 Steam 安装目录。");
        var codex = new CodexLocator().Find(new Dictionary<string, string?>
        {
            ["PATH"] = Environment.GetEnvironmentVariable("PATH"),
            ["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        });
        var running = Process.GetProcessesByName("ChongZhenSimulator");
        try { return new(candidates[0].Path, codex, running.Any(process => !process.HasExited)); }
        finally { foreach (var process in running) process.Dispose(); }
    }
}
