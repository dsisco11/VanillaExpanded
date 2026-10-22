using VanillaExpanded.EcosystemMap.Storage;
using VanillaExpanded.EcosystemMap.Gpu;
using Vintagestory.API.Common;
using Moq;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks bounded grouped reads against independent records, repairs and storage lifetime barriers.</summary>
public sealed class PackedReadBatchTests
{
    #region Public API
    /// <summary>Shared regions are deduplicated while missing and partial records preserve their independent keys and values.</summary>
    [Fact]
    public async Task SharedAndPartialRecordsRemainIndependent()
    {
        using var fixture = new StorageFixture();
        await using var store = fixture.Open();
        var region = new EcosystemRegionKey(0, 0);
        var first = new EcosystemTileKey(0, 0);
        var second = new EcosystemTileKey(1, 0);
        var missing = new EcosystemTileKey(2, 0);
        await store.WriteRegionAsync(new(region, Climate: new(2, 0, 1, new[] { 0, 1, 2, 3 })), store.CurrentEpoch);
        await store.WriteTileAsync(StorageFixture.Tile(first), store.CurrentEpoch);
        await store.WriteTileAsync(new(second, 32, ocean: new OceanMask(32, Enumerable.Repeat((byte)1, 1024))), store.CurrentEpoch);
        var result = await store.ReadPackedBatchAsync(new[] { region, region }, new[] { first, second, missing });
        Assert.True(store.IsCurrent(result.Epoch));
        Assert.Single(result.Value!.Regions);
        Assert.NotNull(result.Value.Regions[region]!.Layouts[0]);
        Assert.Null(result.Value.Regions[region]!.Layouts[1]);
        Assert.Equal(ushort.MaxValue, result.Value.Tiles[first]!.Values[0]);
        Assert.Equal(0, result.Value.Tiles[second]!.Values[0]);
        Assert.Null(result.Value.Tiles[missing]);
        Assert.Empty((await store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), Array.Empty<EcosystemTileKey>())).Value!.Tiles);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), Enumerable.Range(0, 5).Select(x => new EcosystemTileKey(x, 0)).ToArray()));
    }

    /// <summary>Damaged packed rows rebuild supported originals without accepting wrongly associated or incompatible originals.</summary>
    [Fact]
    public async Task MalformedRowsRepairWithoutHidingInvalidAssociations()
    {
        using var fixture = new StorageFixture();
        var keys = Enumerable.Range(0, 4).Select(x => new EcosystemTileKey(x, 0)).ToArray();
        await using (var store = fixture.Open())
            foreach (var key in keys) await store.WriteTileAsync(StorageFixture.Tile(key), store.CurrentEpoch);
        fixture.Execute("UPDATE ecosystem_tile_upload SET payload='bad'; UPDATE ecosystem_map_piece SET region_x=99 WHERE x=1; UPDATE ecosystem_map_piece SET version=99 WHERE x=2; DELETE FROM ecosystem_map_piece WHERE x=3;");
        await using var reopened = fixture.Open();
        var result = (await reopened.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), keys)).Value!;
        Assert.Equal(ushort.MaxValue, result.Tiles[keys[0]]!.Values[0]);
        for (int i = 1; i < keys.Length; i++) Assert.Null(result.Tiles[keys[i]]);
        Assert.Equal(result.Tiles[keys[0]]!.Values, (await reopened.ReadTileForUploadAsync(keys[0])).Value!.Values);
    }

    /// <summary>Queued updates and purge retain their established ordering around grouped reads; old receipts cannot survive purge.</summary>
    [Fact]
    public async Task UpdatesAndPurgePreserveBarriers()
    {
        using var fixture = new StorageFixture();
        await using var store = fixture.Open(capacity: 1);
        var key = new EcosystemTileKey(0, 0);
        await store.WriteTileAsync(StorageFixture.Tile(key), store.CurrentEpoch);
        var before = await store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key });
        Task<bool> write = store.WriteTileAsync(new(key, 32, Enumerable.Repeat((ushort)765, 1024)), store.CurrentEpoch);
        var after = await store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key });
        Assert.True(await write);
        Assert.Equal(765, after.Value!.Tiles[key]!.Values[0]);
        var pending = Enumerable.Range(0, 16).Select(_ => store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key })).ToArray();
        await store.PurgeAsync();
        foreach (var read in await Task.WhenAll(pending)) Assert.False(store.IsCurrent(read.Epoch));
        Assert.False(store.IsCurrent(before.Epoch));
        Assert.Null((await store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key })).Value!.Tiles[key]);
    }

    /// <summary>Shutdown drains accepted writes and rejects later read admission without reopening a database connection.</summary>
    [Fact]
    public async Task ShutdownDrainsWritesAndRejectsLateReads()
    {
        using var fixture = new StorageFixture();
        var store = fixture.Open(capacity: 1);
        await store.WaitUntilReadyAsync();
        var key = new EcosystemTileKey(0, 0);
        Task<bool> write = store.WriteTileAsync(StorageFixture.Tile(key), store.CurrentEpoch);
        Task<StorageRead<PackedReadBatch>> read = store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key });
        await store.ShutdownAsync();
        Assert.True(await write);
        Assert.False(store.IsCurrent((await read).Epoch));
        Assert.Null((await store.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key })).Value);
        await using var reopened = fixture.Open();
        Assert.Equal(ushort.MaxValue, (await reopened.ReadPackedBatchAsync(Array.Empty<EcosystemRegionKey>(), new[] { key })).Value!.Tiles[key]!.Values[0]);
    }
    /// <summary>A blocked local repair admits at most five total preparation jobs, and queued commits or purge revoke all prior source leases.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightRepairCannotSurviveUpdateOrPurge(bool purge)
    {
        using var fixture = new StorageFixture();
        var region = new EcosystemRegionKey(0, 0);
        var keys = Enumerable.Range(0, 6).Select(x => new EcosystemTileKey(x, 0)).ToArray();
        await using (var initial = fixture.Open())
        {
            await initial.WriteRegionAsync(new(region, Forest: new(2, 0, 1, new[] { 0, 1, 256, 255 })), initial.CurrentEpoch);
            foreach (var key in keys) await initial.WriteTileAsync(StorageFixture.Tile(key), initial.CurrentEpoch);
        }
        fixture.Execute("UPDATE ecosystem_region_upload SET version=99");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var logger = new Mock<ILogger>();
        logger.Setup(value => value.Warning(It.IsAny<string>())).Callback<string>(_ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test repair was not released.");
        });
        await using var store = fixture.Open(logger: logger.Object);
        using var preparation = new GpuHabitatPreparation(store, new(2, 8, 8, 4));
        preparation.SetView(keys);
        Assert.True(preparation.TryPrepare(keys[0], out var first));
        var receipts = new List<Task<GpuHabitatReceipt?>> { first };
        Task barrier;
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            for (int i = 1; i < 5; i++)
            {
                Assert.True(preparation.TryPrepare(keys[i], out var pending));
                receipts.Add(pending);
            }
            Assert.Equal(5, preparation.Counts.Pending);
            Assert.False(preparation.TryPrepare(keys[5], out _));
            // Both operations are ordered after the blocked read, never inside its active reader.
            barrier = purge ? store.PurgeAsync() : store.WriteRegionAsync(new(region, Forest: new(2, 0, 1, new[] { 0, 1, 2, 255 })), store.CurrentEpoch);
        }
        finally { release.Set(); }
        await barrier.WaitAsync(TimeSpan.FromSeconds(10));
        var completed = await Task.WhenAll(receipts).WaitAsync(TimeSpan.FromSeconds(10));
        if (completed[0] != null) Assert.False(preparation.IsCurrent(completed[0]!));
        // Later queued jobs can correctly acquire fresh source leases after the barrier.
        foreach (var receipt in completed)
        {
            if (receipt == null || !preparation.IsCurrent(receipt)) continue;
            if (purge)
            {
                Assert.Null(receipt.Bundle.Region.Value);
                Assert.All(receipt.Bundle.RegionalTexture!.Layouts, layout => Assert.Null(layout));
                Assert.All(receipt.Bundle.RegionalTexture.Values, value => Assert.Equal(0, value));
            }
            else
            {
                var expected = new PackedRegionalData(new(region, Forest: new(2, 0, 1, new[] { 0, 1, 2, 255 })));
                Assert.Equal(expected.Values, receipt.Bundle.RegionalTexture!.Values);
                Assert.NotNull(receipt.Bundle.RegionalTexture.Layouts[1]);
            }
        }
    }
    #endregion
}




