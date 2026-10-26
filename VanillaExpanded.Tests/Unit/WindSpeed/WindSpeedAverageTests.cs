using Moq;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.WindSpeed;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.WindSpeed;

/// <summary>Checks recent wind sample averaging and its overlay lifetime boundaries.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WindSpeedAverageTests
{
    #region Public API
    /// <summary>Startup uses available readings and irregular samples expire exactly at the rolling window boundary.</summary>
    [Fact]
    public void RecentMeanExpiresSamplesAtWindowBoundary()
    {
        var average = new WindSpeedAverage();
        Assert.Equal(.8, average.Update(.8, 100));
        Assert.Equal(.5, average.Update(.2, 700), 12);
        Assert.Equal(.6, average.Update(.8, 5099), 12);
        Assert.Equal(.4, average.Update(.2, 5100), 12);
        Assert.Equal(.9, average.Update(.9, 11000), 12);
    }

    /// <summary>Repeated timestamps replace the current reading and backwards clocks start fresh history.</summary>
    [Fact]
    public void DuplicateAndBackwardsTimesDoNotBiasHistory()
    {
        var average = new WindSpeedAverage();
        average.Update(.8, 100);
        Assert.Equal(.2, average.Update(.2, 100));
        Assert.Equal(.5, average.Update(.8, 200), 12);
        Assert.Equal(.3, average.Update(.4, 200), 12);
        Assert.Equal(.9, average.Update(.9, 50));
    }

    /// <summary>Real calm participates in the mean while invalid data clears it immediately.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.1)]
    public void CalmAndInvalidInputsHaveDifferentLifetime(double invalid)
    {
        var average = new WindSpeedAverage();
        average.Update(.8, 0);
        Assert.Equal(.4, average.Update(0, 300), 12);
        Assert.Equal(0, average.Update(invalid, 600));
        Assert.Equal(.2, average.Update(.2, 900));
        average.Reset();
        Assert.Equal(.9, average.Update(.9, 1200));
    }

    /// <summary>The overlay averages before classification and clears old readings on disable, unavailable data, and world exit.</summary>
    [Fact]
    public void OverlaySmoothsPercentageAndResetsAtLifecycleBoundaries()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        long now = 0;
        fixture.World.SetupGet(value => value.ElapsedMilliseconds).Returns(() => now);
        Vec3d? wind = new(.8, 0, 0);
        var blocks = new Mock<IBlockAccessor>();
        blocks.Setup(value => value.GetWindSpeedAt(It.IsAny<Vec3d>())).Returns(() => wind!);
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        using var overlay = new WindSpeedOverlay();
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        Assert.Equal(80, overlay.Percentage);
        now = 300; wind = new(.2, 0, 0);
        overlay.Refresh();
        Assert.Equal(50, overlay.Percentage);
        Assert.Equal(2, overlay.Intensity);
        now = 600; wind = new(0, 0, 0);
        overlay.Refresh();
        Assert.True(overlay.Percentage > 0);
        now = 900; wind = null;
        overlay.Refresh();
        Assert.Equal(0, overlay.Percentage);
        Assert.Equal(0, overlay.Intensity);
        now = 1200; wind = new(.8, 0, 0);
        overlay.Refresh();
        Assert.Equal(80, overlay.Percentage);
        overlay.SetEnabled(false); overlay.SetEnabled(true);
        now = 1500; wind = new(.2, 0, 0);
        overlay.Refresh();
        Assert.Equal(20, overlay.Percentage);
        overlay.EndSession(); overlay.BeginSession(fixture.Api.Object);
        now = 1800; wind = new(.8, 0, 0);
        overlay.Refresh();
        Assert.Equal(80, overlay.Percentage);
    }
    #endregion
}
