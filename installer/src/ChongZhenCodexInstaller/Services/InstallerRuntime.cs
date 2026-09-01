namespace ChongZhenCodexInstaller.Services;

public static class InstallerRuntime
{
    public static string ResolveRuntimeRoot(IReadOnlyDictionary<string, string?> environment)
    {
        environment.TryGetValue("LOCALAPPDATA", out var localAppData);
        if (string.IsNullOrWhiteSpace(localAppData)) throw new DirectoryNotFoundException("LOCALAPPDATA is unavailable.");
        return Path.GetFullPath(Path.Combine(localAppData, "ChongZhenCodexBridge"));
    }

    public static string RuntimeRoot => ResolveRuntimeRoot(new Dictionary<string, string?>
    {
        ["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    });

    public static IInstallerCoordinator CreateCoordinator()
    {
        var stateStore = new CoordinatorStateStore(RuntimeRoot);
        var discovery = new InstallationDiscoveryService(stateStore);
        var executable = Environment.ProcessPath ?? Application.ExecutablePath;
        var backend = new InstallerBackend(RuntimeRoot, discovery, new EmbeddedPayloadProvider(), executable);
        return new InstallerCoordinator(backend, stateStore);
    }

    public static (WatcherService Watcher, ProcessService Processes) CreateWatcher()
    {
        var paths = BridgeRuntimePaths.FromRoot(RuntimeRoot);
        foreach (var required in new[] { paths.NodePath, paths.BridgeMainPath, paths.ConfigPath })
            if (!File.Exists(required)) throw new FileNotFoundException("Installed bridge runtime is incomplete.", required);
        var processes = new ProcessService(paths);
        return (new WatcherService(processes, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2)), processes);
    }

    public static async Task RefreshSidecarAsync(CancellationToken cancellationToken)
    {
        var coordinatorState = new CoordinatorStateStore(RuntimeRoot).Load();
        var loaderState = new InstallStateStore(RuntimeRoot).LoadLoader();
        if (coordinatorState?.Mode != Domain.InstallMode.BridgeAndGlobal || loaderState is null) return;

        var game = GameInstall.FromRoot(coordinatorState.GamePath);
        var gameProcesses = System.Diagnostics.Process.GetProcessesByName("ChongZhenSimulator");
        bool gameRunning;
        try { gameRunning = gameProcesses.Any(process => !process.HasExited); }
        finally { foreach (var process in gameProcesses) process.Dispose(); }
        if (gameRunning) return;

        var staging = Path.Combine(Path.GetTempPath(), $"ChongZhenCodexRefresh-{Guid.NewGuid():N}");
        try
        {
            using var stream = new EmbeddedPayloadProvider().OpenRead();
            var payload = new PayloadService().ExtractAndVerify(stream, staging);
            var paths = BridgeRuntimePaths.FromRoot(RuntimeRoot);
            var sidecar = new SidecarService(RuntimeRoot, new NodeSidecarPatcher(paths.NodePath, paths.PatcherScriptPath));
            var token = await new BridgeSecretStore(RuntimeRoot).GetOrCreateAsync(cancellationToken);
            await new SidecarRefreshService(RuntimeRoot, sidecar).RefreshAsync(
                game, payload, token, gameRunning: false, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
