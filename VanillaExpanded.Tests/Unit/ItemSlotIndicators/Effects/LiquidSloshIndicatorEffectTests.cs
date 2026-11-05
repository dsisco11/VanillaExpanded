using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks the shared fluid presentation declaration independently of graphics allocation.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidSloshIndicatorEffectTests
{
    #region Public API
    /// <summary>The built-in effect requests shared motion and declares compatible fill-strip geometry.</summary>
    [Fact]
    public void DefinitionUsesSharedMotionAndValidFillStripGeometry()
    {
        var effect = LiquidSloshIndicatorEffect.Definition;
        Assert.True(effect.NeedsCameraMotion);
        Assert.Equal(ItemSlotIndicatorTopology.FillStrip, effect.Topology);
        Assert.True(effect.SegmentCount > 0);
        Assert.All(new[] { effect.Parameters.X, effect.Parameters.Y, effect.Parameters.Z, effect.Parameters.W },
            value => Assert.True(float.IsFinite(value)));
    }
    #endregion
}
