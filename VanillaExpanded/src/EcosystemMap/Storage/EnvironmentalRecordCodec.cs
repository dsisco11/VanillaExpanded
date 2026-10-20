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

    #region Tile inputs
    /// <summary>Preserves every unsigned height and the grid size, without confusing terrain height with surface candidate Y.</summary>
    internal static byte[] EncodeTile(TileEnvironment tile)
    {
        int oceanLength = tile.Ocean == null ? 0 : (tile.Ocean.Columns.Count + 3) / 4;
        int offset = checked(12 + tile.Elevation.Count * 2);
        byte[] bytes = new byte[checked(offset + 4 + oceanLength)];
        WriteInt(bytes, 0, TileEnvironment.Version);
        WriteInt(bytes, 4, tile.Size);
        WriteInt(bytes, 8, tile.Elevation.Count);
        for (int i = 0; i < tile.Elevation.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12 + i * 2, 2), tile.Elevation[i]);
        // Two bits preserve unknown/non-ocean/ocean states without storing any species-dependent result.
        WriteInt(bytes, offset, oceanLength);
        if (tile.Ocean != null)
            for (int i = 0; i < tile.Ocean.Columns.Count; i++)
                bytes[offset + 4 + i / 4] |= (byte)(tile.Ocean.Columns[i] << ((i % 4) * 2));
        return bytes;
    }

    /// <summary>Decodes only the current raw tile layout, with independently available height and semantic classification.</summary>
    internal static TileEnvironment? DecodeTile(EcosystemTileKey key, byte[]? bytes, int expectedSize)
    {
        if (bytes == null || bytes.Length < 12 || ReadInt(bytes, 4) != expectedSize || expectedSize <= 0) return null;
        int version = ReadInt(bytes, 0), count = ReadInt(bytes, 8);
        long cells = (long)expectedSize * expectedSize;
        if (version != TileEnvironment.Version) return null;
        if (count < 0 || (count != cells && count != 0)) return null;
        long offset = 12L + count * 2L;
        if (offset > bytes.Length) return null;
        ushort[] values = new ushort[count];
        for (int i = 0; i < count; i++) values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12 + i * 2, 2));
        OceanMask? ocean = null;
        if (offset + 4 > bytes.Length) return null;
        int length = ReadInt(bytes, (int)offset);
        if (length < 0 || offset + 4 + length != bytes.Length) return null;
        if (length != 0)
        {
            // A damaged optional component must not erase otherwise valid terrain heights.
            if (length == (cells + 3) / 4)
            {
                byte[] columns = new byte[(int)cells];
                bool valid = true;
                for (int i = 0; i < columns.Length; i++)
                {
                    columns[i] = (byte)((bytes[(int)offset + 4 + i / 4] >> ((i % 4) * 2)) & 3);
                    valid &= columns[i] <= 2;
                }
                if (valid) ocean = new OceanMask(expectedSize, columns);
            }
        }
        return new TileEnvironment(key, expectedSize, values, ocean);
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
