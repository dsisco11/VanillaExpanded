using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Presentation;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises installed terrain coverage and presentation lifecycles without creating an external graphics harness.</summary>
public sealed class MapPresentationTests
{
    #region Public API
    /// <summary>Uses actual component chunk flags and revokes coverage after unset, removal and terrain toggle.</summary>
    [Fact]
    public void CoverageUsesLiveTerrainComponentsOnly()
    {
        var terrain = (ChunkMapLayer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkMapLayer));
        terrain.Active = true;
        var components = new ConcurrentDictionary<FastVec2i, MultiChunkMapComponent>();
        typeof(ChunkMapLayer).GetField("loadedMapData", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(terrain, components);
        var component = new MultiChunkMapComponent(new Mock<ICoreClientAPI>().Object, new FastVec2i(3, 6));
        component.setChunk(1, 2, new int[1024]);
        components[new(1, 2)] = component;
        var layers = new MapLayer[] { terrain };
        var view = new Cuboidd(0, 0, 0, 512, 0, 512);
        Assert.Equal(new EcosystemTileKey(4, 8), Assert.Single(ExploredMapCoverage.Capture(layers, view)));
        Assert.Empty(ExploredMapCoverage.Capture(layers, new Cuboidd(0, 0, 0, 32, 0, 32)));
        component.unsetChunk(1, 2);
        Assert.Empty(ExploredMapCoverage.Capture(layers, view));
        component.setChunk(1, 2, new int[1024]);
        terrain.Active = false;
        Assert.Empty(ExploredMapCoverage.Capture(layers, view));
        terrain.Active = true; components.Clear();
        Assert.Empty(ExploredMapCoverage.Capture(layers, view));
    }

    /// <summary>None initializes even an empty catalogue, survives rebuilding and explicitly clears an active species.</summary>
    [Fact]
    public void NoneIsTheInitialSelectionAndCanBeRestored()
    {
        var selection = new EcosystemMapSelection(item => item.Code, () => "None");
        Assert.Equal(new[] { EcosystemMapSelection.NoneCode }, selection.Codes);
        Assert.Equal(new[] { "None" }, selection.Names);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Null(selection.SelectedCode);
        var animal = Species("game:a", "game:a-name");
        selection.SetSpecies(new[] { animal });
        Assert.Equal(new[] { EcosystemMapSelection.NoneCode, animal.Code }, selection.Codes);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Null(selection.ActiveRanges(_ => throw new InvalidOperationException()));
        selection.Select(animal.Code, true);
        Assert.Equal(1, selection.SelectedIndex);
        selection.Select(EcosystemMapSelection.NoneCode, false);
        Assert.Equal(animal.Code, selection.SelectedCode);
        selection.Select(EcosystemMapSelection.NoneCode, true);
        selection.SetSpecies(new[] { animal });
        Assert.Null(selection.SelectedCode);
        Assert.Equal(0, selection.SelectedIndex);
        Assert.Null(selection.ActiveRanges(_ => throw new InvalidOperationException()));
        selection.Select(animal.Code, true);
        selection.SetSpecies(Array.Empty<EcosystemSpecies>());
        Assert.Equal(new[] { EcosystemMapSelection.NoneCode }, selection.Codes);
        Assert.Null(selection.SelectedCode);
    }

    /// <summary>Localized ordering, stable identity and explicit criteria resolution survive dropdown reconstruction.</summary>
    [Fact]
    public void SelectionSurvivesRecompositionWithoutMergingRules()
    {
        var a = Species("game:a", "game:z-name");
        var b = Species("game:b", "game:a-name");
        var selection = new EcosystemMapSelection(item => item.Code == a.Code ? "Zebra" : "Antelope", () => "None");
        selection.SetSpecies(new[] { a, b });
        Assert.Equal(selection.Names.Skip(1).Order(StringComparer.CurrentCulture), selection.Names.Skip(1));
        selection.Select(a.Code, true);
        selection.SetSpecies(new[] { b, a });
        Assert.Equal(a.Code, selection.SelectedCode);
        selection.Select("game:unknown", true); selection.Select(b.Code, false);
        Assert.Equal(a.Code, selection.SelectedCode);
        Assert.Equal(HabitatRanges.From(a.Alternatives[1]), selection.ActiveRanges(species => species.Alternatives[1]));
        selection.SetSpecies(Array.Empty<EcosystemSpecies>());
        Assert.Null(selection.ActiveRanges(_ => throw new InvalidOperationException()));
    }

