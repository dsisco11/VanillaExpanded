using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks the shared fluid presentation declaration independently of graphics allocation.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidSloshIndicatorEffectTests
{
    #region Public API
    /// <summary>The built-in effect requests one shared camera capture and declares a deformable bounded presentation.</summary>
    [Fact]
    public void Definition_UsesSharedSurfaceAndVisibleDrawRange()
    {
        var effect = LiquidSloshIndicatorEffect.Definition;
        Assert.True(effect.NeedsCameraMotion);
        Assert.Equal(ItemSlotIndicatorTopology.FillStrip, effect.Topology);
        Assert.Equal(16, effect.SegmentCount);
        Assert.True(effect.Parameters.X > 0);
        Assert.Equal(0, effect.Parameters.Y);
        Assert.Equal(0, effect.Parameters.Z);
        Assert.InRange(effect.Parameters.W, 0, 1);
    }
    #endregion
}
