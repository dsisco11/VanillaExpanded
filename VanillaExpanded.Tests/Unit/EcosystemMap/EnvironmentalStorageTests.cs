using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises real independent SQLite persistence, versioned partial records and world/dimension isolation.</summary>
public sealed class EnvironmentalStorageTests
{
    #region Public API
    #region Persistent inputs
    /// <summary>Preserves exact map values/layout and tile heights through partial arrivals, reads and a complete close/reopen.</summary>
    [Fact]
    public async Task PartialInputsAndOriginalLayoutsSurviveReopen()
    {
        using var fixture = new StorageFixture();
        var region = new EcosystemRegionKey(7, -2, 3);
        var tileKey = new EcosystemTileKey(112, -32, 3);
        var climate = StorageFixture.Map();
        var forest = new EnvironmentalMap(4, 0, 1, Enumerable.Range(0, 16));
        var shrubs = new EnvironmentalMap(5, 2, 1, Enumerable.Range(0, 25).Select(value => value * 17));
        await using (var store = fixture.Open())
        {
            await store.WaitUntilReadyAsync();
            Assert.Null((await store.ReadRegionAsync(region)).Value);
            await store.WriteRegionAsync(new RegionalEnvironment(region, Climate:climate), store.CurrentEpoch);
            var partial = (await store.ReadRegionAsync(region)).Value!;
            Assert.Equal(EnvironmentalCompleteness.Climate, partial.Completeness);
            Assert.Null(partial.Shrubs);
            Assert.Null((await store.ReadTileAsync(tileKey)).Value);
            await store.WriteRegionAsync(new RegionalEnvironment(region, Shrubs:shrubs), store.CurrentEpoch);
            await store.WriteRegionAsync(new RegionalEnvironment(region, Forest:forest), store.CurrentEpoch);
            await store.WriteTileAsync(StorageFixture.Tile(tileKey), store.CurrentEpoch);
        }
        await using (var reopened = fixture.Open())
        {
            var read = await reopened.ReadRegionAsync(region);
            Assert.True(reopened.IsCurrent(read.Epoch));
            Assert.Equal(EnvironmentalCompleteness.Climate | EnvironmentalCompleteness.Forest | EnvironmentalCompleteness.Shrubs, read.Value!.Completeness);
            Assert.Equal(climate.Values, read.Value.Climate!.Values);
            Assert.Equal(3, read.Value.Climate.Size);
            Assert.Equal(1, read.Value.Climate.TopLeftPadding);
            Assert.Equal(0, read.Value.Climate.BottomRightPadding);
            Assert.Equal(forest.Values, read.Value.Forest!.Values);
            Assert.Equal(1, read.Value.Forest.BottomRightPadding);
            Assert.Equal(shrubs.Values, read.Value.Shrubs!.Values);
            Assert.Equal(2, read.Value.Shrubs.TopLeftPadding);
            var tile = (await reopened.ReadTileAsync(tileKey)).Value!;
            Assert.Equal(StorageFixture.Tile(tileKey).Elevation, tile.Elevation);
            Assert.Equal(EnvironmentalCompleteness.Elevation, tile.Completeness);
        }
    }

