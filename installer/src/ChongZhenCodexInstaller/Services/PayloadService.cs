using System.IO.Compression;
using System.Text.Json;
using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public sealed record VerifiedPayload(PayloadManifest Manifest, string RootPath)
{
    public string GetPath(string relativePath)
    {
        if (!Manifest.Files.ContainsKey(relativePath)) throw new InvalidDataException($"Payload file is not manifested: {relativePath}");
        return Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}

public sealed class PayloadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public VerifiedPayload ExtractAndVerify(Stream payload, string stagingRoot)
    {
        var root = Path.GetFullPath(stagingRoot);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException($"Payload staging path already exists: {root}");
        Directory.CreateDirectory(root);
        try
        {
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, true);
            var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Payload manifest is missing.");
            PayloadManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<PayloadManifest>(manifestStream, JsonOptions)
                    ?? throw new InvalidDataException("Payload manifest is invalid.");
            }
            if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files.Count == 0)
                throw new InvalidDataException("Payload manifest has no version or files.");

            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries.Where(entry => !string.Equals(entry.FullName, "manifest.json", StringComparison.Ordinal)))
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var relative = Normalize(entry.FullName);
                if (!manifest.Files.TryGetValue(relative, out var expected))
                    throw new InvalidDataException($"Unmanifested payload entry: {relative}");
                if (!observed.Add(relative)) throw new InvalidDataException($"Duplicate payload entry: {relative}");
                var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Payload entry escapes staging root: {relative}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                long writtenLength;
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                    writtenLength = output.Length;
                }
                if (writtenLength != expected.Length || !string.Equals(Hashing.Sha256(destination), expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Payload hash verification failed: {relative}");
            }
            if (!manifest.Files.Keys.All(observed.Contains)) throw new InvalidDataException("One or more manifested payload files are missing.");
            return new(manifest, root);
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            throw;
        }
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Invalid payload entry path: {path}");
        return normalized;
    }
}
