using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using VanillaExpanded.EcosystemMap.Storage;
using VanillaExpanded.EcosystemMap.Transport;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks bounded lossless database envelopes and ordinary reacquisition without altering shrub transport.</summary>
public sealed class EnvironmentalCompressionTests
{
    #region Public API
    #region Compression and malformed inputs
    /// <summary>Preserves signed extrema, independent native layouts and every height/ocean component combination.</summary>
    [Fact]
    public void OriginalRecordsRoundTripExactly()
    {
        foreach (var layout in new[] { (3, 1, 0), (20, 2, 2), (17, 0, 1), (33, 0, 1) })
        {
            int[] values = Enumerable.Range(0, layout.Item1 * layout.Item1)
                .Select(i => (i % 4) switch { 0 => int.MinValue, 1 => int.MaxValue, 2 => -i, _ => i * 257 }).ToArray();
            var map = new EnvironmentalMap(layout.Item1, layout.Item2, layout.Item3, values);
            byte[] original = EnvironmentalRecordCodec.EncodeMap(map);
            byte[] restored = EnvironmentalCompression.Decode(EnvironmentalCompression.Encode(original))!;
            Assert.Equal(original, restored);
            var decoded = EnvironmentalRecordCodec.DecodeMap(restored)!;
            Assert.Equal(values, decoded.Values);
            Assert.Equal(layout.Item2, decoded.TopLeftPadding);
            Assert.Equal(layout.Item3, decoded.BottomRightPadding);
        }
        var key = new EcosystemTileKey(-5, 8, 3);
        ushort[] heights = Enumerable.Range(0, 1024).Select(i => (ushort)(i % 2 == 0 ? 0 : ushort.MaxValue)).ToArray();
        var ocean = new OceanMask(32, Enumerable.Range(0, 1024).Select(i => (byte)(i % 3)));
        foreach (bool height in new[] { false, true })
        foreach (bool categories in new[] { false, true })
        {
            var tile = new TileEnvironment(key, 32, height ? heights : null, categories ? ocean : null);
            byte[] raw = EnvironmentalRecordCodec.EncodeTile(tile);
            var decoded = EnvironmentalRecordCodec.DecodeTile(key, EnvironmentalCompression.Decode(EnvironmentalCompression.Encode(raw)), 32)!;
            Assert.Equal(tile.Key, decoded.Key);
            Assert.Equal(tile.Completeness, decoded.Completeness);
            Assert.Equal(tile.Elevation, decoded.Elevation);
            Assert.Equal(tile.Ocean?.Columns, decoded.Ocean?.Columns);
        }
    }