    /// <summary>Prefers ongoing spawning and uses deterministic entity identity; worldgen is an explicit fallback.</summary>
    [Fact]
    public void ActiveRuleUsesApprovedPreferenceAndStableRepresentative()
    {
        var worldgen = Species("game:z", "game:z").Alternatives[0];
        var runtimeZ = worldgen with { SpawnPath = HabitatSpawnPath.Runtime, EntityCode = "game:z" };
        var runtimeA = runtimeZ with { EntityCode = "game:a", MinRainfall = .9f };
        var species = new EcosystemSpecies("game:test", new[] { "game:z", "game:a" }, new[] { worldgen, runtimeZ, runtimeA });
        Assert.Same(runtimeA, EcosystemActiveRule.Select(species));
        var reordered = new EcosystemSpecies("game:test", species.EntityCodes, species.Alternatives.Reverse());
        Assert.Same(runtimeA, EcosystemActiveRule.Select(reordered));
        Assert.Same(worldgen, EcosystemActiveRule.Select(new("game:z", new[] { "game:z" }, new[] { worldgen })));
    }
    /// <summary>Old/full/minimap objects cannot pass exact composed-surface identity checks accidentally.</summary>
    [Fact]
    public void FullMapSurfaceRequiresExactIdentity()
    {
        object full = new(), minimap = new(), replacement = new();
        Assert.True(EcosystemMapControls.MatchesSurface(full, full));
        Assert.False(EcosystemMapControls.MatchesSurface(full, minimap));
        Assert.False(EcosystemMapControls.MatchesSurface(replacement, full));
        Assert.False(EcosystemMapControls.MatchesSurface(null, null));
    }

