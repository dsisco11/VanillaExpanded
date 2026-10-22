using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Presentation;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises production graphics admission and atlas caches with recording pages, never a GL context.</summary>
internal sealed class BudgetedGraphicsFixture : IDisposable
{
    internal HabitatMapGraphics Graphics { get; } = (HabitatMapGraphics)RuntimeHelpers.GetUninitializedObject(typeof(HabitatMapGraphics));
    internal EcosystemStorageIdentity World { get; } = new("budget-test", 10000, 256, 10000, 110, 512, 32);
    private readonly StorageEpoch epoch = new(Guid.NewGuid(), 0);
    private readonly PackedRegionalData region = new(null);
    private readonly EnvironmentalInput<PackedRegionalData> regional;
    private readonly BoundedLru<(EcosystemRegionKey, EnvironmentalInput<PackedRegionalData>), GpuRegionalTextures> regions = new(8);
    private readonly BoundedLru<(EcosystemTileKey, EnvironmentalInput<PackedTerrainData>), GpuTileTextures> tiles = new(16);
    internal List<AtlasTextureFixture> Pages { get; } = new();
    internal int RegionCount => regions.Count;
    internal int TileCount => tiles.Count;
    internal int Transfers => Pages.Sum(page => page.Uploads.Count);

    #region Public API
    /// <summary>Installs the real source-keyed cache owners and fake page-storage factory.</summary>
    internal BudgetedGraphicsFixture()
    {
        regional = new(epoch) { Loaded = true, Value = region };
        Set("regions", regions); Set("tiles", tiles);
        Set("regionalAtlas", new HabitatAtlas(true, 4096, 9, CreatePage));
        Set("terrainAtlas", new HabitatAtlas(false, 4096, 17, CreatePage));
    }
    /// <summary>Creates exact prepared buffers and either a shared or distinct regional lease for a terrain receipt.</summary>
    internal GpuHabitatReceipt Receipt(int x, bool distinctRegion = false)
    {
        var key = new EcosystemTileKey(x, 0);
        var input = distinctRegion ? new EnvironmentalInput<PackedRegionalData>(epoch) { Loaded = true, Value = region } : regional;
        var terrain = new PackedTerrainData(32, new ushort[2048]);
        return new(new(key, World, input, new(epoch) { Loaded = true, Value = terrain })
            { RegionalTexture = region, TerrainTexture = terrain.Values }, new object());
    }
    /// <summary>Disposes production leases/pages without accessing the intentionally absent shader owner.</summary>
    public void Dispose() { Graphics.Clear(); epoch.CancelDisposableWork(); }
    #endregion

    #region Private
    /// <summary>Records exact packed transfers while keeping real atlas allocation and lifetime logic.</summary>
    private IHabitatAtlasTexture CreatePage(int width, int height, bool normalized)
    {
        var page = new AtlasTextureFixture(); Pages.Add(page); return page;
    }
    /// <summary>Installs owning graphics fields while bypassing only native GL/shader construction.</summary>
    private void Set(string field, object value) => typeof(HabitatMapGraphics).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Graphics, value);
    #endregion
}
