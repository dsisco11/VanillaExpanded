using System;
using System.Buffers.Binary;
using System.IO;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Encodes original values and layouts with explicit component versions, rejecting unsupported/corrupt cached inputs.</summary>
internal static class EnvironmentalRecordCodec
{
    #region Public API
    #region Regional maps
    /// <summary>Writes all integer bits and sampling layout in a versioned little-endian blob.</summary>
    internal static byte[] EncodeMap(EnvironmentalMap map)
    {
        byte[] bytes = new byte[checked(20 + map.Values.Count * 4)];
        WriteInt(bytes, 0, EnvironmentalMap.Version);
        WriteInt(bytes, 4, map.Size);
        WriteInt(bytes, 8, map.TopLeftPadding);
        WriteInt(bytes, 12, map.BottomRightPadding);
        WriteInt(bytes, 16, map.Values.Count);
        for (int i = 0; i < map.Values.Count; i++) WriteInt(bytes, 20 + i * 4, map.Values[i]);
        return bytes;
    }

    /// <summary>Returns unavailable for an unknown component version, malformed length, or invalid sampling layout.</summary>
    internal static EnvironmentalMap? DecodeMap(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 20 || ReadInt(bytes, 0) != EnvironmentalMap.Version) return null;
        int count = ReadInt(bytes, 16);
        if (count < 0 || 20L + count * 4L != bytes.Length) return null;
        int[] values = new int[count];
        for (int i = 0; i < count; i++) values[i] = ReadInt(bytes, 20 + i * 4);
        try { return new EnvironmentalMap(ReadInt(bytes, 4), ReadInt(bytes, 8), ReadInt(bytes, 12), values); }
        catch (ArgumentException) { return null; }
    }
    #endregion

    #region Tile elevation
    /// <summary>Preserves every unsigned height and the grid size, without confusing terrain height with surface candidate Y.</summary>
    internal static byte[] EncodeTile(TileEnvironment tile)
    {
        byte[] bytes = new byte[checked(12 + tile.Elevation.Count * 2)];
        WriteInt(bytes, 0, TileEnvironment.Version);
        WriteInt(bytes, 4, tile.Size);
        WriteInt(bytes, 8, tile.Elevation.Count);
        for (int i = 0; i < tile.Elevation.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12 + i * 2, 2), tile.Elevation[i]);
        return bytes;
    }

    /// <summary>Validates the complete blob and geometry before exposing a tile as an available input.</summary>
    internal static TileEnvironment? DecodeTile(EcosystemTileKey key, byte[]? bytes, int expectedSize)
    {
        if (bytes == null || bytes.Length < 12 || ReadInt(bytes, 0) != TileEnvironment.Version || ReadInt(bytes, 4) != expectedSize)
            return null;
        int count = ReadInt(bytes, 8);
        if (count < 0 || 12L + count * 2L != bytes.Length || (long)expectedSize * expectedSize != count) return null;
        ushort[] values = new ushort[count];
        for (int i = 0; i < count; i++) values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12 + i * 2, 2));
        return new TileEnvironment(key, expectedSize, values);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reads a fixed-width signed integer after the caller has validated the blob length.</summary>
    private static int ReadInt(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    /// <summary>Writes a fixed-width signed integer without altering the original map's bits.</summary>
    private static void WriteInt(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    #endregion
}
