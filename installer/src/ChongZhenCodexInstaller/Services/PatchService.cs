using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public enum FailurePoint
{
    None,
    AfterBackup,
    AfterAsarReplace,
    AfterDllReplace,
}

public sealed class InjectedFailureException(FailurePoint point) : Exception($"Injected failure: {point}");

public sealed record GameInstall(string RootPath, string ExecutablePath, string AsarPath, string VersionPath, string VersionOriginalPath)
{
    public static GameInstall FromRoot(string root)
    {
        var full = Path.GetFullPath(root);
        return new(
            full,
            Path.Combine(full, "ChongZhenSimulator.exe"),
            Path.Combine(full, "resources", "app.asar"),
            Path.Combine(full, "version.dll"),
            Path.Combine(full, "version_original.dll"));
    }
}

public sealed record PatchResult(LoaderInstallState State);

public sealed class PatchService
{
    private static readonly string[] LegacyManagedFiles = ["resources/app.asar", "version.dll", "version_original.dll"];
    private readonly InstallStateStore stateStore;
    private readonly SidecarService sidecarService;
    private readonly BridgeSecretStore secretStore;
    private readonly LoaderInstallService loaderInstallService;

    public PatchService(InstallStateStore stateStore, string? systemVersionPath = null)
    {
        this.stateStore = stateStore;
        var paths = BridgeRuntimePaths.FromRoot(stateStore.RootPath);
        sidecarService = new SidecarService(
            stateStore.RootPath,
            new NodeSidecarPatcher(paths.NodePath, paths.PatcherScriptPath));
        secretStore = new BridgeSecretStore(stateStore.RootPath);
        loaderInstallService = new LoaderInstallService(stateStore, systemVersionPath);
    }

    public PatchService(
        InstallStateStore stateStore,
        SidecarService sidecarService,
        BridgeSecretStore secretStore,
        LoaderInstallService loaderInstallService)
    {
        this.stateStore = stateStore;
        this.sidecarService = sidecarService;
        this.secretStore = secretStore;
        this.loaderInstallService = loaderInstallService;
    }

    public async Task<PatchResult> InstallGlobalAsync(
        GameInstall game,
        VerifiedPayload payload,
        FailurePoint failurePoint,
        CancellationToken cancellationToken)
    {
        var previous = stateStore.Load();
        if (previous is not null) await RestoreAsync(game, previous, cancellationToken);

        var executableHash = Hashing.Sha256(game.ExecutablePath);
        var asarHash = Hashing.Sha256(game.AsarPath);
        var token = await secretStore.GetOrCreateAsync(cancellationToken);
        await sidecarService.EnsureCurrentAsync(game, payload, token, cancellationToken);
        var state = await loaderInstallService.InstallAsync(game, payload, failurePoint, cancellationToken);
        EnsureOfficialUnchanged(game, executableHash, asarHash);
        return new(state);
    }

    public Task RestoreAsync(GameInstall game, LoaderInstallState state, CancellationToken cancellationToken) =>
        loaderInstallService.UninstallAsync(game, state, cancellationToken);

    public async Task RestoreAsync(GameInstall game, PatchInstallState state, CancellationToken cancellationToken)
    {
        if (!state.InstalledSha256.ContainsKey("resources/app.asar"))
        {
            await loaderInstallService.UninstallAsync(game, new LoaderInstallState(
                state.GamePath,
                state.Version,
                state.Originals,
                state.InstalledSha256,
                state.InstalledAt), cancellationToken);
            return;
        }
        await RestoreLegacyAsync(game, state, cancellationToken);
    }

    private async Task RestoreLegacyAsync(GameInstall game, PatchInstallState state, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetFullPath(game.RootPath), Path.GetFullPath(state.GamePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Legacy state belongs to a different game directory.");
        if (state.Originals.Keys.Any(key => !LegacyManagedFiles.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
            state.InstalledSha256.Keys.Any(key => !LegacyManagedFiles.Contains(key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Legacy state contains an unexpected file path.");

        foreach (var pair in state.InstalledSha256)
        {
            var target = ResolveLegacyPath(game, pair.Key);
            if (File.Exists(target) && !string.Equals(Hashing.Sha256(target), pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to overwrite an unknown current file: {pair.Key}");
        }
        foreach (var pair in state.Originals.Where(pair => pair.Value.Existed))
        {
            if (pair.Value.BackupPath is null || !File.Exists(pair.Value.BackupPath) ||
                !string.Equals(Hashing.Sha256(pair.Value.BackupPath), pair.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Original backup is missing or damaged: {pair.Key}");
        }

        var recoveryRoot = stateStore.CreateBackupDirectory();
        await using var transaction = new FileTransaction();
        foreach (var relative in LegacyManagedFiles)
        {
            var target = ResolveLegacyPath(game, relative);
            await transaction.BackupAsync(target, Path.Combine(recoveryRoot, relative.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
        }
        foreach (var pair in state.Originals)
        {
            var target = ResolveLegacyPath(game, pair.Key);
            if (pair.Value.Existed)
                await transaction.ReplaceAsync(pair.Value.BackupPath!, target, cancellationToken);
            else if (File.Exists(target))
                File.Delete(target);
        }
        foreach (var pair in state.Originals)
        {
            var target = ResolveLegacyPath(game, pair.Key);
            if (pair.Value.Existed)
            {
                if (!File.Exists(target) || !string.Equals(Hashing.Sha256(target), pair.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Restored file verification failed: {pair.Key}");
            }
            else if (File.Exists(target))
                throw new IOException($"Added file removal verification failed: {pair.Key}");
        }
        stateStore.Remove();
        transaction.Commit();
        Directory.Delete(recoveryRoot, true);
    }

    private static string ResolveLegacyPath(GameInstall game, string relative) => relative switch
    {
        "resources/app.asar" => game.AsarPath,
        "version.dll" => game.VersionPath,
        "version_original.dll" => game.VersionOriginalPath,
        _ => throw new InvalidDataException("Unexpected legacy file path."),
    };

    private static void EnsureOfficialUnchanged(GameInstall game, string executableHash, string asarHash)
    {
        if (!string.Equals(Hashing.Sha256(game.ExecutablePath), executableHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Hashing.Sha256(game.AsarPath), asarHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Official game files changed during global installation.");
    }
}