    /// <summary>Malformed headers, truncation, output-size lies and damaged contents never expose partial or fabricated samples.</summary>
    [Fact]
    public void InvalidEnvelopesRemainUnavailable()
    {
        byte[] original = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        byte[] encoded = EnvironmentalCompression.Encode(original);
        Assert.Null(EnvironmentalCompression.Decode(null));
        Assert.Null(EnvironmentalCompression.Decode(Array.Empty<byte>()));
        Assert.Null(EnvironmentalCompression.Decode(original));
        for (int length = 0; length < encoded.Length; length++)
            Assert.Null(EnvironmentalCompression.Decode(encoded[..length]));
        Assert.Null(EnvironmentalCompression.Decode(encoded.Concat(new byte[] { 1 }).ToArray()));
        Assert.Null(EnvironmentalCompression.Decode(encoded, original.Length - 1));
        foreach (int length in new[] { -1, 0, original.Length - 1, original.Length + 1, int.MaxValue })
        {
            byte[] damaged = (byte[])encoded.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(4), length);
            Assert.Null(EnvironmentalCompression.Decode(damaged));
        }
        byte[] wrongVersion = (byte[])encoded.Clone();
        wrongVersion[0] ^= 1;
        Assert.Null(EnvironmentalCompression.Decode(wrongVersion));
        byte[] badChecksum = (byte[])encoded.Clone();
        badChecksum[12] ^= 1;
        Assert.Null(EnvironmentalCompression.Decode(badChecksum));
        byte[] badStream = (byte[])encoded.Clone();
        Array.Fill(badStream, (byte)255, EnvironmentalCompression.HeaderSize, badStream.Length - EnvironmentalCompression.HeaderSize);
        Assert.Null(EnvironmentalCompression.Decode(badStream));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentalCompression.Encode(Array.Empty<byte>()));
    }
    #endregion

    #region Database boundary
    /// <summary>SQLite contains compressed blobs; a corrupt component stays independent and ordinary capture replaces incompatible records.</summary>
    [Fact]
    public async Task DatabaseCompressesAndReacquiresWithoutLegacyReaders()
    {
        using var fixture = new StorageFixture();
        var key = new EcosystemRegionKey(2, 3, 1);
        var tileKey = new EcosystemTileKey(32, 48, 1);
        var map = new EnvironmentalMap(20, 2, 2, Enumerable.Repeat(0x123456, 400));
        var mask = new OceanMask(32, Enumerable.Repeat((byte)2, 1024));
        await using (var storage = fixture.Open())
        {
            await storage.WriteRegionAsync(new(key, map, map, map), storage.CurrentEpoch);
            await storage.WriteTileAsync(new(tileKey, 32, Enumerable.Repeat((ushort)110, 1024), mask), storage.CurrentEpoch);
        }
        using (var connection = new SqliteConnection("Data Source=" + fixture.Identity.GetDatabasePath(fixture.Root) + ";Pooling=false"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT version,climate FROM ecosystem_region";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(EcosystemDatabase.RegionalRecordVersion, reader.GetInt32(0));
            byte[] blob = (byte[])reader.GetValue(1);
            Assert.True(blob.Length < EnvironmentalRecordCodec.EncodeMap(map).Length);
            Assert.Equal(EnvironmentalRecordCodec.EncodeMap(map), EnvironmentalCompression.Decode(blob));
        }
        // Damage only climate. Forest/shrubs must survive, and a new climate arrival must retain them.
        fixture.Execute("UPDATE ecosystem_region SET climate=X'00000000';");
        await using (var storage = fixture.Open())
        {
            var partial = (await storage.ReadRegionAsync(key)).Value!;
            Assert.Null(partial.Climate);
            Assert.Equal(map.Values, partial.Forest!.Values);
            Assert.Equal(map.Values, partial.Shrubs!.Values);
            await storage.WriteRegionAsync(new(key, Climate: map), storage.CurrentEpoch);
        }
        fixture.Execute("UPDATE ecosystem_region SET version=1; UPDATE ecosystem_map_piece SET version=2;");
        await using (var storage = fixture.Open())
        {
            Assert.Null((await storage.ReadRegionAsync(key)).Value);
            Assert.Null((await storage.ReadTileAsync(tileKey)).Value);
            await storage.WriteRegionAsync(new(key, Shrubs: map), storage.CurrentEpoch);
            await storage.WriteTileAsync(new(tileKey, 32, ocean: mask), storage.CurrentEpoch);
        }
        await using (var storage = fixture.Open())
        {
            var region = (await storage.ReadRegionAsync(key)).Value!;
            Assert.Null(region.Climate);
            Assert.Null(region.Forest);
            Assert.Equal(map.Values, region.Shrubs!.Values);
            var tile = (await storage.ReadTileAsync(tileKey)).Value!;
            Assert.False(tile.HasElevation);
            Assert.Equal(mask.Columns, tile.Ocean!.Columns);
        }
    }

    /// <summary>Database compression never changes the original version-1 map-layer shrub payload.</summary>
    [Fact]
    public void ShrubWireFormatRemainsRaw()
    {
        var map = StorageFixture.Map();
        byte[] payload = ShrubPayload.Encode(new(2, -3), map);
        Assert.Equal(EnvironmentalRecordCodec.EncodeMap(map), payload[12..]);
        Assert.Equal(map.Values, ShrubPayload.Decode(payload)!.Shrubs!.Values);
        Assert.Null(EnvironmentalCompression.Decode(payload[12..]));
    }
    #endregion
    #endregion
}
