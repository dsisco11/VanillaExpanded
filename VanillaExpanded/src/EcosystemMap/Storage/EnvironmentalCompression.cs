using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Frames database-only Deflate payloads with bounded lengths and an accidental-corruption checksum.</summary>
internal static class EnvironmentalCompression
{
    // VED1 identifies the current Deflate envelope; it is independent of raw component and network versions.
    private const int Magic = 0x31444556;
    internal const int HeaderSize = 20;
    internal const int MaximumDecodedLength = 64 * 1024 * 1024;

    #region Public API
    /// <summary>Compresses an exact serialized component with the measured Optimal setting; unsupported sizes fail before writing.</summary>
    internal static byte[] Encode(byte[] original)
    {
        if (original.Length == 0 || original.Length > MaximumDecodedLength)
            throw new ArgumentOutOfRangeException(nameof(original), "Environmental payload exceeds the supported decoded size.");
        using var output = new MemoryStream();
        output.Position = HeaderSize;
        // Finish the stream before recording its length: final Deflate blocks are emitted on disposal.
        using (var encoder = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true)) encoder.Write(original);
        byte[] encoded = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(encoded, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(4), original.Length);
        BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(8), encoded.Length - HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(encoded.AsSpan(12), Checksum(original));
        return encoded;
    }

    /// <summary>Rejects unsupported, truncated, oversized or damaged cached payloads without publishing partial output.</summary>
    internal static byte[]? Decode(byte[]? encoded, int maximumDecodedLength = MaximumDecodedLength)
    {
        if (encoded == null || encoded.Length <= HeaderSize || BinaryPrimitives.ReadInt32LittleEndian(encoded) != Magic) return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4));
        int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(8));
        if (length <= 0 || length > Math.Min(maximumDecodedLength, MaximumDecodedLength)
            || compressedLength != encoded.Length - HeaderSize) return null;
        byte[] decoded = new byte[length];
        try
        {
            using var input = new MemoryStream(encoded, HeaderSize, compressedLength, writable: false);
            using var decoder = new DeflateStream(input, CompressionMode.Decompress);
            // Exact allocation and one extra read bound decompression, including streams advertising too little output.
            decoder.ReadExactly(decoded);
            if (decoder.ReadByte() != -1 || Checksum(decoded) != BinaryPrimitives.ReadUInt64LittleEndian(encoded.AsSpan(12))) return null;
            return decoded;
        }
        catch (InvalidDataException) { return null; }
        catch (EndOfStreamException) { return null; }
    }
    #endregion

    #region Private
    /// <summary>Computes FNV-1a over original bytes to detect accidental damage, without a cryptographic authenticity claim.</summary>
    private static ulong Checksum(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in bytes) hash = unchecked((hash ^ value) * 1099511628211UL);
        return hash;
    }
    #endregion
}
