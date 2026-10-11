using VanillaExpanded.ApproximateTime;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks clock uncertainty, sunlight calibration, midnight wrapping, and lifetime discontinuities.</summary>
public sealed class ApproximateTimeEstimateTests
{
    #region Public API
    /// <summary>Entry under cover seeds an estimate, while sunlight subsequently recalibrates it.</summary>
    [Fact]
    public void StartsWithEstimateAndCalibratesInSunlight()
    {
        var estimate = new ApproximateTimeEstimate();
        estimate.Observe(100, 4, 24, false, 2);
        Assert.Equal(4, estimate.Hour);
        estimate.Observe(104, 8, 24, true, 2);
        Assert.Equal(8, estimate.Hour);
    }
    /// <summary>Error accumulates in either direction, and current authoritative hour is ignored under cover.</summary>
    [Theory]
    [InlineData(2, 19)]
    [InlineData(3, 17)]
    public void UnobservedClockDriftsAndRecalibrates(long entityId, double expected)
    {
        var estimate = new ApproximateTimeEstimate();
        estimate.Observe(8, 8, 24, true, entityId);
        estimate.Observe(18, 0, 24, false, entityId);
        Assert.Equal(expected, estimate.Hour);
        estimate.Observe(18, 18, 24, true, entityId);
        Assert.Equal(18, estimate.Hour);
    }
    /// <summary>Normal midnight progression wraps rather than invalidating the remembered hour.</summary>
    [Fact]
    public void MidnightAndCustomDayLength()
    {
        var estimate = new ApproximateTimeEstimate();
        estimate.Observe(23.75, 23.75, 24, true, 2);
        estimate.Observe(24.25, .25, 24, false, 2);
        Assert.Equal(.3, estimate.Hour!.Value, 6);
        estimate.Observe(25, 6, 12, true, 2);
        Assert.Equal(12, estimate.Hour);
    }
    /// <summary>Long underground stays stop increasing error after one third of the day.</summary>
    [Theory]
    [InlineData(24, 2, 8)]
    [InlineData(24, 3, 16)]
    [InlineData(12, 2, 8)]
    [InlineData(12, 3, 16)]
    public void DriftCapsWithoutStoppingTheClock(double hoursPerDay, long entityId, double expected)
    {
        var estimate = new ApproximateTimeEstimate();
        estimate.Observe(0, 0, hoursPerDay, true, entityId);
        for (int hour = 1; hour <= 120; hour++)
            estimate.Observe(hour, hour % hoursPerDay, hoursPerDay, false, entityId);
        Assert.Equal(expected, estimate.Hour!.Value, 6);
    }
    /// <summary>Time reversal, malformed input, and explicit reset discard calibration.</summary>
    [Fact]
    public void DiscontinuitiesDiscardCalibration()
    {
        var estimate = new ApproximateTimeEstimate();
        estimate.Observe(8, 8, 24, true, 2);
        estimate.Observe(7, 7, 24, false, 2);
        Assert.Equal(7, estimate.Hour);
        estimate.Observe(8, 8, 24, true, 2);
        estimate.Observe(double.NaN, 8, 24, false, 2);
        Assert.Null(estimate.Hour);
        estimate.Observe(8, 8, 24, true, 2);
        estimate.Reset();
        Assert.Null(estimate.Hour);
    }
    /// <summary>Solar classification follows light and rising direction rather than fixed hour bands.</summary>
    [Theory]
    [InlineData(-.4f, -.3f, -.2f, .05f, 6)]
    [InlineData(-.1f, 0, .1f, .4f, 1)]
    [InlineData(.1f, 0, -.1f, .4f, 5)]
    [InlineData(.2f, .4f, .6f, .8f, 2)]
    [InlineData(.6f, .4f, .2f, .8f, 4)]
    [InlineData(.3f, .31f, .3f, .8f, 3)]
    [InlineData(.08f, .09f, .08f, .8f, 3)]
    [InlineData(.9f, 1, .9f, .8f, 3)]
    public void SolarElevationChoosesArtwork(float earlier, float current, float later, float intensity, int expected)
    {
        Assert.Equal(expected, (int)ApproximateTimeSolarPeriod.FromSunHeight(earlier, current, later, intensity));
    }
    /// <summary>The absolute solar timestamp retains the accumulated personal clock error.</summary>
    [Fact]
    public void SolarDateUsesEstimatedTime()
    {
        var estimate = new ApproximateTimeEstimate();
        Assert.Null(estimate.EstimatedDayOffset);
        estimate.Observe(104, 8, 24, true, 2);
        Assert.Equal(0, estimate.EstimatedDayOffset);
        estimate.Observe(114, 18, 24, false, 2);
        Assert.Equal(1d / 24, estimate.EstimatedDayOffset);
    }
    /// <summary>Twilight responds to intensity instead of an elevation cutoff or color hue.</summary>
    [Theory]
    [InlineData(-.4f, -.3f, -.2f, 1)]
    [InlineData(-.2f, -.3f, -.4f, 5)]
    public void SolarIntensityOverridesElevationCutoff(float earlier, float current, float later, int expected)
    {
        Assert.Equal(expected, (int)ApproximateTimeSolarPeriod.FromSunHeight(earlier, current, later, .3f));
        Assert.Equal(ApproximateTimePeriod.Night, ApproximateTimeSolarPeriod.FromSunHeight(earlier, current, later, .05f));
    }
    #endregion
}