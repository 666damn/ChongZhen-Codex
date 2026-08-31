namespace ChongZhenCodexInstaller.Services;

public static class ReleasePayloadAudit
{
    private static readonly HashSet<string> ExactFiles = new(StringComparer.Ordinal)
    {
        "bridge/node.exe",
        "bridge/package.json",
        "bridge/tools/configure-game-byok.mjs",
        "bridge/src/bridge-service.js",
        "bridge/src/codex-client.js",
        "bridge/src/codex-discovery.js",
        "bridge/src/hash.js",
        "bridge/src/http-server.js",
        "bridge/src/jsonl-rpc.js",
        "bridge/src/main.js",
        "bridge/src/openai-format.js",
        "bridge/src/prompt-builder.js",
        "bridge/src/ready-status.js",
        "bridge/src/state-store.js",
        "bridge/src/watcher-main.js",
        "bridge/src/watcher.js",
        "bridge/patcher/package.json",
        "bridge/patcher/package-lock.json",
        "bridge/patcher/src/build-sidecar.mjs",
        "bridge/patcher/src/verify-sidecar.mjs",
        "bridge/scripts/asar-header-hash.mjs",
        "bridge/scripts/patch-region.mjs",
        "bridge/scripts/patch-renderer.mjs",
        "bridge/scripts/verify-asar.mjs",
        "bridge/scripts/verify-asar-delta.mjs",
        "global/version.dll",
    };

    private static readonly HashSet<string> PinnedDependencyRoots = new(StringComparer.Ordinal)
    {
        ".package-lock.json",
        "@electron/asar",
        "balanced-match",
        "brace-expansion",
        "glob",
        "lru-cache",
        "minimatch",
        "minipass",
        "path-scurry",
    };

    public static void Run()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"ChongZhenCodexAudit-{Guid.NewGuid():N}");
        try
        {
            using var stream = new EmbeddedPayloadProvider().OpenRead();
            var payload = new PayloadService().ExtractAndVerify(stream, staging);
            foreach (var relative in payload.Manifest.Files.Keys)
            {
                if (relative.EndsWith(".asar", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith("bridge-id.txt", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith("release-token.txt", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Forbidden payload file: {relative}");
                if (!IsAllowed(relative)) throw new InvalidDataException($"Unallowlisted payload file: {relative}");
            }
            foreach (var required in ExactFiles)
            {
                if (!File.Exists(payload.GetPath(required)))
                    throw new InvalidDataException($"Required payload file is missing: {required}");
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static bool IsAllowed(string relative)
    {
        if (ExactFiles.Contains(relative)) return true;
        const string dependencyPrefix = "bridge/patcher/node_modules/";
        if (!relative.StartsWith(dependencyPrefix, StringComparison.Ordinal)) return false;
        var remainder = relative[dependencyPrefix.Length..];
        var segments = remainder.Split('/');
        var packageRoot = segments[0] == "@electron" && segments.Length > 1
            ? $"{segments[0]}/{segments[1]}"
            : segments[0];
        if (!PinnedDependencyRoots.Contains(packageRoot)) return false;
        var extension = Path.GetExtension(relative);
        return extension is ".js" or ".mjs" or ".cjs" or ".json" or ".md" or ".map" or ".ts" or ".cts" or ".mts" or "";
    }
}
