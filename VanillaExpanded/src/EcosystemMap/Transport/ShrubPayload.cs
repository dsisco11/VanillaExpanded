using System;
using System.Buffers.Binary;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Transport;

/// <summary>A versioned map-layer payload containing original two-dimensional regional shrub values and layout.</summary>
internal static class ShrubPayload
{
    internal const int Version = 1;

    #region Public API
    /// <summary>Encodes owned data without resampling or interpreting integer density bits.</summary>
    internal static byte[] Encode(EcosystemRegionKey key, EnvironmentalMap shrubs)
    {
        if (key.Dimension != 0) throw new ArgumentException("Ordinary map-region packets are two-dimensional.", nameof(key));
        byte[] map = EnvironmentalRecordCodec.EncodeMap(shrubs);
        byte[] payload = new byte[checked(12 + map.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(payload, Version);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), key.X);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), key.Z);
        map.CopyTo(payload, 12);
        return payload;
    }

    /// <summary>Validates versions, exact length and layout before exposing any sample; allocation is bounded by received bytes.</summary>
    internal static RegionalEnvironment? Decode(byte[]? payload)
    {
        if (payload == null || payload.Length < 32 || BinaryPrimitives.ReadInt32LittleEndian(payload) != Version) return null;
        int size = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(16));
        int count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(28));
        if (size <= 0 || (long)size * size != count || 32L + count * 4L != payload.Length) return null;
        EnvironmentalMap? map = EnvironmentalRecordCodec.DecodeMap(payload.AsSpan(12).ToArray());
        return map == null ? null : new RegionalEnvironment(new(
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8))), Shrubs: map);
    }
    #endregion
}
