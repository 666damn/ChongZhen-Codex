using System.Diagnostics;
using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public sealed class InstallerBackend : IInstallerBackend, IDisposable
{
    private readonly string runtimeRoot;
    private readonly IInstallationDiscoveryService discovery;
    private readonly IPayloadProvider payloadProvider;
    private readonly string installerSource;
    private readonly StartupService startup;
    private readonly InstallStateStore patchStateStore;
    private readonly CoordinatorStateStore coordinatorStateStore;
    private VerifiedPayload? payload;
    private string? payloadStaging;

    public InstallerBackend(
        string runtimeRoot,
        IInstallationDiscoveryService discovery,
        IPayloadProvider payloadProvider,
        string installerSource)
    {
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
        this.discovery = discovery;
        this.payloadProvider = payloadProvider;
        this.installerSource = Path.GetFullPath(installerSource);
        startup = new(new WindowsStartupRegistry());
        patchStateStore = new(this.runtimeRoot);
        coordinatorStateStore = new(this.runtimeRoot);
    }

    public Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) => discovery.DiscoverAsync(cancellationToken);

    public async Task InstallBridgeAsync(DiscoveryResult discoveryResult, CancellationToken cancellationToken)
    {
        try
        {
            StopInstalledWatchers();
            var verified = EnsurePayload();
            var paths = await new BridgeInstallService(runtimeRoot, startup)
                .InstallRuntimeAsync(verified, installerSource, cancellationToken);
            StartInstalledWatcher(paths.InstalledExecutablePath);
        }
        catch
        {
            CleanupPayload();
            throw;
        }
    }

    public async Task InstallGlobalAsync(DiscoveryResult discoveryResult, CancellationToken cancellationToken)
    {
        try
        {
            await new PatchService(patchStateStore).InstallGlobalAsync(
                GameInstall.FromRoot(discoveryResult.GamePath), EnsurePayload(), FailurePoint.None, cancellationToken);
        }
        catch (InvalidDataException error)
        {
            CleanupPayload();
            throw new UnsupportedGameVersionException(error.Message);
        }
        catch
        {
            CleanupPayload();
            throw;
        }
    }

    public async Task RestoreGlobalAsync(string gamePath, CancellationToken cancellationToken)
    {
        var state = patchStateStore.Load();
        if (state is null) return;
        await new PatchService(patchStateStore).RestoreAsync(GameInstall.FromRoot(gamePath), state, cancellationToken);
    }

    public async Task ConfigureByokAsync(DiscoveryResult discoveryResult, bool install, CancellationToken cancellationToken)
    {
        try
        {
            var paths = BridgeRuntimePaths.FromRoot(runtimeRoot);
            var service = new GameByokService(paths, new GameDebugSessionFactory(), new ProcessCommandRunner());
            await service.ConfigureAsync(discoveryResult.GamePath, install, cancellationToken);
        }
        finally { CleanupPayload(); }
    }

    public Task UninstallBridgeAsync(CancellationToken cancellationToken)
    {
        startup.Disable();
        CleanupPayload();
        StopInstalledWatchers();
        if (Directory.Exists(runtimeRoot))
        {
            var localRoot = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (!runtimeRoot.StartsWith(localRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(runtimeRoot), "ChongZhenCodexBridge", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove an unexpected runtime directory.");
            Directory.Delete(runtimeRoot, true);
        }
        return Task.CompletedTask;
    }

    public async Task<InstallerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        DiscoveryResult? found = null;
        string detail;
        try
        {
            found = await discovery.DiscoverAsync(cancellationToken);
            detail = "已找到游戏和 Codex CLI；登录状态由本机桥接启动时验证。";
        }
        catch (Exception error)
        {
            detail = error.Message;
        }
        var coordinatorState = coordinatorStateStore.Load();
        var paths = BridgeRuntimePaths.FromRoot(runtimeRoot);
        var bridgeInstalled = File.Exists(paths.NodePath) && File.Exists(paths.BridgeMainPath) && File.Exists(paths.ConfigPath);
        var globalInstalled = patchStateStore.Load() is not null;
        return new(
            found?.GamePath ?? coordinatorState?.GamePath,
            found?.CodexPath,
            coordinatorState?.Mode,
            found?.GameRunning ?? false,
            bridgeInstalled,
            globalInstalled,
            detail);
    }

    private VerifiedPayload EnsurePayload()
    {
        if (payload is not null) return payload;
        payloadStaging = Path.Combine(Path.GetTempPath(), $"ChongZhenCodexPayload-{Guid.NewGuid():N}");
        using var stream = payloadProvider.OpenRead();
        payload = new PayloadService().ExtractAndVerify(stream, payloadStaging);
        return payload;
    }

    private void CleanupPayload()
    {
        payload = null;
        if (payloadStaging is not null && Directory.Exists(payloadStaging)) Directory.Delete(payloadStaging, true);
        payloadStaging = null;
    }

    private void StopInstalledWatchers()
    {
        new InstalledRuntimeProcessStopper().Stop(BridgeRuntimePaths.FromRoot(runtimeRoot));
    }

    private static void StartInstalledWatcher(string installedExecutable)
    {
        var start = new ProcessStartInfo
        {
            FileName = installedExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("--watch");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start the installed watcher.");
    }

    public void Dispose() => CleanupPayload();
}
