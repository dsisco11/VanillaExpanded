using VanillaExpanded.EnvironmentalTemperature;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks radial calibration against caller-supplied housing geometry.</summary>
public sealed class RadialGaugeArtworkTests
{
    #region Public API
    /// <summary>The thermometer mechanism stays inside its supplied housing at arbitrary instrument sizes.</summary>
    [Theory]
    [InlineData(45)] [InlineData(90)] [InlineData(180)]
    public void MechanismRemainsInsideSuppliedHousing(float size)
    {
        var housing = new System.Drawing.RectangleF(13, 27, size, size);
        var mechanism = EnvironmentalTemperatureGaugeLayout.MechanismBounds(housing);
        Assert.True(housing.Contains(mechanism));
    }
    #endregion
}
