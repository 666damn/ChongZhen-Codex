namespace ChongZhenCodexInstaller.Services;

public static class ReleasePayloadAudit
{
    public static void Run()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"ChongZhenCodexAudit-{Guid.NewGuid():N}");
        try
        {
            using var stream = new EmbeddedPayloadProvider().OpenRead();
            var payload = new PayloadService().ExtractAndVerify(stream, staging);
            foreach (var required in new[]
            {
                "bridge/node.exe", "bridge/src/main.js", "bridge/tools/configure-game-byok.mjs",
                "bridge/bridge-id.txt", "global/app.asar", "global/version.dll",
            })
            {
                if (!File.Exists(payload.GetPath(required))) throw new InvalidDataException($"Required payload file is missing: {required}");
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
