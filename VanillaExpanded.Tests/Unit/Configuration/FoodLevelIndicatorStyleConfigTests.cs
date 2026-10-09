using Newtonsoft.Json;
using VanillaExpanded.ItemSlotIndicators;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Checks food-level defaults and persisted style selection.</summary>
public sealed class FoodLevelIndicatorStyleConfigTests
{
    #region Public API
    /// <summary>New configurations choose the progress bar and do not run particles.</summary>
    [Fact]
    public void DefaultUsesProgressBar()
    {
        var config = new VanillaExpandedConfig();
        Assert.Equal("progress-bar", config.FoodLevelIndicatorStyle);
        Assert.Equal(ItemSlotIndicatorRenderingStyle.HorizontalBar, config.FoodLevelRenderingStyle);
        Assert.False(config.FoodGrainEffectEnabled);
    }

    /// <summary>Mapped strings and numeric selections resolve consistently, with unknown values using the default.</summary>
    [Theory]
    [InlineData("progress-bar", false)]
    [InlineData("slot-background", true)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("unknown", false)]
    public void SavedStyleControlsParticles(string style, bool particles)
    {
        var config = JsonConvert.DeserializeObject<VanillaExpandedConfig>(
            JsonConvert.SerializeObject(new { FoodLevelIndicatorStyle = style }))!;
        Assert.Equal(particles, config.FoodGrainEffectEnabled);
        Assert.Equal(particles ? ItemSlotIndicatorRenderingStyle.SlotBackground
            : ItemSlotIndicatorRenderingStyle.HorizontalBar, config.FoodLevelRenderingStyle);
    }
    #endregion
}
