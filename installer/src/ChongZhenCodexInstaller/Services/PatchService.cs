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

public sealed record PatchResult(PatchInstallState State);

public sealed class PatchService
{
    private readonly InstallStateStore stateStore;
    private readonly string systemVersionPath;

    public PatchService(InstallStateStore stateStore, string? systemVersionPath = null)
    {
        this.stateStore = stateStore;
        this.systemVersionPath = systemVersionPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "version.dll");
    }

    public async Task<PatchResult> InstallGlobalAsync(
        GameInstall game,
        VerifiedPayload payload,
        FailurePoint failurePoint,
        CancellationToken cancellationToken)
    {
        var executableHashBefore = Hashing.Sha256(game.ExecutablePath);
        ValidateGame(game, payload.Manifest);
        var payloadAsar = payload.GetPath("global/app.asar");
        var payloadProxy = payload.GetPath("global/version.dll");
        ValidatePayloadFile(payload.Manifest, "global/app.asar", payloadAsar);
        ValidatePayloadFile(payload.Manifest, "global/version.dll", payloadProxy);
        if (!File.Exists(systemVersionPath)) throw new FileNotFoundException("Target system Version API DLL is missing.", systemVersionPath);

        var backupRoot = stateStore.CreateBackupDirectory();
        var originals = new Dictionary<string, OriginalFileState>(StringComparer.OrdinalIgnoreCase);
        await using var transaction = new FileTransaction();
        originals["resources/app.asar"] = await transaction.BackupAsync(game.AsarPath, Path.Combine(backupRoot, "app.asar"), cancellationToken);
        originals["version.dll"] = await transaction.BackupAsync(game.VersionPath, Path.Combine(backupRoot, "version.dll"), cancellationToken);
        originals["version_original.dll"] = await transaction.BackupAsync(game.VersionOriginalPath, Path.Combine(backupRoot, "version_original.dll"), cancellationToken);
        ThrowIfInjected(failurePoint, FailurePoint.AfterBackup);

        await transaction.ReplaceAsync(payloadAsar, game.AsarPath, cancellationToken);
        ThrowIfInjected(failurePoint, FailurePoint.AfterAsarReplace);
        await transaction.ReplaceAsync(payloadProxy, game.VersionPath, cancellationToken);
        ThrowIfInjected(failurePoint, FailurePoint.AfterDllReplace);
        await transaction.ReplaceAsync(systemVersionPath, game.VersionOriginalPath, cancellationToken);

        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resources/app.asar"] = Hashing.Sha256(game.AsarPath),
            ["version.dll"] = Hashing.Sha256(game.VersionPath),
            ["version_original.dll"] = Hashing.Sha256(game.VersionOriginalPath),
        };
        if (!string.Equals(executableHashBefore, Hashing.Sha256(game.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Game executable changed during installation.");
        var state = new PatchInstallState(game.RootPath, payload.Manifest.Version, originals, installed, DateTimeOffset.UtcNow);
        stateStore.Save(state);
        transaction.Commit();
        return new(state);
    }

    public async Task RestoreAsync(GameInstall game, PatchInstallState state, CancellationToken cancellationToken)
    {
        foreach (var pair in state.InstalledSha256)
        {
            var target = Path.Combine(game.RootPath, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target) && !string.Equals(Hashing.Sha256(target), pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to overwrite an unknown current file: {pair.Key}");
        }

        var recoveryRoot = stateStore.CreateBackupDirectory();
        await using var transaction = new FileTransaction();
        foreach (var pair in state.Originals)
        {
            var target = Path.Combine(game.RootPath, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            await transaction.BackupAsync(target, Path.Combine(recoveryRoot, pair.Key.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
        }
        foreach (var pair in state.Originals)
        {
            var target = Path.Combine(game.RootPath, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (pair.Value.Existed)
            {
                if (pair.Value.BackupPath is null || !File.Exists(pair.Value.BackupPath) ||
                    !string.Equals(Hashing.Sha256(pair.Value.BackupPath), pair.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Original backup is missing or damaged: {pair.Key}");
                await transaction.ReplaceAsync(pair.Value.BackupPath, target, cancellationToken);
            }
            else if (File.Exists(target)) File.Delete(target);
        }
        foreach (var pair in state.Originals)
        {
            var target = Path.Combine(game.RootPath, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (pair.Value.Existed)
            {
                if (!File.Exists(target) || !string.Equals(Hashing.Sha256(target), pair.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Restored file verification failed: {pair.Key}");
            }
            else if (File.Exists(target)) throw new IOException($"Added file removal verification failed: {pair.Key}");
        }
        transaction.Commit();
        stateStore.Remove();
        Directory.Delete(recoveryRoot, true);
    }

    private static void ValidateGame(GameInstall game, PayloadManifest manifest)
    {
        if (!File.Exists(game.ExecutablePath) || !File.Exists(game.AsarPath)) throw new FileNotFoundException("Game executable or ASAR is missing.");
        var exeHash = Hashing.Sha256(game.ExecutablePath);
        if (!manifest.SupportedGameExeSha256.Contains(exeHash, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported game executable hash.");
        var asarHeader = Hashing.AsarHeaderSha256(game.AsarPath);
        if (!manifest.SupportedAsarHeaderSha256.Contains(asarHeader, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported game ASAR header hash.");
    }

    private static void ValidatePayloadFile(PayloadManifest manifest, string relativePath, string path)
    {
        if (!manifest.Files.TryGetValue(relativePath, out var expected) || !File.Exists(path) ||
            new FileInfo(path).Length != expected.Length ||
            !string.Equals(Hashing.Sha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Payload file verification failed: {relativePath}");
    }

    private static void ThrowIfInjected(FailurePoint selected, FailurePoint current)
    {
        if (selected == current) throw new InjectedFailureException(current);
    }
}
