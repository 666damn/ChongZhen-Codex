using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public sealed class LoaderInstallService
{
    private static readonly string[] ManagedFiles = ["version.dll", "version_original.dll"];
    private readonly InstallStateStore stateStore;
    private readonly string systemVersionPath;

    public LoaderInstallService(InstallStateStore stateStore, string? systemVersionPath = null)
    {
        this.stateStore = stateStore;
        this.systemVersionPath = Path.GetFullPath(systemVersionPath ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "version.dll"));
    }

    public Task<LoaderInstallState> InstallAsync(
        GameInstall game,
        VerifiedPayload payload,
        CancellationToken cancellationToken) =>
        InstallAsync(game, payload, FailurePoint.None, cancellationToken);

    public async Task<LoaderInstallState> InstallAsync(
        GameInstall game,
        VerifiedPayload payload,
        FailurePoint failurePoint,
        CancellationToken cancellationToken)
    {
        ValidateOfficialGame(game, payload.Manifest);
        var officialExeHash = Hashing.Sha256(game.ExecutablePath);
        var officialAsarHash = Hashing.Sha256(game.AsarPath);
        var payloadProxy = payload.GetPath("global/version.dll");
        ValidatePayloadFile(payload.Manifest, "global/version.dll", payloadProxy);
        if (!File.Exists(systemVersionPath))
            throw new FileNotFoundException("Target system Version API DLL is missing.", systemVersionPath);

        var proxyHash = Hashing.Sha256(payloadProxy);
        var systemVersionHash = Hashing.Sha256(systemVersionPath);
        ValidateCollision(game.VersionPath, payload.Manifest.SupportedExistingVersionSha256.Append(proxyHash));
        ValidateCollision(game.VersionOriginalPath, [systemVersionHash]);

        var backupRoot = stateStore.CreateBackupDirectory();
        try
        {
            await using var transaction = new FileTransaction();
            await transaction.BackupAsync(game.VersionPath, Path.Combine(backupRoot, "version.dll"), cancellationToken);
            await transaction.BackupAsync(game.VersionOriginalPath, Path.Combine(backupRoot, "version_original.dll"), cancellationToken);
            ThrowIfInjected(failurePoint, FailurePoint.AfterBackup);

            await transaction.ReplaceAsync(payloadProxy, game.VersionPath, cancellationToken);
            ThrowIfInjected(failurePoint, FailurePoint.AfterDllReplace);
            await transaction.ReplaceAsync(systemVersionPath, game.VersionOriginalPath, cancellationToken);

            EnsureOfficialUnchanged(game, officialExeHash, officialAsarHash);
            var originals = ManagedFiles.ToDictionary(
                relative => relative,
                _ => new OriginalFileState(false, null, null),
                StringComparer.OrdinalIgnoreCase);
            var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["version.dll"] = Hashing.Sha256(game.VersionPath),
                ["version_original.dll"] = Hashing.Sha256(game.VersionOriginalPath),
            };
            var state = new LoaderInstallState(
                game.RootPath, payload.Manifest.Version, originals, installed, DateTimeOffset.UtcNow);
            stateStore.Save(state);
            transaction.Commit();
            return state;
        }
        finally
        {
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
        }
    }

    public async Task UninstallAsync(
        GameInstall game,
        LoaderInstallState state,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetFullPath(game.RootPath), Path.GetFullPath(state.GamePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Loader state belongs to a different game directory.");
        if (state.InstalledSha256.Keys.Any(key => !ManagedFiles.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
            ManagedFiles.Any(key => !state.InstalledSha256.ContainsKey(key)))
            throw new InvalidDataException("Loader state contains an unexpected file path.");

        foreach (var relative in ManagedFiles)
        {
            var target = ResolveManagedPath(game, relative);
            if (File.Exists(target) &&
                !string.Equals(Hashing.Sha256(target), state.InstalledSha256[relative], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to remove an unknown current file: {relative}");
        }

        var officialExeHash = Hashing.Sha256(game.ExecutablePath);
        var officialAsarHash = Hashing.Sha256(game.AsarPath);
        var recoveryRoot = stateStore.CreateBackupDirectory();
        try
        {
            await using var transaction = new FileTransaction();
            foreach (var relative in ManagedFiles)
            {
                var target = ResolveManagedPath(game, relative);
                await transaction.BackupAsync(target, Path.Combine(recoveryRoot, relative), cancellationToken);
            }
            foreach (var relative in ManagedFiles)
            {
                var target = ResolveManagedPath(game, relative);
                if (File.Exists(target)) File.Delete(target);
            }
            EnsureOfficialUnchanged(game, officialExeHash, officialAsarHash);
            stateStore.Remove();
            transaction.Commit();
        }
        finally
        {
            if (Directory.Exists(recoveryRoot)) Directory.Delete(recoveryRoot, true);
        }
    }

    private static string ResolveManagedPath(GameInstall game, string relative) => relative switch
    {
        "version.dll" => game.VersionPath,
        "version_original.dll" => game.VersionOriginalPath,
        _ => throw new InvalidDataException("Unexpected loader file path."),
    };

    private static void ValidateCollision(string path, IEnumerable<string> allowedHashes)
    {
        if (!File.Exists(path)) return;
        var current = Hashing.Sha256(path);
        if (!allowedHashes.Contains(current, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Refusing to overwrite unknown file: {Path.GetFileName(path)}");
    }

    private static void ValidateOfficialGame(GameInstall game, PayloadManifest manifest)
    {
        if (!File.Exists(game.ExecutablePath) || !File.Exists(game.AsarPath))
            throw new FileNotFoundException("Game executable or official ASAR is missing.");
        if (!manifest.SupportedGameExeSha256.Contains(Hashing.Sha256(game.ExecutablePath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported game executable hash.");
        if (!manifest.SupportedAsarHeaderSha256.Contains(Hashing.AsarHeaderSha256(game.AsarPath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported official ASAR header hash.");
    }

    private static void ValidatePayloadFile(PayloadManifest manifest, string relativePath, string path)
    {
        if (!manifest.Files.TryGetValue(relativePath, out var expected) || !File.Exists(path) ||
            new FileInfo(path).Length != expected.Length ||
            !string.Equals(Hashing.Sha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Payload file verification failed: {relativePath}");
    }

    private static void EnsureOfficialUnchanged(GameInstall game, string executableHash, string asarHash)
    {
        if (!string.Equals(Hashing.Sha256(game.ExecutablePath), executableHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Hashing.Sha256(game.AsarPath), asarHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Official game files changed during loader installation.");
    }

    private static void ThrowIfInjected(FailurePoint selected, FailurePoint current)
    {
        if (selected == current) throw new InjectedFailureException(current);
    }
}
