using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises production tile binding against recording engine APIs without executing GL or shader evaluation.</summary>
public sealed class HabitatTileBindingTests
{
    #region Public API
    /// <summary>Adjacent tiles share regional bindings; changing regional ownership rebinds layouts even if texture IDs coincide.</summary>
    [Fact]
    public void TileDrawsOnlyUpdateTileInputsAndChangedRegions()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var shader = new Mock<IShaderProgram>();
        var overlay = new GpuHabitatOverlay(api.Object);
        Set(overlay, "shader", shader.Object);
        var first = Region();
        var second = Region();
        var tile = (GpuTileTextures)RuntimeHelpers.GetUninitializedObject(typeof(GpuTileTextures));
        Set(tile, "elevation", RuntimeHelpers.GetUninitializedObject(typeof(GpuIntegerTexture)));
        Set(tile, "coverage", RuntimeHelpers.GetUninitializedObject(typeof(GpuIntegerTexture)));
        var world = new EcosystemStorageIdentity("binding-fixture", 512, 256, 512, 110, 512, 32);
        var epoch = new StorageEpoch(Guid.NewGuid(), 0);
        var bundle = new GpuHabitatBundle(new(0, 0), world, new(epoch), new(epoch));
        overlay.Draw(bundle, first, tile, 0, 0, 32, 32);
        overlay.Draw(bundle, first, tile, 32, 0, 32, 32);
        overlay.Draw(bundle, second, tile, 64, 0, 32, 32);
        overlay.Draw(bundle, first, tile, 96, 0, 32, 32);
        var binds = shader.Invocations.Where(call => call.Method.Name == nameof(IShaderProgram.BindTexture2D))
            .Select(call => (string)call.Arguments[0]).ToArray();
        Assert.Equal(3, binds.Count(name => name == "climateMap"));
        Assert.Equal(3, binds.Count(name => name == "forestMap"));
        Assert.Equal(3, binds.Count(name => name == "shrubMap"));
        Assert.Equal(4, binds.Count(name => name == "terrainMap"));
        Assert.Equal(4, binds.Count(name => name == "exploredMap"));
        var uniforms = shader.Invocations.Where(call => call.Method.Name == nameof(IShaderProgram.Uniform))
            .Select(call => (string)call.Arguments[0]).ToArray();
        Assert.Equal(3, uniforms.Count(name => name == "climateLayout"));
        Assert.Equal(4, uniforms.Count(name => name == "terrainAvailable"));
        Assert.Equal(4, uniforms.Count(name => name == "screenBounds"));
        Assert.Equal(4, uniforms.Count(name => name == "tileOrigin"));
        Assert.All(uniforms, name => Assert.Contains(name, new[]
            { "climateLayout", "forestLayout", "shrubLayout", "terrainAvailable", "screenBounds", "tileOrigin" }));
        epoch.CancelDisposableWork();
        // Unallocated fixture resources deliberately never enter GL disposal or setup.
    }
    #endregion

    #region Private
    /// <summary>Provides unallocated production binding owners; mocks observe their calls without an OpenGL context.</summary>
    private static GpuRegionalTextures Region()
    {
        var region = (GpuRegionalTextures)RuntimeHelpers.GetUninitializedObject(typeof(GpuRegionalTextures));
        Set(region, "maps", Enumerable.Range(0, 3).Select(_ =>
            (GpuIntegerTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuIntegerTexture))).ToArray());
        Set(region, "sources", new EnvironmentalMap?[3]);
        return region;
    }

    /// <summary>Initializes private graphics handles for a binding-only fixture without allocating native resources.</summary>
    private static void Set(object owner, string field, object value) =>
        owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
    #endregion
}
