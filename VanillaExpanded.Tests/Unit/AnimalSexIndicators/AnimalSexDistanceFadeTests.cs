using VanillaExpanded.AnimalSexIndicators;

namespace VanillaExpanded.Tests.Unit.AnimalSexIndicators;

/// <summary>Checks that distance eligibility suppresses symbols at and beyond the configured boundary.</summary>
public sealed class AnimalSexDistanceFadeTests
{
    #region Public API
    /// <summary>The indicator is suppressed at and beyond its caller-supplied range.</summary>
    [Theory]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    public void FadeSuppressesAtAndBeyondConfiguredRange(double distance, double range)
    {
        Assert.Equal(0, AnimalSexIndicatorRenderer.GetDistanceFade(distance, range));
    }
    #endregion
}
