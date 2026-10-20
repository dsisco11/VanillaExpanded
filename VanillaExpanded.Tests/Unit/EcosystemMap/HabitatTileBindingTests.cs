using System.Reflection;
using Moq;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises production atlas binding and regional layout changes without a graphics context.</summary>
public sealed class HabitatTileBindingTests
{
    #region Public API
    /// <summary>Regions and tiles on shared pages update origins/layouts while binding each sampler only once.</summary>
    [Fact]
    public void TileDrawsOnlyUpdateTileInputsAndChangedRegions()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var shader = new Mock<IShaderProgram>();
        var overlay = new GpuHabitatOverlay(api.Object);
        typeof(GpuHabitatOverlay).GetField("shader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(overlay, shader.Object);
        using var regions = new HabitatAtlas(true, 64, 4, (w, h, n) => new AtlasTextureFixture());
        using var terrain = new HabitatAtlas(false, 64, 4, (w, h, n) => new AtlasTextureFixture());
        using var first = new GpuRegionalTextures(new PackedRegionalData(null), regions);
        using var second = new GpuRegionalTextures(new PackedRegionalData(null), regions);
        using var tile = new GpuTileTextures(new ushort[2048], 32, terrain);
        using var otherTile = new GpuTileTextures(new ushort[2048], 32, terrain);
        var world = new EcosystemStorageIdentity("binding-fixture", 512, 256, 512, 110, 512, 32);
        var epoch = new StorageEpoch(Guid.NewGuid(), 0);
        var bundle = new GpuHabitatBundle(new(0, 0), world, new(epoch), new(epoch));
        overlay.Draw(bundle, first, tile, 0, 0, 32, 32);
        overlay.Draw(bundle, first, otherTile, 32, 0, 32, 32);
        overlay.Draw(bundle, second, tile, 64, 0, 32, 32);
        overlay.Draw(bundle, first, tile, 96, 0, 32, 32);
        var binds = shader.Invocations.Where(call => call.Method.Name == nameof(IShaderProgram.BindTexture2D))
            .Select(call => (string)call.Arguments[0]).ToArray();
        Assert.Equal(new[] { "regionalMap", "terrainMap" }, binds);
        var uniforms = shader.Invocations.Where(call => call.Method.Name == nameof(IShaderProgram.Uniform))
            .Select(call => (string)call.Arguments[0]).ToArray();
        Assert.Equal(3, uniforms.Count(name => name == "climateLayout"));
        Assert.Equal(3, uniforms.Count(name => name == "regionalAtlasOrigin"));
        Assert.Equal(3, uniforms.Count(name => name == "regionalAtlasSize"));
        Assert.Equal(4, uniforms.Count(name => name == "terrainAtlasOrigin"));
        Assert.Equal(4, uniforms.Count(name => name == "screenBounds"));
        Assert.Equal(4, uniforms.Count(name => name == "tileOrigin"));
        epoch.CancelDisposableWork();
    }
    /// <summary>Hardware-limited extra pages rebind only the affected sampler and stale allocations cannot draw.</summary>
    [Fact]
    public void PageChangesRebindAndClearedPagesCannotDraw()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var shader = new Mock<IShaderProgram>();
        var overlay = new GpuHabitatOverlay(api.Object);
        typeof(GpuHabitatOverlay).GetField("shader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(overlay, shader.Object);
        using var regions = new HabitatAtlas(true, 32, 2, (w, h, n) => new AtlasTextureFixture());
        using var terrain = new HabitatAtlas(false, 32, 2, (w, h, n) => new AtlasTextureFixture());
        using var region = new GpuRegionalTextures(new PackedRegionalData(null), regions);
        using var first = new GpuTileTextures(new ushort[2048], 32, terrain);
        using var second = new GpuTileTextures(new ushort[2048], 32, terrain);
        var world = new EcosystemStorageIdentity("page-fixture", 512, 256, 512, 110, 512, 32);
        var epoch = new StorageEpoch(Guid.NewGuid(), 0);
        var bundle = new GpuHabitatBundle(new(0, 0), world, new(epoch), new(epoch));
        overlay.Draw(bundle, region, first, 0, 0, 32, 32);
        overlay.Draw(bundle, region, second, 32, 0, 32, 32);
        var binds = shader.Invocations.Where(call => call.Method.Name == nameof(IShaderProgram.BindTexture2D))
            .Select(call => (string)call.Arguments[0]).ToArray();
        Assert.Equal(new[] { "regionalMap", "terrainMap", "terrainMap" }, binds);
        terrain.Clear();
        Assert.Throws<InvalidOperationException>(() => overlay.Draw(bundle, region, first, 0, 0, 32, 32));
        epoch.CancelDisposableWork();
    }
    #endregion
}