    /// <summary>Unknown records transition to original inputs, selections reuse bundles, and close/purge revoke disposable results.</summary>
    [Fact]
    public async Task PresentationTransitionsAndLifetimePreservePersistence()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 4, 4, 2));
        var graphics = new RecordingGraphics();
        var errors = new List<Exception>();
        var presentation = new HabitatMapPresentation(preparation, graphics, errors.Add);
        var ranges = HabitatRanges.From(Species("game:a", "game:a").Alternatives[0]);
        var tile = new HabitatMapTile(default, 0, 0, 32, 32);
        await Draw(presentation, graphics, tile, ranges);
        Assert.Null(graphics.Last!.Bundle.Region.Value);
        var old = graphics.Last;
        await storage.WriteRegionAsync(new(default, StorageFixture.Map(), StorageFixture.Map(), StorageFixture.Map()), storage.CurrentEpoch);
        await storage.WriteTileAsync(StorageFixture.Tile(default), storage.CurrentEpoch);
        await Draw(presentation, graphics, tile, ranges);
        Assert.NotNull(graphics.Last!.Bundle.Region.Value);
        Assert.NotSame(old.Bundle.Region, graphics.Last.Bundle.Region);
        var complete = graphics.Last.Bundle;
        await Draw(presentation, graphics, tile, ranges with { MinRainfall = .8f });
        Assert.Same(complete, graphics.Last.Bundle);
        Assert.Equal(.8f, graphics.Ranges!.MinRainfall);
        presentation.Suspend();
        Assert.False(preparation.IsCurrent(graphics.Last));
        Assert.True(graphics.Clears > 0);
        Assert.True(await storage.WriteTileAsync(StorageFixture.Tile(default), storage.CurrentEpoch));
        await Draw(presentation, graphics, tile, ranges);
        await storage.PurgeAsync(); preparation.Reset();
        Assert.False(preparation.IsCurrent(graphics.Last!));
        await Draw(presentation, graphics, new(new(1, 0), 32, 0, 32, 32), ranges);
        Assert.Equal(new EcosystemTileKey(1, 0), graphics.Last!.Bundle.Key);
        Assert.Null(graphics.Last.Bundle.Region.Value);
        Assert.True(presentation.Reload());
        presentation.Dispose(); presentation.Dispose();
        Assert.Equal(1, graphics.Disposals);
        Assert.True(await storage.WriteTileAsync(StorageFixture.Tile(default), storage.CurrentEpoch));
        Assert.Empty(errors);
    }
    /// <summary>Far-zoom views larger than the cache floors settle into a complete frame without repeated reads or eviction churn.</summary>
    [Fact]
    public async Task WideExploredViewRetainsAllVisibleInputs()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, GpuHabitatLimits.Client);
        var graphics = new RecordingGraphics();
        using var presentation = new HabitatMapPresentation(preparation, graphics, error => throw error);
        var ranges = HabitatRanges.From(Species("game:a", "game:a").Alternatives[0]);
        var view = Enumerable.Range(0, 130).Select(x => new HabitatMapTile(new(x * 2, 0), x, 0, 1, 1)).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            graphics.FrameKeys.Clear(); presentation.Render(view, ranges, new float[16]);
            if (graphics.FrameKeys.Count == view.Length) break;
            await Task.Delay(1, timeout.Token);
        }
        var work = preparation.Work;
        for (int i = 0; i < 3; i++)
        {
            graphics.FrameKeys.Clear(); presentation.Render(view, ranges, new float[16]);
            Assert.Equal(view.Length, graphics.FrameKeys.Count);
            Assert.Equal(work, preparation.Work);
        }
    }
    /// <summary>Native matrix getters share scratch storage; the shared pass retains the frame projection despite model-view getter mutation.</summary>
    [Fact]
    public async Task NativeBorrowedProjectionSurvivesModelViewReadsAcrossTileDraws()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 4, 4, 2));
        var game = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        game.PMatrix = new StackMatrix4();
        game.MvMatrix = new StackMatrix4();
        var expected = Enumerable.Range(1, 16).Select(value => (float)value).ToArray();
        var modelView = Enumerable.Range(101, 16).Select(value => (float)value).ToArray();
        game.PMatrix.Push(expected.Select(value => (double)value).ToArray());
        game.MvMatrix.Push(modelView.Select(value => (double)value).ToArray());
        typeof(ClientMain).GetField("tmpMatrix", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, new float[16]);
        var recorded = new List<float[]>();
        int draws = 0;
        var graphics = new Mock<IHabitatMapGraphics>();
        graphics.Setup(item => item.Begin(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatRanges>(), It.IsAny<float[]>()))
            .Callback<GpuHabitatReceipt, HabitatRanges, float[]>((receipt, ranges, projection) =>
            {
                // Match the native pass setup: model-view reads overwrite the borrowed projection scratch buffer.
                recorded.Add(projection.ToArray());
                _ = game.CurrentModelViewMatrix;
            }).Returns(Mock.Of<IDisposable>());
        graphics.Setup(item => item.Draw(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatMapTile>()))
            .Callback(() => draws++);
        using var presentation = new HabitatMapPresentation(preparation, graphics.Object, error => throw error);
        var ranges = HabitatRanges.From(Species("game:a", "game:a").Alternatives[0]);
        var tiles = new[] { new HabitatMapTile(new(0, 0), 0, 0, 32, 32), new HabitatMapTile(new(1, 0), 32, 0, 32, 32) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            recorded.Clear(); draws = 0;
            float[] borrowed = game.CurrentProjectionMatrix;
            presentation.Render(tiles, ranges, borrowed);
            if (draws == tiles.Length)
            {
                Assert.Equal(modelView, borrowed);
                Assert.Equal(expected, Assert.Single(recorded));
                break;
            }
            await Task.Delay(1, timeout.Token);
        }
    }
    /// <summary>Preserves server double elevation comparisons at float rule boundaries without GPU float division ambiguity.</summary>
    [Theory]
    [InlineData(0.2f, 0.7f)]
    [InlineData(1f, 1f)]
    [InlineData(1.2f, 1.5f)]
    [InlineData(-1f, 3f)]
    public void ElevationLimitsMatchVanillaForEverySurface(float minimum, float maximum)
    {
        var bounds = HabitatHeightRange.From(minimum, maximum, 110, 256);
        for (int y = 1; y < 256; y++)
        {
            double elevation = y <= 110 ? (double)y / 110 : 1.0 + ((double)y - 110) / (256 - 110);
            Assert.Equal(elevation >= minimum && elevation <= maximum, y >= bounds.Minimum && y <= bounds.Maximum);
        }
    }
    #endregion

    #region Private
    /// <summary>Builds distinct complete criteria sets so an accidental union is observable.</summary>
    private static EcosystemSpecies Species(string code, string name) => new(code, new[] { code }, new[]
    {
        new HabitatRule(code, HabitatSpawnPath.Worldgen, -10, 10, .1f, .5f, 0, 1, 0, 1, 0, 0, 2),
        new HabitatRule(code, HabitatSpawnPath.Runtime, 20, 30, .6f, .9f, 0, 1, 0, 1, 0, 0, 2)
    }, name);

    /// <summary>Polls normal render admission until a new graphics publication occurs, with a bounded fixture timeout.</summary>
    private static async Task Draw(HabitatMapPresentation presentation, RecordingGraphics graphics, HabitatMapTile tile, HabitatRanges ranges)
    {
        int before = graphics.Draws;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (graphics.Draws == before)
        {
            presentation.Render(new[] { tile }, ranges, new float[16]);
            await Task.Delay(1, timeout.Token);
        }
    }
    #endregion

    /// <summary>Records the normal graphics boundary without evaluating habitat values on the CPU.</summary>
    private sealed class RecordingGraphics : IHabitatMapGraphics
    {
        internal int Draws, Clears, Disposals;
        internal HashSet<EcosystemTileKey> FrameKeys = new();
        internal GpuHabitatReceipt? Last;
        internal HabitatRanges? Ranges;
        #region Public API
        /// <summary>Records normal shader-event forwarding.</summary>
        public bool Reload() => true;
        /// <summary>Accepts finite view resource retention without allocating graphics in the fixture.</summary>
        public void SetView(IReadOnlyCollection<EcosystemTileKey> visible, EcosystemStorageIdentity world, GpuHabitatLimits limits) { }
        /// <summary>Provides an empty acquisition scope without GL state in this fixture.</summary>
        public IDisposable BeginUploads() => Mock.Of<IDisposable>();
        /// <summary>Accepts source acquisition without allocating graphics in this fixture.</summary>
        public void Prepare(GpuHabitatReceipt receipt) { }
        /// <summary>Records shared criteria once at the normal pass boundary.</summary>
        public IDisposable Begin(GpuHabitatReceipt first, HabitatRanges ranges, float[] projection)
        { Ranges = ranges; return Mock.Of<IDisposable>(); }
        /// <summary>Records exact input lease and selected uniform criteria.</summary>
        public void Draw(GpuHabitatReceipt receipt, HabitatMapTile tile)
        { Draws++; Last = receipt; FrameKeys.Add(tile.Key); }
        /// <summary>Records disposable texture release.</summary>
        public void Clear() => Clears++;
        /// <summary>Records graphics-owner disposal.</summary>
        public void Dispose() => Disposals++;
        #endregion
    }
}
