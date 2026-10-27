using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.WindSpeed;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.WindSpeed;

/// <summary>Checks wind band boundaries and native sampling independently of composite weather.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WindSpeedTests
{
    #region Public API
    #region Conversion and classification
    /// <summary>Percentage readings truncate like windmills and retain above-normal wind without invalid numeric output.</summary>
    [Theory]
    [InlineData(.659, 65)]
    [InlineData(1.25, 125)]
    [InlineData(.009, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(double.MaxValue, int.MaxValue)]
    public void WindmillPercentageConversion(double speed, int expected)
    {
        Assert.Equal(expected, WindSpeedSample.ToPercentage(speed));
    }

    /// <summary>Exact entry and exit boundaries remain inactive while values above them activate the band.</summary>
    [Theory]
    [InlineData(.05f, 0, 0)]
    [InlineData(.051f, 0, 1)]
    [InlineData(.30f, 0, 1)]
    [InlineData(.301f, 0, 2)]
    [InlineData(.70f, 0, 2)]
    [InlineData(.701f, 0, 3)]
    [InlineData(.035f, 1, 0)]
    [InlineData(.036f, 1, 1)]
    [InlineData(.25f, 2, 1)]
    [InlineData(.251f, 2, 2)]
    [InlineData(.60f, 3, 2)]
    [InlineData(.601f, 3, 3)]
    [InlineData(float.NaN, 3, 0)]
    [InlineData(float.PositiveInfinity, 3, 0)]
    [InlineData(-1, 3, 0)]
    public void StableBandBoundaries(float speed, int previous, int expected)
    {
        Assert.Equal(expected, WindSpeedSample.Classify(speed, previous));
    }

    #endregion
    #region Sampling
    /// <summary>Sampling needs only local wind, reuses stable readings, and clears history across eligibility changes.</summary>
    [Fact]
    public void NativeWindDoesNotRequireWeatherOrClimate()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var blocks = new Mock<IBlockAccessor>(MockBehavior.Strict);
        var wind = new Vec3d(.8, 0, 0);
        blocks.Setup(value => value.GetWindSpeedAt(It.IsAny<Vec3d>())).Returns(() => wind);
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        using var overlay = new WindSpeedOverlay();
        overlay.BeginSession(fixture.Api.Object);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(3, overlay.Intensity);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        wind = new Vec3d(.65, 0, 0);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(3, overlay.Intensity);
        Assert.Equal(65, overlay.Percentage);
        wind = new Vec3d(.659, 0, 0);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(65, overlay.Percentage);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable());
        Assert.Equal(0, overlay.Intensity);
        overlay.SetEnabled(true);
        overlay.Refresh();
        Assert.Equal(2, overlay.Intensity);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
        Assert.Equal(0, overlay.Intensity);
        overlay.EndSession();
        Assert.False(overlay.IsApplicable());
        blocks.VerifyAll();
    }

    #endregion
    #endregion
}
