using System.Text;
using System.Text.RegularExpressions;

namespace ChongZhenCodexInstaller.Domain;

public sealed record SidecarMetadata(
    int SchemaVersion,
    string OfficialAsarPath,
    long SourceLength,
    string SourceHeaderSha256,
    string SidecarPath,
    long SidecarLength,
    string SidecarHeaderSha256);

public static class SidecarMetadataCodec
{
    private static readonly byte[] Magic = "CZSCAR1\0"u8.ToArray();
    private static readonly Regex HashPattern = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public const int CurrentSchemaVersion = 1;

    public static void Write(string path, SidecarMetadata metadata, string runtimeRoot)
    {
        Validate(metadata, runtimeRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
        writer.Write(Magic);
        writer.Write(metadata.SchemaVersion);
        writer.Write(0);
        writer.Write(metadata.SourceLength);
        writer.Write(metadata.SidecarLength);
        writer.Write(Convert.FromHexString(metadata.SourceHeaderSha256));
        writer.Write(Convert.FromHexString(metadata.SidecarHeaderSha256));
        WritePath(writer, metadata.OfficialAsarPath);
        WritePath(writer, metadata.SidecarPath);
        writer.Flush();
        stream.Flush(true);
    }

    public static SidecarMetadata Read(string path, string runtimeRoot)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.Unicode, true);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Invalid sidecar metadata magic.");
        var schema = reader.ReadInt32();
        if (reader.ReadInt32() != 0) throw new InvalidDataException("Invalid sidecar metadata flags.");
        var sourceLength = reader.ReadInt64();
        var sidecarLength = reader.ReadInt64();
        var sourceHash = Convert.ToHexString(reader.ReadBytes(32)).ToLowerInvariant();
        var sidecarHash = Convert.ToHexString(reader.ReadBytes(32)).ToLowerInvariant();
        var officialPath = ReadPath(reader);
        var sidecarPath = ReadPath(reader);
        if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing sidecar metadata.");
        var metadata = new SidecarMetadata(schema, officialPath, sourceLength, sourceHash, sidecarPath, sidecarLength, sidecarHash);
        Validate(metadata, runtimeRoot);
        return metadata;
    }

    private static void Validate(SidecarMetadata metadata, string runtimeRoot)
    {
        if (metadata.SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException("Unsupported sidecar metadata schema.");
        if (metadata.SourceLength <= 0 || metadata.SidecarLength <= 0) throw new InvalidDataException("Invalid sidecar metadata length.");
        if (!HashPattern.IsMatch(metadata.SourceHeaderSha256) || !HashPattern.IsMatch(metadata.SidecarHeaderSha256))
            throw new InvalidDataException("Invalid sidecar metadata hash.");
        var official = Path.GetFullPath(metadata.OfficialAsarPath);
        if (!string.Equals(Path.GetFileName(official), "app.asar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Official ASAR metadata path is invalid.");
        var sidecar = Path.GetFullPath(metadata.SidecarPath);
        var sidecarsRoot = Path.GetFullPath(Path.Combine(runtimeRoot, "sidecars"));
        if (!sidecar.StartsWith(sidecarsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sidecar metadata path escapes the runtime root.");
    }

    private static void WritePath(BinaryWriter writer, string path)
    {
        var bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(path));
        if (bytes.Length is <= 0 or > 65534) throw new InvalidDataException("Sidecar metadata path is too long.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadPath(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is <= 0 or > 65534 || length % 2 != 0) throw new InvalidDataException("Invalid sidecar metadata path length.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Truncated sidecar metadata path.");
        return Path.GetFullPath(Encoding.Unicode.GetString(bytes));
    }
}
