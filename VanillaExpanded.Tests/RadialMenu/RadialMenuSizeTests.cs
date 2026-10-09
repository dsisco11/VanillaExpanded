using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.Tests.RadialMenu;

/// <summary>Checks live menu sizing and safe bounds for configuration values.</summary>
public sealed class RadialMenuSizeTests
{
    #region Public API
    /// <summary>Changing the setting updates an existing layout while preserving its caller scale.</summary>
    [Fact]
    public void LiveMultiplierPreservesBaseScale()
    {
        float multiplier = 1f;
        var layout = new RadialMenuLayout(["entry"], 0, 1, radiusScale: 0.6,
            sizeMultiplier: () => multiplier);

        Assert.Equal(0.6, layout.RadiusScale, precision: 6);
        multiplier = 2.5f;
        Assert.Equal(1.5, layout.RadiusScale, precision: 6);
        multiplier = 0.15f;
        Assert.Equal(0.09, layout.RadiusScale, precision: 6);
    }

    /// <summary>Finite values clamp to supported bounds and nonfinite values use the default size.</summary>
    [Theory]
    [InlineData(-1f, 0.09)]
    [InlineData(0.5f, 0.3)]
    [InlineData(1f, 0.6)]
    [InlineData(1.5f, 0.9)]
    [InlineData(2f, 1.2)]
    [InlineData(0.15f, 0.09)]
    [InlineData(2.5f, 1.5)]
    [InlineData(3f, 1.5)]
    [InlineData(float.NaN, 0.6)]
    [InlineData(float.PositiveInfinity, 0.6)]
    [InlineData(float.NegativeInfinity, 0.6)]
    public void InvalidMultiplierUsesSafeSize(float multiplier, double expected)
    {
        var layout = new RadialMenuLayout(["entry"], 0, 1, radiusScale: 0.6,
            sizeMultiplier: () => multiplier);

        Assert.Equal(expected, layout.RadiusScale, precision: 6);
    }
    #endregion
}
