using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChongZhenCodexInstaller.Tests;

internal static class TestFiles
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void WriteMinimalAsar(string path, string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = Encoding.UTF8.GetBytes($"{{\"files\":{{\"marker\":{{\"size\":0,\"offset\":\"0\"}}}},\"tag\":\"{marker}\"}}\0");
        var header = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), json.Length);
        json.CopyTo(header.AsSpan(8));
        var archive = new byte[8 + header.Length];
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(4), header.Length);
        header.CopyTo(archive.AsSpan(8));
        File.WriteAllBytes(path, archive);
    }

    public static MemoryStream CreatePayloadZip(IReadOnlyDictionary<string, byte[]> files, bool corruptHash = false)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var manifestFiles = files.ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    sha256 = corruptHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant(),
                    length = pair.Value.LongLength,
                });
            var manifest = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifest.Open(), Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(JsonSerializer.Serialize(new
                {
                    version = "test",
                    files = manifestFiles,
                    supportedGameExeSha256 = Array.Empty<string>(),
                    supportedAsarHeaderSha256 = Array.Empty<string>(),
                }));
            }
            foreach (var pair in files)
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using var output = entry.Open();
                output.Write(pair.Value);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
