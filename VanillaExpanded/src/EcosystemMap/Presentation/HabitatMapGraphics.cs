using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Owns bounded original textures and the engine-managed shader for the full-map presentation.</summary>
internal sealed class HabitatMapGraphics : IHabitatMapGraphics
{
    private readonly GpuHabitatOverlay overlay;
    private readonly Vintagestory.API.Common.ILogger logger;
    private readonly BoundedLru<(EcosystemRegionKey Key, EnvironmentalInput<RegionalEnvironment> Source), GpuRegionalTextures> regions = new(8);
    private readonly BoundedLru<(EcosystemTileKey Key, EnvironmentalInput<TileEnvironment> Source), GpuTileTextures> tiles = new(128);

    #region Public API
    /// <summary>Creates the graphics owner and loads the shader through the normal game API.</summary>
    internal HabitatMapGraphics(ICoreClientAPI api)
    {
        logger = api.Logger;
        overlay = new(api);
        try { if (!overlay.Reload()) throw new InvalidOperationException("Ecosystem shader compilation failed."); }
        catch { overlay.Dispose(); throw; }
    }
    /// <summary>Reloads the owned engine file shader, retaining its previous program if compilation fails.</summary>
    public bool Reload() => overlay.Reload();

    /// <summary>Resizes retention to the visible working set and releases resources evicted by view shrink.</summary>
    public void SetView(IReadOnlyCollection<EcosystemTileKey> visible, EcosystemStorageIdentity world, GpuHabitatLimits limits)
    {
        // Coordinate membership protects current leases before shrink or newly entering tile uploads.
        var tileKeys = visible.ToHashSet();
        var regionKeys = visible.Select(world.RegionFor).ToHashSet();
        foreach (var texture in regions.Resize(limits.Regions, item => item.Source.Valid && regionKeys.Contains(item.Key))) texture.Dispose();
        foreach (var texture in tiles.Resize(limits.SourceTiles, item => item.Source.Valid && tileKeys.Contains(item.Key))) texture.Dispose();
    }
    /// <summary>Reuses original textures independently of species and uploads only newly encountered source leases.</summary>
    public void Prepare(GpuHabitatReceipt receipt)
    {
        var bundle = receipt.Bundle;
        if (!regions.TryGet((bundle.World.RegionFor(bundle.Key), bundle.Region), out var region))
        {
            region = new(bundle.Region.Value, logger);
            regions.Put((bundle.World.RegionFor(bundle.Key), bundle.Region), region)?.Dispose();
        }
        if (!tiles.TryGet((bundle.Key, bundle.Elevation), out var heights))
        {
            // Coverage is a whole explored chunk here. Unknown chunks never enter this draw path.
            heights = new(bundle.Elevation.Value, bundle.World.ChunkSize);
            tiles.Put((bundle.Key, bundle.Elevation), heights)?.Dispose();
        }
    }
    /// <summary>Begins the shared state lifetime only after texture acquisition has completed.</summary>
    public IDisposable Begin(GpuHabitatReceipt first, HabitatRanges ranges, float[] projection) => overlay.Begin(first.Bundle.World, ranges, projection);

    /// <summary>Uses acquired resources without allocations or uploads that could disturb pass bindings.</summary>
    public void Draw(GpuHabitatReceipt receipt, HabitatMapTile tile)
    {
        var bundle = receipt.Bundle;
        if (!regions.TryGet((bundle.World.RegionFor(bundle.Key), bundle.Region), out var region) || !tiles.TryGet((bundle.Key, bundle.Elevation), out var heights))
            throw new InvalidOperationException("Habitat textures must be acquired before the pass begins.");
        overlay.Draw(bundle, region!, heights!, tile.X, tile.Y, tile.Width, tile.Height);
    }
    /// <summary>Releases all original textures on close, purge or coverage invalidation.</summary>
    public void Clear()
    {
        foreach (var texture in regions.Clear()) texture.Dispose();
        foreach (var texture in tiles.Clear()) texture.Dispose();
    }
    /// <summary>Disposes texture, shader and mesh ownership on the graphics thread.</summary>
    public void Dispose() { Clear(); overlay.Dispose(); }
    #endregion
}
