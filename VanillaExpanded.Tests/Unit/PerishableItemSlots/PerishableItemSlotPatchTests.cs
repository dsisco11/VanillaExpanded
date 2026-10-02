using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.PerishableItemSlots;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.PerishableItemSlots;

[Trait("Category", "Unit")]
public sealed class PerishableItemSlotPatchTests
{
    [Theory]
    [InlineData(100, 100, 20, 0, 1)]
    [InlineData(50, 100, 20, 50, 0.5833333f)]
    [InlineData(0, 100, 20, 110, 0.0833333f)]
    [InlineData(0, 100, 20, 120, 0)]
    public void CalculateFreshness_TracksRemainingLifetime(
        float freshHoursLeft,
        float freshHours,
        float transitionHours,
        float transitionedHours,
        float expected)
    {
        var state = new TransitionState
        {
            FreshHoursLeft = freshHoursLeft,
            FreshHours = freshHours,
            TransitionHours = transitionHours,
            TransitionedHours = transitionedHours
        };

        float actual = FreshnessIndicatorProvider.CalculateFreshness(state);

        Assert.Equal(expected, actual, precision: 5);
    }

    [Fact]
    public void CalculateFreshness_EmptyLifetime_ReturnsZero()
    {
        var state = new TransitionState();

        Assert.Equal(0, FreshnessIndicatorProvider.CalculateFreshness(state));
    }

    [Fact]
    public void FreshnessColor_VeryFresh_IsGreenDominant()
    {
        var color = ItemSlotIndicatorRenderer.PremultiplyColor(FreshnessIndicatorProvider.FreshnessColor(1));

        Assert.True(color.G > color.R);
        Assert.True(color.A <= 0.45f);
        Assert.True(color.R <= color.A && color.G <= color.A && color.B <= color.A);
    }

    [Fact]
    public void FreshnessColor_AtNinetyFivePercent_RemainsFullyGreen()
    {
        var fullyFresh = ItemSlotIndicatorRenderer.PremultiplyColor(FreshnessIndicatorProvider.FreshnessColor(1));
        var nearlyFresh = ItemSlotIndicatorRenderer.PremultiplyColor(FreshnessIndicatorProvider.FreshnessColor(0.95f));

        Assert.Equal(fullyFresh.R, nearlyFresh.R);
        Assert.Equal(fullyFresh.G, nearlyFresh.G);
        Assert.Equal(fullyFresh.B, nearlyFresh.B);
        Assert.Equal(fullyFresh.A, nearlyFresh.A);
    }

    [Fact]
    public void FreshnessColor_NotFresh_IsRedDominant()
    {
        var color = ItemSlotIndicatorRenderer.PremultiplyColor(FreshnessIndicatorProvider.FreshnessColor(0));

        Assert.True(color.R > color.G);
        Assert.True(color.A <= VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity * 1.3f);
        Assert.True(color.R <= color.A && color.G <= color.A && color.B <= color.A);
    }

    [Fact]
    public void FreshnessColor_PartiallyStale_IsRedDominant()
    {
        var color = ItemSlotIndicatorRenderer.PremultiplyColor(FreshnessIndicatorProvider.FreshnessColor(0.5f));

        Assert.True(color.R > color.G * 2);
    }
}