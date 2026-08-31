namespace ChongZhenCodexInstaller.Domain;

public sealed record CoordinatorState(
    string GamePath,
    InstallMode Mode,
    string Version,
    DateTimeOffset InstalledAt);

public sealed record DiscoveryResult(string GamePath, string CodexPath, bool GameRunning);

public sealed record InstallerStatus(
    string? GamePath,
    string? CodexPath,
    InstallMode? Mode,
    bool GameRunning,
    bool BridgeInstalled,
    bool GlobalInstalled,
    string Detail);

public sealed record OperationResult(bool Success, string Message, InstallerStatus? Status = null);

public sealed class UnsupportedGameVersionException(string message) : Exception(message);
