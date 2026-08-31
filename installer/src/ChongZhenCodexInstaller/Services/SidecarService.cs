using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public sealed record SidecarEnsureResult(string SidecarPath, string MetadataPath, SidecarMetadata Metadata);

public sealed class SidecarService
{
    private readonly string runtimeRoot;
    private readonly string sidecarsRoot;
    private readonly ISidecarPatcher patcher;

    public SidecarService(string runtimeRoot, ISidecarPatcher patcher)
    {
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
        sidecarsRoot = Path.Combine(this.runtimeRoot, "sidecars");
        this.patcher = patcher;
    }

    public async Task<SidecarEnsureResult> EnsureCurrentAsync(
        GameInstall game,
        VerifiedPayload payload,
        string token,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(game.ExecutablePath) || !File.Exists(game.AsarPath))
            throw new FileNotFoundException("Game executable or official ASAR is missing.");
        var executableHash = Hashing.Sha256(game.ExecutablePath);
        if (!payload.Manifest.SupportedGameExeSha256.Contains(executableHash, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported game executable hash.");
        var sourceHeader = Hashing.AsarHeaderSha256(game.AsarPath);
        if (!payload.Manifest.SupportedAsarHeaderSha256.Contains(sourceHeader, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported official ASAR header hash.");
        var sourceLength = new FileInfo(game.AsarPath).Length;
        var finalRoot = Path.Combine(sidecarsRoot, sourceHeader);
        var finalSidecar = Path.Combine(finalRoot, "app.asar");
        var finalMetadata = Path.Combine(finalRoot, "loader-metadata.bin");
        var existing = TryReadExisting(game, finalSidecar, finalMetadata, sourceHeader, sourceLength);
        if (existing is not null) return existing;

        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(sidecarsRoot);
        var temporaryRoot = Path.Combine(runtimeRoot, $".sidecar-building-{Guid.NewGuid():N}");
        var temporarySidecar = Path.Combine(temporaryRoot, "app.asar");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var built = await patcher.BuildAsync(game.AsarPath, temporarySidecar, token, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(built.SourceHeaderSha256, sourceHeader, StringComparison.OrdinalIgnoreCase) ||
                built.SourceLength != sourceLength ||
                !File.Exists(temporarySidecar) ||
                built.SidecarLength != new FileInfo(temporarySidecar).Length ||
                !string.Equals(built.SidecarHeaderSha256, Hashing.AsarHeaderSha256(temporarySidecar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Generated sidecar verification failed.");
            if (!string.Equals(Hashing.Sha256(game.ExecutablePath), executableHash, StringComparison.OrdinalIgnoreCase) ||
                new FileInfo(game.AsarPath).Length != sourceLength ||
                !string.Equals(Hashing.AsarHeaderSha256(game.AsarPath), sourceHeader, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Official game files changed during sidecar generation.");

            var metadata = new SidecarMetadata(
                SidecarMetadataCodec.CurrentSchemaVersion,
                Path.GetFullPath(game.AsarPath),
                sourceLength,
                sourceHeader,
                Path.GetFullPath(finalSidecar),
                built.SidecarLength,
                built.SidecarHeaderSha256.ToLowerInvariant());
            SidecarMetadataCodec.Write(Path.Combine(temporaryRoot, "loader-metadata.bin"), metadata, runtimeRoot);

            if (Directory.Exists(finalRoot)) Directory.Delete(finalRoot, true);
            Directory.Move(temporaryRoot, finalRoot);
            var published = TryReadExisting(game, finalSidecar, finalMetadata, sourceHeader, sourceLength);
            return published ?? throw new IOException("Published sidecar verification failed.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    private SidecarEnsureResult? TryReadExisting(
        GameInstall game,
        string sidecarPath,
        string metadataPath,
        string sourceHeader,
        long sourceLength)
    {
        if (!File.Exists(sidecarPath) || !File.Exists(metadataPath)) return null;
        try
        {
            var metadata = SidecarMetadataCodec.Read(metadataPath, runtimeRoot);
            if (!string.Equals(metadata.OfficialAsarPath, Path.GetFullPath(game.AsarPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(metadata.SidecarPath, Path.GetFullPath(sidecarPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(metadata.SourceHeaderSha256, sourceHeader, StringComparison.OrdinalIgnoreCase) ||
                metadata.SourceLength != sourceLength ||
                metadata.SidecarLength != new FileInfo(sidecarPath).Length ||
                !string.Equals(metadata.SidecarHeaderSha256, Hashing.AsarHeaderSha256(sidecarPath), StringComparison.OrdinalIgnoreCase))
                return null;
            return new(sidecarPath, metadataPath, metadata);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
