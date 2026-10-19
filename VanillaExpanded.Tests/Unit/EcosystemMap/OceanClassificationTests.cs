using System.Buffers.Binary;
using Moq;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks semantic classification, compact persistence and raw GPU flags without generating habitat pixels on the CPU.</summary>
public sealed class OceanClassificationTests
{
    #region Public API
    /// <summary>Native categories distinguish oceans from identical-color freshwater/lava and retain shoreline land and modded ocean blocks.</summary>
    [Fact]
    public void CapturesSemanticSurfaceBeforeDecoration()
    {
        var fixture = new OceanClassificationFixture();
        fixture.Surface = (x, y, z) => x switch { 0 => 2, 1 => 3, 2 => 4, 3 => 1, 4 => 6, _ => 1 };
        var tile = fixture.Classifier.Capture(new(0, 0), fixture.Map.Object)!;
        Assert.False(tile.HasElevation);
        Assert.Equal(new byte[] { 2, 1, 1, 1, 2 }, tile.Ocean!.Columns.Take(5));
        Assert.Equal(EnvironmentalCompleteness.Ocean, tile.Completeness);
        fixture.Blocks.Verify(value => value.GetChunk(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
    }

    /// <summary>Exactly one snow layer is stepped down across a chunk boundary; unavailable/air columns remain unknown.</summary>
    [Fact]
    public void SnowAndMissingSurfaceKeepNativeSelection()
    {
        var fixture = new OceanClassificationFixture();
        fixture.Surface = (x, y, z) => x switch { 0 when y == 32 => 5, 0 => 2, 1 => 0, _ => 1 };
        var tile = fixture.Classifier.Capture(new(0, 0), fixture.Map.Object)!;
        Assert.Equal((byte)2, tile.Ocean!.Columns[0]);
        Assert.Equal((byte)0, tile.Ocean.Columns[1]);
        fixture.Chunks[1].SetupGet(value => value.LoadedFromServer).Returns(false);
        Assert.Null(fixture.Classifier.Capture(new(0, 0), fixture.Map.Object));
        fixture.Chunks[1].SetupGet(value => value.LoadedFromServer).Returns(true);
        fixture.Chunks[0].SetupGet(value => value.Disposed).Returns(true);
        Assert.Null(fixture.Classifier.Capture(new(0, 0), fixture.Map.Object));
    }

    /// <summary>Raw classification and exploration bits remain independent; no height or climate is required to encode known ocean.</summary>
    [Fact]
    public void PacksUnknownLandOceanAndExplorationIndependently()
    {
        var mask = new OceanMask(2, new byte[] { 0, 1, 2, 2 });
        Assert.Equal(new byte[] { 1, 3, 5, 4 }, HabitatCoverageFlags.Pack(new byte[] { 1, 1, 1, 0 }, mask, 2));
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, HabitatCoverageFlags.Pack(new byte[] { 1, 1, 1, 1 }, null, 2));
        Assert.Throws<ArgumentException>(() => new OceanMask(2, new byte[] { 0, 1, 2, 3 }));
        var fixture = new OceanClassificationFixture();
        Assert.Throws<NotSupportedException>(() => new NativeOceanClassifier(fixture.Terrain, fixture.Blocks.Object, fixture.Palette, 16));
    }

    /// <summary>Old height records retain elevations with unknown classification; the new two-bit component roundtrips without heights.</summary>
    [Fact]
    public void CodecKeepsExistingHeightsAndIndependentOcean()
    {
        var key = new EcosystemTileKey(0, 0);
        var original = StorageFixture.Tile(key);
        byte[] old = new byte[12 + 1024 * 2];
        BinaryPrimitives.WriteInt32LittleEndian(old, 1);
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(4), 32);
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(8), 1024);
        for (int i = 0; i < 1024; i++) BinaryPrimitives.WriteUInt16LittleEndian(old.AsSpan(12 + i * 2), original.Elevation[i]);
        var decoded = EnvironmentalRecordCodec.DecodeTile(key, old, 32)!;
        Assert.Equal(original.Elevation, decoded.Elevation);
        Assert.Null(decoded.Ocean);
        var mask = new OceanMask(32, Enumerable.Range(0, 1024).Select(i => (byte)(i % 3)));
        byte[] encoded = EnvironmentalRecordCodec.EncodeTile(new(key, 32, ocean: mask));
        Assert.Equal(16 + 256, encoded.Length);
        decoded = EnvironmentalRecordCodec.DecodeTile(key, encoded, 32)!;
        Assert.False(decoded.HasElevation);
        Assert.Equal(mask.Columns, decoded.Ocean!.Columns);
        encoded[16] = 3;
        Assert.Null(EnvironmentalRecordCodec.DecodeTile(key, encoded, 32)!.Ocean);
    }

    /// <summary>Per-component capture ordinals reject old ocean results without rejecting independently newer heights.</summary>
    [Fact]
    public async Task AcquisitionOrdersTileComponentsIndependently()
    {
        using var fixture = new StorageFixture();
        var storage = fixture.Open();
        var errors = new List<Exception>();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Add);
        var key = new EcosystemTileKey(0, 0);
        var epoch = acquisition.CurrentEpoch;
        long older = acquisition.NextSequence(), newer = acquisition.NextSequence();
        var ocean = new OceanMask(32, Enumerable.Repeat((byte)2, 1024));
        var land = new OceanMask(32, Enumerable.Repeat((byte)1, 1024));
        acquisition.Submit(new(epoch, Tile: new(key, 32, ocean: ocean), Sequence: newer));
        acquisition.Submit(new(epoch, Tile: new(key, 32, StorageFixture.Tile(key).Elevation, land), Sequence: older));
        await acquisition.ShutdownAsync();
        await using var reopened = fixture.Open();
        var tile = (await reopened.ReadTileAsync(key)).Value!;
        Assert.True(tile.HasElevation);
        Assert.Equal(ocean.Columns, tile.Ocean!.Columns);
        Assert.Empty(errors);
    }
    /// <summary>Independent arrivals preserve both components across shutdown/reopen, invalidate tile leases and honor purge epochs.</summary>
    [Fact]
    public async Task PersistenceMergesComponentsAndInvalidatesInputs()
    {
        using var fixture = new StorageFixture();
        var key = new EcosystemTileKey(0, 0);
        var mask = new OceanMask(32, Enumerable.Repeat((byte)2, 1024));
        await using (var storage = fixture.Open())
        {
            using var prep = new GpuHabitatPreparation(storage, GpuHabitatLimits.Client);
            await storage.WriteTileAsync(new(key, 32, ocean: mask), storage.CurrentEpoch);
            prep.TryPrepare(key, out var pending);
            var first = (await pending)!;
            Assert.False(first.Bundle.Elevation.Value!.HasElevation);
            Assert.NotNull(first.Bundle.Elevation.Value.Ocean);
            await storage.WriteTileAsync(StorageFixture.Tile(key), storage.CurrentEpoch);
            Assert.False(prep.IsCurrent(first));
            var merged = (await storage.ReadTileAsync(key)).Value!;
            Assert.True(merged.HasElevation);
            Assert.Equal(mask.Columns, merged.Ocean!.Columns);
        }
        await using var reopened = fixture.Open();
        Assert.NotNull((await reopened.ReadTileAsync(key)).Value!.Ocean);
        var old = reopened.CurrentEpoch;
        await reopened.PurgeAsync();
        Assert.False(await reopened.WriteTileAsync(new(key, 32, ocean: mask), old));
        Assert.Null((await reopened.ReadTileAsync(key)).Value);
    }
    #endregion
}
