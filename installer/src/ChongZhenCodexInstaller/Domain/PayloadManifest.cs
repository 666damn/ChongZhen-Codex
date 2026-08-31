namespace ChongZhenCodexInstaller.Domain;

public sealed record PayloadFile(string Sha256, long Length);

public sealed record PayloadManifest(
    string Version,
    IReadOnlyDictionary<string, PayloadFile> Files,
    IReadOnlyList<string> SupportedGameExeSha256,
    IReadOnlyList<string> SupportedAsarHeaderSha256,
    IReadOnlyList<string> SupportedExistingVersionSha256);

public sealed record OriginalFileState(bool Existed, string? Sha256, string? BackupPath);

public sealed record PatchInstallState(
    string GamePath,
    string Version,
    IReadOnlyDictionary<string, OriginalFileState> Originals,
    IReadOnlyDictionary<string, string> InstalledSha256,
    DateTimeOffset InstalledAt);
