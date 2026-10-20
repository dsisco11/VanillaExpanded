using Moq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Verifies raw GPU input reuse and uniform-only criteria changes without pretending to execute a graphics shader.</summary>
public sealed class GpuHabitatTests
{
    #region Public API
    /// <summary>Retains original arrays/layout, reuses bundles across views, and invalidates only affected source data.</summary>
    [Fact]
    public async Task RawInputsAreReusedWithoutCpuSuitabilityGrids()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        var original = StorageFixture.Map();
        await storage.WriteRegionAsync(new(default, original, original, original), storage.CurrentEpoch);
        await storage.WriteTileAsync(StorageFixture.Tile(default), storage.CurrentEpoch);
        using var preparation = new GpuHabitatPreparation(storage, new(2, 4, 4, 2));
        var first = (await Prepare(preparation, default))!;
        Assert.Equal(original.Values, first.Bundle.Region.Value!.Climate!.Values);
        Assert.Equal(original.TopLeftPadding, first.Bundle.Region.Value.Climate.TopLeftPadding);
        Assert.Equal(ushort.MaxValue, first.Bundle.Elevation.Value!.Elevation[0]);
        var work = preparation.Work;
        preparation.CancelPresentation();
        Assert.False(preparation.IsCurrent(first));
        var next = (await Prepare(preparation, default))!;
        Assert.Same(first.Bundle, next.Bundle);
        Assert.Equal(work, preparation.Work);
        await storage.WriteRegionAsync(new(new(2, 2), original), storage.CurrentEpoch);
        Assert.True(preparation.IsCurrent(next));
        await storage.WriteRegionAsync(new(default, Shrubs: StorageFixture.Map(1)), storage.CurrentEpoch);
        Assert.False(preparation.IsCurrent(next));
        Assert.Equal(StorageFixture.Map(1).Values, (await Prepare(preparation, default))!.Bundle.Region.Value!.Shrubs!.Values);
    }

    /// <summary>Missing inputs remain explicit, while purge/close revoke late upload receipts without closing durable storage.</summary>
    [Fact]
    public async Task MissingPurgeAndSuspendDoNotFabricateInputs()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(1, 2, 2, 1));
        var missing = (await Prepare(preparation, default))!;
        Assert.Null(missing.Bundle.Region.Value);
        Assert.Null(missing.Bundle.Elevation.Value);
        await storage.PurgeAsync();
        Assert.False(preparation.IsCurrent(missing));
        preparation.Reset();
        Assert.Equal((0, 0, 0), (preparation.Counts.Regions, preparation.Counts.SourceTiles, preparation.Counts.Bundles));
        preparation.CancelPresentation(true);
        Assert.False(preparation.TryPrepare(default, out _));
        Assert.True(await storage.WriteRegionAsync(new(default, Climate: StorageFixture.Map()), storage.CurrentEpoch));
    }

    /// <summary>Admission and LRU bounds apply to original input bundles, with no species-specific work or unbounded wait queue.</summary>
    [Fact]
    public async Task RawReadJobsRemainBounded()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 3, 3, 2));
        var jobs = new List<Task<GpuHabitatReceipt?>>();
        for (int i = 0; i < 50; i++)
        {
            if (preparation.TryPrepare(new(i, 0), out var task)) jobs.Add(task);
            Assert.InRange(preparation.Counts.Pending, 0, 3);
        }
        await Task.WhenAll(jobs);
        for (int i = 0; i < 10; i++) await Prepare(preparation, new(i * 16, 0));
        Assert.InRange(preparation.Counts.Regions, 0, 2);
        Assert.InRange(preparation.Counts.SourceTiles, 0, 3);
        Assert.InRange(preparation.Counts.Bundles, 0, 3);
    }

    /// <summary>One chosen criteria set updates scalar/range uniforms only; it never serializes a species or rule array.</summary>
    [Fact]
    public void ActiveRangesOnlySetComparisonUniforms()
    {
        var rule = new HabitatRule("game:test", HabitatSpawnPath.Worldgen, -5, 20, .2f, .8f, .1f, .9f, .3f, .7f, .4f, .5f, 1.5f);
        var ranges = HabitatRanges.From(rule);
        var shader = new Mock<IShaderProgram>(MockBehavior.Strict);
        shader.Setup(value => value.Uniform("temperatureRange", -5f, 20f));
        shader.Setup(value => value.Uniform("rainfallRange", .2f, .8f));
        shader.Setup(value => value.Uniform("forestRange", .1f, .9f));
        shader.Setup(value => value.Uniform("shrubRange", .3f, .7f));
        shader.Setup(value => value.Uniform("minForestOrShrubs", .4f));
        shader.Setup(value => value.Uniform("samplingPath", 0));
        ranges.Apply(shader.Object);
        shader.VerifyAll();
    }
    #endregion

    #region Private
    /// <summary>Models bounded presentation retry without creating background work for a full queue.</summary>
    private static async Task<GpuHabitatReceipt?> Prepare(GpuHabitatPreparation preparation, EcosystemTileKey key)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (preparation.TryPrepare(key, out var task)) return await task.WaitAsync(timeout.Token);
            await Task.Delay(1, timeout.Token);
        }
    }
    #endregion
}
