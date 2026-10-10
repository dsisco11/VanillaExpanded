using VanillaExpanded.AnimalSexIndicators;

namespace VanillaExpanded.Tests.Unit.AnimalSexIndicators;

/// <summary>Checks the near-opacity plateau and continuous fade at the viewing boundary.</summary>
public sealed class AnimalSexDistanceFadeTests
{
    #region Public API
    /// <summary>Preserves opacity nearby, fades over the outer half, and hides symbols at or beyond the range.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(6, 0.5)]
    [InlineData(7, 0.25)]
    [InlineData(8, 0)]
    [InlineData(9, 0)]
    public void FadeReachesZeroAtEightBlocks(double distance, float expected)
    {
        Assert.Equal(expected, AnimalSexIndicatorRenderer.GetDistanceFade(distance, 8));
    }
    #endregion
}
