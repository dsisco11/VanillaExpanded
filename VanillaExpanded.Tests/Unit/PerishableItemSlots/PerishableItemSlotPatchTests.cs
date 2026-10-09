using VanillaExpanded.PerishableItemSlots;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.PerishableItemSlots;

/// <summary>Checks remaining perish lifetime independently of indicator presentation.</summary>
[Trait("Category", "Unit")]
public sealed class PerishableItemSlotPatchTests
{
    #region Public API
    /// <summary>Remaining fresh and transition hours determine the resource lifetime fraction.</summary>
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

    /// <summary>An empty lifetime reports no remaining freshness.</summary>
    [Fact]
    public void CalculateFreshness_EmptyLifetime_ReturnsZero()
    {
        var state = new TransitionState();

        Assert.Equal(0, FreshnessIndicatorProvider.CalculateFreshness(state));
    }

    #endregion
}
