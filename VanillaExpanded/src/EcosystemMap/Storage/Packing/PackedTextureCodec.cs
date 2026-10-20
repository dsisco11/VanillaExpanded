using System;
using System.Buffers.Binary;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Serializes versioned upload-ready channel buffers independently of original environmental record formats.</summary>
internal static class PackedTextureCodec
{
    internal const int Version = 1;

    #region Public API
    /// <summary>Writes physical size, each original logical layout/offset and upload-ready RGBA bytes.</summary>
    internal static byte[] EncodeRegion(PackedRegionalData data)
    {
        byte[] bytes = new byte[checked(56 + data.Values.Length)];
        Write(bytes, 0, Version); Write(bytes, 4, data.Size);
        for (int i = 0; i < 3; i++)
        {
            var layout = data.Layouts[i];
            Write(bytes, 8 + i * 16, layout?.Size ?? 0);
            Write(bytes, 12 + i * 16, layout?.TopLeftPadding ?? 0);
            Write(bytes, 16 + i * 16, layout?.BottomRightPadding ?? 0);
            Write(bytes, 20 + i * 16, data.Offsets[i]);
        }
        data.Values.CopyTo(bytes, 56);
        return bytes;
    }

    /// <summary>Restores checked layout metadata and bytes directly; original array decoding and channel packing are unnecessary.</summary>
    internal static PackedRegionalData? DecodeRegion(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 56 || Read(bytes, 0) != Version) return null;
        int size = Read(bytes, 4);
        if (size <= 0 || (long)size * size > (bytes.Length - 56L) / 4
            || 56L + (long)size * size * 4 != bytes.Length) return null;
        var layouts = new PackedMapLayout?[3];
        var offsets = new int[3];
        for (int i = 0; i < 3; i++)
        {
            int count = Read(bytes, 8 + i * 16), left = Read(bytes, 12 + i * 16), right = Read(bytes, 16 + i * 16);
            offsets[i] = Read(bytes, 20 + i * 16);
            if (count == 0)
            {
                if (left != 0 || right != 0 || offsets[i] != 0) return null;
                continue;
            }
            if (count < 0 || left < 0 || right < 0 || (long)left + right >= count
                || offsets[i] < 0 || (long)count + offsets[i] > size) return null;
            layouts[i] = new PackedMapLayout(count, left, right);
        }
        return new PackedRegionalData(size, bytes.AsSpan(56).ToArray(), layouts, offsets);
    }

    /// <summary>Writes exact interleaved RG16UI values in a versioned little-endian blob.</summary>
    internal static byte[] EncodeTile(ushort[] values, int size)
    {
        if (size <= 0 || (long)size * size * 2 != values.Length) throw new ArgumentException("Invalid packed terrain layout.");
        byte[] bytes = new byte[checked(12 + values.Length * 2)];
        Write(bytes, 0, Version); Write(bytes, 4, size); Write(bytes, 8, values.Length);
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12 + i * 2), values[i]);
        return bytes;
    }

    /// <summary>Decodes exact height/flag words with bounded geometry and rejects unknown category or flag bits.</summary>
    internal static PackedTerrainData? DecodeTile(byte[]? bytes, int size)
    {
        long count = (long)size * size * 2;
        if (bytes == null || bytes.Length < 12 || size <= 0 || count > int.MaxValue
            || Read(bytes, 0) != Version || Read(bytes, 4) != size || Read(bytes, 8) != count || 12L + count * 2 != bytes.Length) return null;
        var values = new ushort[(int)count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12 + i * 2));
            if ((i & 1) != 0 && ((values[i] & ~7) != 0 || (values[i] & 6) == 6)) return null;
        }
        return new PackedTerrainData(size, values);
    }
    #endregion

    #region Private
    /// <summary>Reads a header word after its enclosing header length has been checked.</summary>
    private static int Read(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    /// <summary>Writes a layout/version header word without host-endian dependencies.</summary>
    private static void Write(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    #endregion
}