    /// <summary>Copies source buffers and rejects invalid layouts before any asynchronous work can observe them.</summary>
    [Fact]
    public void EnvironmentalRecordsOwnTheirArraysAndValidateGeometry()
    {
        int[] values = { -1, 0, 255, int.MaxValue };
        var map = new EnvironmentalMap(2, 0, 0, values);
        values[0] = 999;
        Assert.Equal(-1, map.Values[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)map.Values).Clear());
        ushort[] heights = { 0, ushort.MaxValue, 1, 200 };
        var tile = new TileEnvironment(new(0, 0), 2, heights);
        heights[1] = 0;
        Assert.Equal(ushort.MaxValue, tile.Elevation[1]);
        Assert.Throws<ArgumentException>(() => new EnvironmentalMap(2, 0, 0, new[] { 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnvironmentalMap(2, 1, 1, new int[4]));
        Assert.Throws<ArgumentException>(() => new TileEnvironment(new(0, 0), 2, new ushort[3]));
    }

    /// <summary>Separates independent worlds and dimensions, using digest paths and verifying sampling metadata on reopen.</summary>
    [Fact]
    public async Task WorldAndDimensionIsolationCannotReuseAnotherCache()
    {
        using var fixture = new StorageFixture();
        var other = new EcosystemStorageIdentity("../different/world", 10000, 256, 12000, 110, 512, 32);
        var key = new EcosystemRegionKey(4, 5);
        StorageEpoch oldTicket;
        await using (var first = fixture.Open())
        {
            oldTicket = first.CurrentEpoch;
            await first.WriteRegionAsync(new(key, Climate:StorageFixture.Map()), oldTicket);
            await first.WriteRegionAsync(new(new(4,5,1), Forest:StorageFixture.Map()), oldTicket);
            Assert.Null((await first.ReadRegionAsync(new(4,5,1))).Value!.Climate);
            Assert.Null((await first.ReadRegionAsync(key)).Value!.Forest);
        }
        Assert.NotEqual(fixture.Identity.GetDatabasePath(fixture.Root), other.GetDatabasePath(fixture.Root));
        Assert.StartsWith(Path.Combine(fixture.Root,"ModData","vanillaexpanded","ecosystem"), other.GetDatabasePath(fixture.Root));
        await using (var second = fixture.Open(other))
        {
            Assert.Null((await second.ReadRegionAsync(key)).Value);
            Assert.False(await second.WriteRegionAsync(new(key, Climate:StorageFixture.Map()), oldTicket));
        }
        var mismatch = fixture.Open(new("test-world", 10000, 320, 12000, 110, 512, 32));
        await Assert.ThrowsAsync<InvalidDataException>(() => mismatch.WaitUntilReadyAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => mismatch.ShutdownAsync());
        Assert.Throws<ArgumentException>(() => new EcosystemStorageIdentity("",10000,256,12000,110,512,32));
        Assert.Equal(new EcosystemRegionKey(-1,-2,7), fixture.Identity.RegionFor(new(-1,-17,7)));
    }
    #endregion

    #region Unavailable and compatibility
    /// <summary>Rejects unknown row/component versions independently rather than treating unsupported inputs as valid samples.</summary>
    [Fact]
    public async Task UnknownVersionsAndMalformedComponentsRemainUnavailable()
    {
        using var fixture = new StorageFixture();
        var key = new EcosystemRegionKey(1,2);
        var tile = new EcosystemTileKey(16,32);
        await using (var store = fixture.Open())
        {
            await store.WriteRegionAsync(new(key, StorageFixture.Map(), StorageFixture.Map(), StorageFixture.Map()), store.CurrentEpoch);
            await store.WriteTileAsync(StorageFixture.Tile(tile), store.CurrentEpoch);
        }
        fixture.Execute("UPDATE ecosystem_region SET climate=X'02000000', forest=X'01000000'; UPDATE ecosystem_map_piece SET version=99;");
        await using (var reopened = fixture.Open())
        {
            var region = (await reopened.ReadRegionAsync(key)).Value!;
            Assert.Null(region.Climate);
            Assert.Null(region.Forest);
            Assert.NotNull(region.Shrubs);
            Assert.Equal(EnvironmentalCompleteness.Shrubs, region.Completeness);
            Assert.Null((await reopened.ReadTileAsync(tile)).Value);
        }
        fixture.Execute("UPDATE ecosystem_region SET version=99;");
        await using (var reopened = fixture.Open()) Assert.Null((await reopened.ReadRegionAsync(key)).Value);
        fixture.Execute("PRAGMA user_version=99;");
        var unsupported = fixture.Open();
        await Assert.ThrowsAsync<IOException>(() => unsupported.WaitUntilReadyAsync());
        await Assert.ThrowsAsync<IOException>(() => unsupported.ShutdownAsync());
    }

    /// <summary>Rejects record blobs with unsupported versions, inconsistent lengths or different tile geometry.</summary>
    [Fact]
    public void ComponentCodecsRejectUnsupportedAndCorruptRecords()
    {
        byte[] map = EnvironmentalRecordCodec.EncodeMap(StorageFixture.Map());
        map[0] = 2;
        Assert.Null(EnvironmentalRecordCodec.DecodeMap(map));
        Assert.Null(EnvironmentalRecordCodec.DecodeMap(new byte[0]));
        byte[] tile = EnvironmentalRecordCodec.EncodeTile(StorageFixture.Tile(new(0,0)));
        Assert.Null(EnvironmentalRecordCodec.DecodeTile(new(0,0),tile,16));
        Assert.Null(EnvironmentalRecordCodec.DecodeTile(new(0,0),tile[..^1],32));
    }
    #endregion
    #endregion
}
