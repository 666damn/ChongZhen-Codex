using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ChongZhenCodexInstaller.Services;

public static class Hashing
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string AsarHeaderSha256(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> prefix = stackalloc byte[8];
        stream.ReadExactly(prefix);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(prefix[4..]);
        if (headerSize <= 8 || headerSize > stream.Length - 8 || headerSize > 32 * 1024 * 1024)
            throw new InvalidDataException($"Invalid ASAR header size: {headerSize}");
        var header = new byte[headerSize];
        stream.ReadExactly(header);
        var jsonSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (jsonSize <= 0 || jsonSize > header.Length - 8)
            throw new InvalidDataException($"Invalid ASAR JSON header size: {jsonSize}");
        return Convert.ToHexString(SHA256.HashData(header.AsSpan(8, jsonSize))).ToLowerInvariant();
    }
}
