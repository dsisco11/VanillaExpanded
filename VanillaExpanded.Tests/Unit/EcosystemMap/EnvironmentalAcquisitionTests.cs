using System.Collections.Concurrent;
using Moq;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Storage;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks independent arrivals, ordered copied jobs and durable acquisition shutdown with no GUI or region loading.</summary>
public sealed class EnvironmentalAcquisitionTests
{
    #region Public API
    /// <summary>Clones actual map/height arrays and rejects missing or invalid source layouts without climate fallback APIs.</summary>
    [Fact]
    public void SnapshotsOwnArraysAndKeepMissingInputsUnavailable()
    {
        var values = StorageFixture.Map().Values.ToArray();
        var source = new IntDataMap2D { Size = 3, TopLeftPadding = 1, Data = values };
        var map = EnvironmentalSnapshots.Map(source)!;
        values[0] = 42;
        source.Size = 12;
        Assert.Equal(int.MinValue, map.Values[0]);
        Assert.Null(EnvironmentalSnapshots.Map(source));
        Assert.Null(EnvironmentalSnapshots.Map(null));
        Assert.Null(EnvironmentalSnapshots.Map(IntDataMap2D.CreateEmpty()));
        var region = new Mock<IMapRegion>(MockBehavior.Strict);
        region.SetupGet(value => value.ClimateMap).Returns((IntDataMap2D)null!);
        region.SetupGet(value => value.ForestMap).Returns(new IntDataMap2D { Size = 1, Data = new[] { 255 } });
        var capture = EnvironmentalSnapshots.Region(new(1, 2), region.Object);
        Assert.Null(capture.Climate);
        Assert.NotNull(capture.Forest);
        Assert.Null(capture.Shrubs);
        ushort[] heights = Enumerable.Repeat((ushort)123, 1024).ToArray();
        var chunk = new Mock<IMapChunk>(MockBehavior.Strict);
        chunk.SetupGet(value => value.WorldGenTerrainHeightMap).Returns(heights);
        var tile = EnvironmentalSnapshots.Tile(new(0, 0), chunk.Object, 32)!;
        heights[0] = 44;
        Assert.Equal((ushort)123, tile.Elevation[0]);
        Assert.Null(EnvironmentalSnapshots.Tile(new(0, 0), chunk.Object, 16));
        chunk.SetupGet(value => value.WorldGenTerrainHeightMap).Returns((ushort[])null!);
        Assert.Null(EnvironmentalSnapshots.Tile(new(0, 0), chunk.Object, 32));
    }

    /// <summary>Normal network map capacity can exceed logical map cells without making the original data unavailable.</summary>
    [Fact]
    public void SnapshotCopiesLogicalMapFromOversizedNetworkBuffer()
    {
        var values = Enumerable.Range(0, 16).ToArray();
        var snapshot = EnvironmentalSnapshots.Map(new() { Size = 3, TopLeftPadding = 1, Data = values });
        Assert.NotNull(snapshot);
        Assert.Equal(Enumerable.Range(0, 9), snapshot.Values);
        values[0] = 999;
        Assert.Equal(0, snapshot.Values[0]);
        Assert.Null(EnvironmentalSnapshots.Map(new() { Size = 5, Data = values }));
    }
    /// <summary>Normal tab-closed acquisition merges delayed shrubs and drains both bounded stages before reconnect reads.</summary>
    [Fact]
    public async Task DelayedShrubsAndElevationSurviveAcquisitionShutdown()
    {
        using var fixture = new StorageFixture();
        var storage = fixture.Open(capacity: 1);
        var errors = new ConcurrentQueue<Exception>();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        var key = new EcosystemRegionKey(2, 3);
        var epoch = acquisition.CurrentEpoch;
        Assert.True(acquisition.Submit(new(epoch, new(key, Climate: StorageFixture.Map()), Sequence: acquisition.NextSequence())));
        Assert.True(acquisition.Submit(new(epoch, new(key, Forest: StorageFixture.Map(1)), Sequence: acquisition.NextSequence())));
        var shrubs = ShrubPayload.Decode(ShrubPayload.Encode(key, StorageFixture.Map(2)))!;
        Assert.True(acquisition.Submit(new(epoch, shrubs, Sequence: acquisition.NextSequence())));
        Assert.True(acquisition.Submit(new(epoch, Tile: StorageFixture.Tile(new(32, 48)), Sequence: acquisition.NextSequence())));
        await acquisition.ShutdownAsync();
        await acquisition.ShutdownAsync();
        Assert.False(acquisition.Submit(new(epoch, new(key))));
        Assert.Empty(errors);
        await using var reopened = fixture.Open();
        var saved = (await reopened.ReadRegionAsync(key)).Value!;
        Assert.Equal(EnvironmentalCompleteness.Climate | EnvironmentalCompleteness.Forest | EnvironmentalCompleteness.Shrubs, saved.Completeness);
        Assert.Equal(StorageFixture.Map(2).Values, saved.Shrubs!.Values);
        Assert.NotNull((await reopened.ReadTileAsync(new(32, 48))).Value);
    }

