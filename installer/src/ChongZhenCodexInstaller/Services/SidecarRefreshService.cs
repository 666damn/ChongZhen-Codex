using System.Text.Json;

namespace ChongZhenCodexInstaller.Services;

public enum SidecarRefreshOutcome
{
    Refreshed,
    Current,
    Incompatible,
    IncompatibleUnchanged,
    GameRunning,
}

public sealed class SidecarRefreshService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string runtimeRoot;
    private readonly string statusPath;
    private readonly SidecarService sidecarService;

    public SidecarRefreshService(string runtimeRoot, SidecarService sidecarService)
    {
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
        statusPath = Path.Combine(this.runtimeRoot, "sidecar-status.json");
        this.sidecarService = sidecarService;
    }

    public async Task<SidecarRefreshOutcome> RefreshAsync(
        GameInstall game,
        VerifiedPayload payload,
        string token,
        bool gameRunning,
        CancellationToken cancellationToken)
    {
        if (gameRunning) return SidecarRefreshOutcome.GameRunning;
        var fingerprint = Hashing.AsarHeaderSha256(game.AsarPath);
        var previousStatus = ReadStatus();
        if (previousStatus is { State: "incompatible" } &&
            string.Equals(previousStatus.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            return SidecarRefreshOutcome.IncompatibleUnchanged;

        var executableHash = Hashing.Sha256(game.ExecutablePath);
        if (!payload.Manifest.SupportedGameExeSha256.Contains(executableHash, StringComparer.OrdinalIgnoreCase) ||
            !payload.Manifest.SupportedAsarHeaderSha256.Contains(fingerprint, StringComparer.OrdinalIgnoreCase))
        {
            WriteStatus(new(fingerprint, "incompatible"));
            return SidecarRefreshOutcome.Incompatible;
        }

        var sidecarPath = Path.Combine(runtimeRoot, "sidecars", fingerprint, "app.asar");
        var metadataPath = Path.Combine(runtimeRoot, "sidecars", fingerprint, "loader-metadata.bin");
        var beforeSidecar = File.Exists(sidecarPath) ? Hashing.Sha256(sidecarPath) : null;
        var beforeMetadata = File.Exists(metadataPath) ? Hashing.Sha256(metadataPath) : null;
        var result = await sidecarService.EnsureCurrentAsync(game, payload, token, cancellationToken);
        var unchanged = beforeSidecar is not null && beforeMetadata is not null &&
            string.Equals(beforeSidecar, Hashing.Sha256(result.SidecarPath), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(beforeMetadata, Hashing.Sha256(result.MetadataPath), StringComparison.OrdinalIgnoreCase);
        WriteStatus(new(fingerprint, "compatible"));
        return unchanged ? SidecarRefreshOutcome.Current : SidecarRefreshOutcome.Refreshed;
    }

    private RefreshStatus? ReadStatus()
    {
        if (!File.Exists(statusPath)) return null;
        try { return JsonSerializer.Deserialize<RefreshStatus>(File.ReadAllText(statusPath), JsonOptions); }
        catch (JsonException) { return null; }
    }

    private void WriteStatus(RefreshStatus status)
    {
        Directory.CreateDirectory(runtimeRoot);
        var temporary = statusPath + $".writing-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(status, JsonOptions));
            File.Move(temporary, statusPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record RefreshStatus(string Fingerprint, string State);
}
