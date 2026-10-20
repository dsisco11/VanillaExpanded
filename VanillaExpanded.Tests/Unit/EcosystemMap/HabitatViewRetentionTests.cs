using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Moq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Presentation;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises incremental view membership and source retention through real local preparation and presentation owners.</summary>
public sealed class HabitatViewRetentionTests
{
    #region Public API
    /// <summary>Entering tiles cannot evict colder overlapping tiles, including reverse pans and shrink above cache floors.</summary>
    [Fact]
    public async Task OverlappingPansAndShrinkPreserveDrawableReceipts()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 3, 3, 2));
        var frame = new Dictionary<EcosystemTileKey, (GpuHabitatReceipt Receipt, HabitatMapTile Tile)>();
        var graphics = Graphics(frame);
        using var presentation = new HabitatMapPresentation(preparation, graphics.Object, error => throw error);
        var first = View(0, 1, 2, 3, 4, 5);
        await Settle(presentation, frame, first);
        foreach (var next in new[] { View(6, 1, 2, 3, 4, 5), View(0, 1, 2, 3, 4, 5), View(7, 1, 2) })
        {
            var prior = frame.ToDictionary();
            var work = preparation.Work;
            frame.Clear(); presentation.Render(next, Ranges(), new float[16]);
            // Check the very first changed-view frame; eventual convergence would hide the original regression.
            foreach (var tile in next.Where(tile => prior.ContainsKey(tile.Key)))
            {
                Assert.True(frame.ContainsKey(tile.Key));
                Assert.Same(prior[tile.Key].Receipt, frame[tile.Key].Receipt);
                Assert.True(preparation.IsCurrent(frame[tile.Key].Receipt));
            }
            await Settle(presentation, frame, next);
            Assert.Equal(work.Regions + 1, preparation.Work.Regions);
            Assert.Equal(work.Tiles + 1, preparation.Work.Tiles);
            Assert.Equal(work.Bundles + 1, preparation.Work.Bundles);
            Assert.Equal(next.Length, preparation.Counts.SourceTiles);
        }
    }

    /// <summary>Screen-only changes and coverage removal/re-entry reuse inputs while removed chunks never reach drawing.</summary>
    [Fact]
    public async Task PlacementAndCoverageAreIndependentOfSourceValidity()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(4, 4, 4, 2));
        var frame = new Dictionary<EcosystemTileKey, (GpuHabitatReceipt Receipt, HabitatMapTile Tile)>();
        using var presentation = new HabitatMapPresentation(preparation, Graphics(frame).Object, error => throw error);
        var view = View(0, 1);
        await Settle(presentation, frame, view);
        var receipts = frame.ToDictionary();
        var work = preparation.Work;
        var moved = view.Select(tile => tile with { X = 123, Y = 456, Width = 17, Height = 17 }).ToArray();
        frame.Clear(); presentation.Render(moved, Ranges(), new float[16]);
        foreach (var tile in moved)
        {
            Assert.Same(receipts[tile.Key].Receipt, frame[tile.Key].Receipt);
            Assert.Equal(tile, frame[tile.Key].Tile);
        }
        frame.Clear(); presentation.Render(moved.Take(1).ToArray(), Ranges(), new float[16]);
        Assert.Single(frame);
        Assert.False(preparation.IsCurrent(receipts[view[1].Key].Receipt));
        frame.Clear(); presentation.Render(moved, Ranges(), new float[16]);
        Assert.Equal(2, frame.Count);
        Assert.Same(receipts[view[1].Key].Receipt, frame[view[1].Key].Receipt);
        Assert.Equal(work, preparation.Work);
        // An ocean-only update revokes just this tile, independently of membership and the region lease.
        var old = frame[view[0].Key].Receipt;
        await storage.WriteTileAsync(new(view[0].Key, 32, null, new OceanMask(32, Enumerable.Repeat((byte)2, 1024))), storage.CurrentEpoch);
        Assert.False(preparation.IsCurrent(old));
        Assert.True(preparation.IsCurrent(frame[view[1].Key].Receipt));
        await Settle(presentation, frame, moved);
        Assert.NotSame(old.Bundle.Elevation, frame[view[0].Key].Receipt.Bundle.Elevation);
        Assert.Same(old.Bundle.Region, frame[view[0].Key].Receipt.Bundle.Region);
        Assert.Equal(work.Regions, preparation.Work.Regions);
        Assert.Equal(work.Tiles + 1, preparation.Work.Tiles);
    }

    /// <summary>Useful queued work survives overlapping pans; departed work is skipped unless the tile re-enters before execution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedWorkUsesCurrentMembershipWithoutRevokingOverlap(bool reenter)
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        await storage.WaitUntilReadyAsync();
        using var preparation = new GpuHabitatPreparation(storage, new(3, 3, 3, 3));
        var keys = View(0, 1, 2).Select(tile => tile.Key).ToArray();
        preparation.SetView(keys);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = Field<Channel<StorageCommand>>(storage, "queue");
        Assert.True(queue.Writer.TryWrite(new(_ => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(10)); }, error => entered.TrySetException(error))));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<GpuHabitatReceipt?> a, b, c;
        try
        {
            Assert.True(preparation.TryPrepare(keys[0], out a));
            await Until(() => preparation.Work.Regions == 1);
            Assert.True(preparation.TryPrepare(keys[1], out b));
            Assert.True(preparation.TryPrepare(keys[2], out c));
            preparation.SetView(keys.Take(2).ToArray());
            Assert.False(preparation.TryPrepare(keys[2], out _));
            if (reenter) preparation.SetView(keys);
        }
        finally { release.Set(); }
        var results = await Task.WhenAll(a, b, c).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(preparation.IsCurrent(results[0]!));
        Assert.True(preparation.IsCurrent(results[1]!));
        if (reenter) Assert.True(preparation.IsCurrent(results[2]!));
        else Assert.Null(results[2]);
        Assert.Equal(reenter ? 3 : 2, preparation.Work.Tiles);
        Assert.Equal(reenter ? 3 : 2, preparation.Work.Regions);
    }

    /// <summary>The actual texture owner retains source-keyed resources across shrink without invoking a GL upload on cache hits.</summary>
    [Fact]
    public async Task GraphicsShrinkProtectsColdVisibleTexturesAndPrepareReusesThem()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 3, 3, 2));
        var keys = View(0, 1, 2).Select(tile => tile.Key).ToArray();
        preparation.SetView(keys);
        var receipts = new List<GpuHabitatReceipt>();
        foreach (var key in keys)
        {
            Task<GpuHabitatReceipt?> task = null!;
            await Until(() => preparation.TryPrepare(key, out task));
            receipts.Add((await task)!);
        }
        // Use empty texture owners instead of a GL context. A mistaken cache miss in real Prepare would attempt GL and fail.
        var graphics = (HabitatMapGraphics)RuntimeHelpers.GetUninitializedObject(typeof(HabitatMapGraphics));
        var regions = new BoundedLru<(EcosystemRegionKey Key, EnvironmentalInput<RegionalEnvironment> Source), GpuRegionalTextures>(3);
        var tiles = new BoundedLru<(EcosystemTileKey Key, EnvironmentalInput<TileEnvironment> Source), GpuTileTextures>(3);
        SetField(graphics, "regions", regions); SetField(graphics, "tiles", tiles);
        foreach (var receipt in receipts)
        {
            var region = (GpuRegionalTextures)RuntimeHelpers.GetUninitializedObject(typeof(GpuRegionalTextures));
            SetField(region, "maps", new GpuIntegerTexture?[3]);
            regions.Put((fixture.Identity.RegionFor(receipt.Bundle.Key), receipt.Bundle.Region), region);
            tiles.Put((receipt.Bundle.Key, receipt.Bundle.Elevation), (GpuTileTextures)RuntimeHelpers.GetUninitializedObject(typeof(GpuTileTextures)));
        }
        var keep = receipts[0]; // Oldest entry is the survivor, deliberately colder than the departing entries.
        Assert.True(tiles.TryGet((keep.Bundle.Key, keep.Bundle.Elevation), out var original));
        foreach (var receipt in receipts.Skip(1)) tiles.TryGet((receipt.Bundle.Key, receipt.Bundle.Elevation), out _);
        graphics.SetView(new[] { keys[0] }, fixture.Identity, new(1, 1, 1, 2));
        graphics.Prepare(keep);
        Assert.True(tiles.TryGet((keep.Bundle.Key, keep.Bundle.Elevation), out var retained));
        Assert.Same(original, retained);
        Assert.Equal(1, regions.Count); Assert.Equal(1, tiles.Count);
        // A stale off-view admission cannot evict the protected source, even when it is newest in LRU order.
        var obsolete = (GpuTileTextures)RuntimeHelpers.GetUninitializedObject(typeof(GpuTileTextures));
        Assert.Same(obsolete, tiles.Put((keys[1], receipts[1].Bundle.Elevation), obsolete));
        graphics.Prepare(keep);
        graphics.Clear();
    }
    #endregion

    #region Private
    /// <summary>Builds tile keys in separate regions so regional and tile cache protection are both exercised.</summary>
    private static HabitatMapTile[] View(params int[] x) => x.Select(value => new HabitatMapTile(new(value * 16, 0), value, 0, 32, 32)).ToArray();
    /// <summary>Supplies a single range set without CPU habitat work.</summary>
    private static HabitatRanges Ranges() => HabitatRanges.From(new HabitatRule("game:test", HabitatSpawnPath.Runtime, -20, 30, 0, 1, 0, 1, 0, 1, 0, 0, 2));
    /// <summary>Records each current frame's exact receipt identity and screen placement.</summary>
    private static Mock<IHabitatMapGraphics> Graphics(Dictionary<EcosystemTileKey, (GpuHabitatReceipt Receipt, HabitatMapTile Tile)> frame)
    {
        var graphics = new Mock<IHabitatMapGraphics>();
        graphics.Setup(value => value.Begin(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatRanges>(), It.IsAny<float[]>())).Returns(Mock.Of<IDisposable>());
        graphics.Setup(value => value.Draw(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatMapTile>()))
            .Callback<GpuHabitatReceipt, HabitatMapTile>((receipt, tile) => frame[tile.Key] = (receipt, tile));
        return graphics;
    }
    /// <summary>Waits for all current tiles, clearing frame observations to avoid mistaking old coverage for current output.</summary>
    private static async Task Settle(HabitatMapPresentation presentation, Dictionary<EcosystemTileKey, (GpuHabitatReceipt Receipt, HabitatMapTile Tile)> frame, HabitatMapTile[] view)
    {
        await Until(() => { frame.Clear(); presentation.Render(view, Ranges(), new float[16]); return frame.Count == view.Length; });
    }
    /// <summary>Bounds asynchronous fixture polling without imposing game-thread waits.</summary>
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
    /// <summary>Reads a private owning field solely for deterministic worker scheduling and headless resource fixtures.</summary>
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    /// <summary>Installs empty native resource holders without constructing a graphics context or readback harness.</summary>
    private static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
    #endregion
}