    /// <summary>A newer ordinary packet cannot be overwritten when an older initial snapshot reaches the queue later.</summary>
    [Fact]
    public async Task ReceiptSequencePreservesNewestAvailableComponent()
    {
        using var fixture = new StorageFixture();
        var storage = fixture.Open();
        var errors = new ConcurrentQueue<Exception>();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        var ticket = acquisition.CurrentEpoch;
        var key = new EcosystemRegionKey(0, 0);
        long old = acquisition.NextSequence(), current = acquisition.NextSequence();
        acquisition.Submit(new(ticket, new(key, Climate: StorageFixture.Map(1)), Tile: StorageFixture.Tile(new(0, 0)), Sequence: current));
        acquisition.Submit(new(ticket, new(key, Climate: StorageFixture.Map(), Forest: StorageFixture.Map()), Sequence: old));
        await acquisition.ShutdownAsync();
        await using var reopened = fixture.Open();
        var saved = (await reopened.ReadRegionAsync(key)).Value!;
        Assert.Equal(StorageFixture.Map(1).Values, saved.Climate!.Values);
        Assert.NotNull(saved.Forest);
        Assert.Empty(errors);
    }


    /// <summary>A failed capture observer releases bounded producer waits and still closes durable storage during shutdown.</summary>
    [Fact]
    public async Task CaptureFailureReleasesBackpressureAndClosesStorage()
    {
        using var fixture = new StorageFixture();
        var storage = fixture.Open();
        var acquisition = new EnvironmentalAcquisition(storage, 1, _ => throw new InvalidOperationException("observer failed"));
        var ticket = acquisition.CurrentEpoch;
        acquisition.Submit(new(ticket, Tile: new TileEnvironment(new(0, 0), 1, new ushort[] { 7 })));
        await Task.Run(() =>
        {
            for (int x = 0; x < 20; x++)
                acquisition.Submit(new(ticket, new(new(x, 0), Climate: StorageFixture.Map())));
        }).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => acquisition.ShutdownAsync());
        Assert.False(storage.IsCurrent(ticket));
    }

    /// <summary>Purge rejects every old copied job, including a producer that finishes copying after the clear.</summary>
    [Fact]
    public async Task PurgeRejectsLateCopiedJobsAndNewWorldTickets()
    {
        using var fixture = new StorageFixture();
        var storage = fixture.Open();
        var errors = new ConcurrentQueue<Exception>();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        var original = acquisition.CurrentEpoch;
        for (int x = 0; x < 25; x++)
            acquisition.Submit(new(original, new(new(x, 0), Climate: StorageFixture.Map()), Sequence: acquisition.NextSequence()));
        await storage.PurgeAsync();
        Assert.False(acquisition.Submit(new(original, new(new(99, 0), Shrubs: StorageFixture.Map()))));
        var current = acquisition.CurrentEpoch;
        acquisition.Submit(new(current, new(new(2, 0), Forest: StorageFixture.Map()), Sequence: acquisition.NextSequence()));
        await acquisition.ShutdownAsync();
        Assert.Empty(errors);
        await using var reopened = fixture.Open();
        for (int x = 0; x < 25; x++)
        {
            var region = (await reopened.ReadRegionAsync(new(x, 0))).Value;
            if (x == 2) { Assert.Null(region!.Climate); Assert.NotNull(region.Forest); }
            else Assert.Null(region);
        }
        Assert.False(await reopened.WriteRegionAsync(new(new(99, 0), Climate: StorageFixture.Map()), current));
    }
    #endregion
}
