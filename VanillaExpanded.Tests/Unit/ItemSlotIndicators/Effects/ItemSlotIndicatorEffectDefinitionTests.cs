using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks the immutable effect contract before descriptions can enter the provider registry.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorEffectDefinitionTests
{
    #region Public API
    #region Identity and Assets
    /// <summary>Effect identity requires an explicit canonical namespace and nonempty path components.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("waves")]
    [InlineData(":waves")]
    [InlineData("test:")]
    [InlineData("test:Waves")]
    [InlineData("Test:waves")]
    [InlineData("test:waves:variant")]
    [InlineData("test:/waves")]
    [InlineData("test:waves/")]
    [InlineData("test:waves//variant")]
    [InlineData("test:../waves")]
    [InlineData("test:waves variant")]
    public void InvalidId_IsRejected(string? id)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemSlotIndicatorEffectDefinition(
            id!, "vanillaexpanded", "vanillaexpanded_itemslot_test"));
    }

    /// <summary>Shader domains cannot introduce paths, noncanonical names, or absent identity.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("VanillaExpanded")]
    [InlineData("test/domain")]
    [InlineData("test:domain")]
    [InlineData("../test")]
    public void InvalidShaderDomain_IsRejected(string? domain)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemSlotIndicatorEffectDefinition(
            "test:waves", domain!, "vanillaexpanded_itemslot_test"));
    }

    /// <summary>Engine shader names require a reserved, extension-free basename with a nonempty suffix.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("waves")]
    [InlineData("vanillaexpanded_itemslot_")]
    [InlineData("vanillaexpanded_itemslot_Test")]
    [InlineData("vanillaexpanded_itemslot_test.vsh")]
    [InlineData("vanillaexpanded_itemslot_test/variant")]
    public void InvalidShaderName_IsRejected(string? shaderName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemSlotIndicatorEffectDefinition(
            "test:waves", "vanillaexpanded", shaderName!));
    }
    #endregion

    #region Geometry and Parameters
    /// <summary>Both supported layouts accept exactly their established segment limits.</summary>
    [Theory]
    [InlineData((int)ItemSlotIndicatorTopology.Quad, 1)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 2)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 16)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 64)]
    public void SupportedGeometry_RetainsDescription(int topologyValue, int segments)
    {
        var topology = (ItemSlotIndicatorTopology)topologyValue;
        var definition = new ItemSlotIndicatorEffectDefinition("test:waves/variant-1", "vanillaexpanded",
            "vanillaexpanded_itemslot_test", topology, segments);

        Assert.Equal(topology, definition.Topology);
        Assert.Equal(segments, definition.SegmentCount);
        Assert.Equal(1, definition.AbiVersion);
    }

    /// <summary>Unsupported versions, topologies, and counts cannot become render descriptions.</summary>
    [Theory]
    [InlineData((int)ItemSlotIndicatorTopology.Quad, 0, 1)]
    [InlineData((int)ItemSlotIndicatorTopology.Quad, 2, 1)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 1, 1)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 65, 1)]
    [InlineData(42, 16, 1)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 16, 0)]
    [InlineData((int)ItemSlotIndicatorTopology.FillStrip, 16, 2)]
    public void UnsupportedGeometryOrVersion_IsRejected(int topologyValue, int segments, int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorEffectDefinition(
            "test:waves", "vanillaexpanded", "vanillaexpanded_itemslot_test", (ItemSlotIndicatorTopology)topologyValue,
            segments, abiVersion: version));
    }

    /// <summary>Every parameter lane rejects NaN and either infinity independently.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonfiniteParameters_AreRejected(float invalid)
    {
        // Hold the other lanes finite to verify each lane independently enforces the contract.
        for (int lane = 0; lane < 4; lane++)
        {
            var parameters = Vector4.Zero;
            parameters[lane] = invalid;
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorEffectDefinition(
                "test:waves", "vanillaexpanded", "vanillaexpanded_itemslot_test", parameters: parameters));
        }
    }

    /// <summary>Definitions retain copied values and compare by the complete appearance description.</summary>
    [Fact]
    public void Definition_CopiesParametersAndHasValueIdentity()
    {
        var parameters = new Vector4(0.1f, 2, 0, 0);
        var first = new ItemSlotIndicatorEffectDefinition("test:waves", "vanillaexpanded",
            "vanillaexpanded_itemslot_test", parameters: parameters, needsCameraMotion: true);
        var same = new ItemSlotIndicatorEffectDefinition("test:waves", "vanillaexpanded",
            "vanillaexpanded_itemslot_test", parameters: parameters, needsCameraMotion: true);
        // Mutating the caller's vector cannot alter either immutable appearance definition.
        parameters.X = 0.2f;

        Assert.Equal(same, first);
        Assert.Equal(0.1f, first.Parameters.X);
        Assert.Equal("test:waves", first.Id);
        Assert.Equal("vanillaexpanded", first.ShaderAssetDomain);
        Assert.Equal("vanillaexpanded_itemslot_test", first.ShaderName);
        Assert.Equal(16, first.SegmentCount);
        Assert.True(first.NeedsCameraMotion);
    }
    #endregion
    #endregion
}
