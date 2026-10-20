using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using Moq;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks background prepacking, durable upload-buffer reuse, atomic component refresh and worker-only local rebuilding.</summary>
public sealed class PackedTextureStorageTests
{
    #region Public API
    /// <summary>Invalid-density diagnostics prove packing runs away from a dedicated producer; reopen reads reuse saved bytes without packing again.</summary>
    [Fact]
    public async Task WritesPackOnWorkerAndReopenReusesStoredBuffers()
    {
        using var fixture = new StorageFixture();
        var logger = new Mock<ILogger>();
        int packingThread = 0, warnings = 0;
        logger.Setup(value => value.Warning(It.IsAny<string>())).Callback<string>(_ =>
        {
            Interlocked.Exchange(ref packingThread, Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref warnings);
        });
        var key = new EcosystemRegionKey(2, 3);
        var invalid = new EnvironmentalMap(2, 0, 1, new[] { 0, 1, 256, 255 });
        byte[] expected;
        await using (var store = fixture.Open(logger: logger.Object))
        {
            int producer = await WriteOnDedicatedProducerAsync(store, new(key, Climate: Climate(), Forest: invalid)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(producer, packingThread);
            Assert.Equal(1, warnings);
            var read = (await store.ReadRegionForUploadAsync(key)).Value!;
            expected = read.Values;
            Assert.Null(read.Layouts[1]);
            Assert.Equal(invalid.Values, (await store.ReadRegionAsync(key)).Value!.Forest!.Values);
            Assert.Equal(1, warnings);
            Assert.Equal(1L, Count(fixture, "ecosystem_region_upload"));
        }
        await using (var store = fixture.Open(logger: logger.Object))
        {
            var read = (await store.ReadRegionForUploadAsync(key)).Value!;
            Assert.Equal(expected, read.Values);
            Assert.Equal(1, warnings);
        }
    }

    /// <summary>Partial arrivals refresh both cached formats and preparation passes their exact arrays through; purge clears original and derived rows.</summary>
    [Fact]
    public async Task UpdatesRefreshPackedBuffersAndPurgeRevokesThem()
    {
        using var fixture = new StorageFixture();
        await using var store = fixture.Open();
        var key = new EcosystemTileKey(0, 0);
        var forest = new EnvironmentalMap(17, 0, 1, Enumerable.Repeat(128, 289));
        var shrubs = new EnvironmentalMap(33, 0, 1, Enumerable.Repeat(255, 1089));
        await store.WriteRegionAsync(new(default, Climate: Climate()), store.CurrentEpoch);
        var first = (await store.ReadRegionForUploadAsync(default)).Value!;
        Assert.Null(first.Layouts[1]);
        await store.WriteRegionAsync(new(default, Forest: forest, Shrubs: shrubs), store.CurrentEpoch);
        await store.WriteTileAsync(StorageFixture.Tile(key), store.CurrentEpoch);
        var ocean = new OceanMask(32, Enumerable.Range(0, 1024).Select(i => (byte)(i % 3)));
        await store.WriteTileAsync(new(key, 32, ocean: ocean), store.CurrentEpoch);
        using var preparation = new GpuHabitatPreparation(store, new(2, 4, 4, 2));
        Assert.True(preparation.TryPrepare(key, out var pending));
        var receipt = (await pending)!;
        Assert.Same(receipt.Bundle.Region.Value, receipt.Bundle.RegionalTexture);
        Assert.Same(receipt.Bundle.Elevation.Value!.Values, receipt.Bundle.TerrainTexture);
        var region = receipt.Bundle.RegionalTexture!;
        Assert.Equal(new[] { 0, 2, 0 }, region.Offsets);
        Assert.Equal(new PackedRegionalData(new(default, Climate(), forest, shrubs)).Values, region.Values);
        Assert.Equal(PackedTerrainData.Pack(new(key, 32, StorageFixture.Tile(key).Elevation, ocean), 32), receipt.Bundle.TerrainTexture);
        Assert.Equal(1L, Count(fixture, "ecosystem_tile_upload"));
        await store.PurgeAsync();
        Assert.False(preparation.IsCurrent(receipt));
        Assert.Null((await store.ReadRegionForUploadAsync(default)).Value);
        Assert.Null((await store.ReadTileForUploadAsync(key)).Value);
        Assert.Equal(0L, Count(fixture, "ecosystem_region_upload"));
        Assert.Equal(0L, Count(fixture, "ecosystem_tile_upload"));
    }

    /// <summary>Derived format changes or damage rebuild from supported local originals, never from missing/unsupported original rows.</summary>
    [Fact]
    public async Task MissingOrDamagedUploadRecordsRebuildLocally()
    {
        using var fixture = new StorageFixture();
        var key = new EcosystemTileKey(0, 0);
        await using (var store = fixture.Open())
        {
            await store.WriteRegionAsync(new(default, Climate: Climate()), store.CurrentEpoch);
            await store.WriteTileAsync(StorageFixture.Tile(key), store.CurrentEpoch);
        }
        fixture.Execute("UPDATE ecosystem_region_upload SET version=99; UPDATE ecosystem_tile_upload SET payload='invalid-cache';");
        await using (var store = fixture.Open())
        {
            Assert.Equal(new PackedRegionalData(new(default, Climate: Climate())).Values,
                (await store.ReadRegionForUploadAsync(default)).Value!.Values);
            Assert.Equal(PackedTerrainData.Pack(StorageFixture.Tile(key), 32),
                (await store.ReadTileForUploadAsync(key)).Value!.Values);
        }
        fixture.Execute("DELETE FROM ecosystem_region_upload; DELETE FROM ecosystem_tile_upload; UPDATE ecosystem_region SET version=99; UPDATE ecosystem_map_piece SET version=99;");
        await using (var store = fixture.Open())
        {
            Assert.Null((await store.ReadRegionForUploadAsync(default)).Value);
            Assert.Null((await store.ReadTileForUploadAsync(key)).Value);
            Assert.Equal(0L, Count(fixture, "ecosystem_region_upload"));
            Assert.Equal(0L, Count(fixture, "ecosystem_tile_upload"));
        }
    }

    /// <summary>Upload metadata and full ushort words survive encoding; malformed dimensions, offsets and flag categories fail closed.</summary>
    [Fact]
    public void UploadCodecRejectsInvalidLayoutsAndFlags()
    {
        var original = new RegionalEnvironment(default, Climate: Climate());
        var packed = new PackedRegionalData(original);
        byte[] bytes = PackedTextureCodec.EncodeRegion(packed);
        Assert.Equal(packed.Values, PackedTextureCodec.DecodeRegion(bytes)!.Values);
        Assert.Null(PackedTextureCodec.DecodeRegion(bytes[..^1]));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), int.MaxValue);
        Assert.Null(PackedTextureCodec.DecodeRegion(bytes));
        ushort[] terrain = PackedTerrainData.Pack(StorageFixture.Tile(default), 32);
        byte[] tile = PackedTextureCodec.EncodeTile(terrain, 32);
        Assert.Equal(terrain, PackedTextureCodec.DecodeTile(tile, 32)?.Values);
        Assert.Null(PackedTextureCodec.DecodeTile(tile, 16));
        BinaryPrimitives.WriteUInt16LittleEndian(tile.AsSpan(14), 6);
        Assert.Null(PackedTextureCodec.DecodeTile(tile, 32));
    }
    #endregion

    #region Private
    /// <summary>Provides a native climate layout with varied original channel bytes.</summary>
    private static EnvironmentalMap Climate() => new(20, 2, 2, Enumerable.Range(0, 400).Select(i => ((i & 255) << 16) | (((255-i) & 255) << 8)));
    /// <summary>Keeps the calling producer thread occupied while the storage worker performs the accepted write.</summary>
    private static Task<int> WriteOnDedicatedProducerAsync(EnvironmentalStorage store, RegionalEnvironment record)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = new Thread(() =>
        {
            int thread = Environment.CurrentManagedThreadId;
            try { store.WriteRegionAsync(record, store.CurrentEpoch).GetAwaiter().GetResult(); completion.SetResult(thread); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true };
        producer.Start();
        return completion.Task;
    }
    /// <summary>Inspects only the fixture's dedicated feature database after the corresponding worker operation completes.</summary>
    private static long Count(StorageFixture fixture, string table)
    {
        using var connection = new SqliteConnection("Data Source=" + fixture.Identity.GetDatabasePath(fixture.Root) + ";Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long)command.ExecuteScalar()!;
    }
    #endregion
}
