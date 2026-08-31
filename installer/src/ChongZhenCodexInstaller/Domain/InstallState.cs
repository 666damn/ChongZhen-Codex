namespace ChongZhenCodexInstaller.Domain;

public sealed record InstallState(
    string GamePath,
    InstallMode Mode,
    string Version,
    IReadOnlyList<string> Files,
    DateTimeOffset InstalledAt);
