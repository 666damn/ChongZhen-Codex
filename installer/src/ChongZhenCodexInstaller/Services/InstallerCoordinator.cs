using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public interface IInstallerBackend
{
    Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
    Task InstallBridgeAsync(DiscoveryResult discovery, CancellationToken cancellationToken);
    Task InstallGlobalAsync(DiscoveryResult discovery, CancellationToken cancellationToken);
    Task RestoreGlobalAsync(string gamePath, CancellationToken cancellationToken);
    Task ConfigureByokAsync(DiscoveryResult discovery, bool install, CancellationToken cancellationToken);
    Task UninstallBridgeAsync(CancellationToken cancellationToken);
    Task<InstallerStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public interface IInstallerCoordinator
{
    Task<OperationResult> InstallAsync(InstallMode mode, CancellationToken cancellationToken);
    Task<OperationResult> RepairAsync(CancellationToken cancellationToken);
    Task<OperationResult> RestoreAsync(CancellationToken cancellationToken);
    Task<OperationResult> UninstallAsync(CancellationToken cancellationToken);
    Task<InstallerStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed class InstallerCoordinator(IInstallerBackend backend, ICoordinatorStateStore stateStore) : IInstallerCoordinator
{
    public async Task<OperationResult> InstallAsync(InstallMode mode, CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await backend.DiscoverAsync(cancellationToken);
            if (discovery.GameRunning) return new(false, "游戏正在运行，请先退出游戏后再安装。", await backend.GetStatusAsync(cancellationToken));
            var previous = stateStore.Load();
            await backend.InstallBridgeAsync(discovery, cancellationToken);
            if (mode == InstallMode.BridgeAndGlobal)
                await backend.InstallGlobalAsync(discovery, cancellationToken);
            else if (previous?.Mode == InstallMode.BridgeAndGlobal)
                await backend.RestoreGlobalAsync(previous.GamePath, cancellationToken);
            await backend.ConfigureByokAsync(discovery, true, cancellationToken);
            stateStore.Save(new(discovery.GamePath, mode, "1.0.0", DateTimeOffset.UtcNow));
            return new(true, mode == InstallMode.BridgeOnly ? "仅 Codex 桥接安装完成。" : "Codex 桥接与全球分流安装完成。",
                await backend.GetStatusAsync(cancellationToken));
        }
        catch (UnsupportedGameVersionException error)
        {
            return new(false, $"游戏版本不受支持，未覆盖游戏文件：{error.Message}", await backend.GetStatusAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, "操作已取消。", await backend.GetStatusAsync(CancellationToken.None));
        }
        catch (Exception error)
        {
            return new(false, error.Message, await backend.GetStatusAsync(CancellationToken.None));
        }
    }

    public async Task<OperationResult> RepairAsync(CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        if (state is null) return new(false, "尚未安装，无需修复。", await backend.GetStatusAsync(cancellationToken));
        if (state.Mode == InstallMode.BridgeAndGlobal)
        {
            try { await backend.RestoreGlobalAsync(state.GamePath, cancellationToken); }
            catch (Exception error) { return new(false, error.Message, await backend.GetStatusAsync(CancellationToken.None)); }
        }
        return await InstallAsync(state.Mode, cancellationToken);
    }

    public async Task<OperationResult> RestoreAsync(CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        if (state?.Mode != InstallMode.BridgeAndGlobal)
            return new(false, "当前没有已安装的全球分流补丁。", await backend.GetStatusAsync(cancellationToken));
        try
        {
            await backend.RestoreGlobalAsync(state.GamePath, cancellationToken);
            stateStore.Save(state with { Mode = InstallMode.BridgeOnly, InstalledAt = DateTimeOffset.UtcNow });
            return new(true, "已恢复原始游戏文件，Codex 桥接仍保留。", await backend.GetStatusAsync(cancellationToken));
        }
        catch (Exception error)
        {
            return new(false, error.Message, await backend.GetStatusAsync(CancellationToken.None));
        }
    }

    public async Task<OperationResult> UninstallAsync(CancellationToken cancellationToken)
    {
        try
        {
            var discovery = await backend.DiscoverAsync(cancellationToken);
            if (discovery.GameRunning) return new(false, "游戏正在运行，请先退出游戏后再卸载。", await backend.GetStatusAsync(cancellationToken));
            var state = stateStore.Load();
            if (state?.Mode == InstallMode.BridgeAndGlobal)
                await backend.RestoreGlobalAsync(state.GamePath, cancellationToken);
            await backend.ConfigureByokAsync(discovery, false, cancellationToken);
            await backend.UninstallBridgeAsync(cancellationToken);
            stateStore.Remove();
            return new(true, "已恢复游戏并卸载 Codex 桥接。", await backend.GetStatusAsync(cancellationToken));
        }
        catch (Exception error)
        {
            return new(false, error.Message, await backend.GetStatusAsync(CancellationToken.None));
        }
    }

    public Task<InstallerStatus> GetStatusAsync(CancellationToken cancellationToken) => backend.GetStatusAsync(cancellationToken);
}
