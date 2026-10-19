using Moq;
using VanillaExpanded.EcosystemMap.Gpu;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks the engine metadata boundary responsible for live integer-sampler binding failures, without a graphics harness.</summary>
public sealed class HabitatSamplerBindingTests
{
    #region Public API
    /// <summary>Restores all signed/unsigned sampler names missing from native parsed metadata and leaves unrelated uniforms intact.</summary>
    [Fact]
    public void LinkedIntegerSamplersCompleteEngineMetadata()
    {
        var program = new ShaderProgram();
        program.uniformLocations["existing"] = 42;
        string[] names = { "climateMap", "forestMap", "shrubMap", "terrainMap", "exploredMap" };
        var resolved = new List<string>();
        GpuHabitatSamplers.Register(program, name => { resolved.Add(name); return Array.IndexOf(names, name); });
        Assert.Equal(names, resolved);
        for (int i = 0; i < names.Length; i++) Assert.Equal(i, program.uniformLocations[names[i]]);
        Assert.Equal(42, program.uniformLocations["existing"]);
    }
    /// <summary>A missing linked sampler rejects the replacement before any partial metadata is published.</summary>
    [Fact]
    public void MissingLinkedSamplerFailsWithoutPartialRegistration()
    {
        var program = new ShaderProgram();
        Assert.Throws<InvalidOperationException>(() => GpuHabitatSamplers.Register(program, name => name == "terrainMap" ? -1 : 7));
        Assert.Empty(program.uniformLocations);
        Assert.Throws<NotSupportedException>(() => GpuHabitatSamplers.Register(Mock.Of<IShaderProgram>(), _ => 0));
    }
    #endregion
}
