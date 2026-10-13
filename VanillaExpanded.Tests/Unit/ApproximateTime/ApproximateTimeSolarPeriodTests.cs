using VanillaExpanded.ApproximateTime;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Exercises complete seasonal sun paths and retained phase boundaries without a graphics context.</summary>
public sealed class ApproximateTimeSolarPeriodTests
{
    #region Public API
    /// <summary>Ordinary summer, winter, and equinox cycles include night between sunset and sunrise.</summary>
    [Theory]
    [InlineData(45, 23.44)] [InlineData(45, -23.44)] [InlineData(45, 0)] [InlineData(0, 0)]
    public void SeasonalCyclesContainEveryPhase(double latitude, double declination)
    {
        Assert.Equal(new[] { ApproximateTimePeriod.Night, ApproximateTimePeriod.Dawn,
            ApproximateTimePeriod.Morning, ApproximateTimePeriod.Noon, ApproximateTimePeriod.Afternoon,
            ApproximateTimePeriod.Dusk, ApproximateTimePeriod.Night }, Cycle(latitude, declination, 0));
    }

    /// <summary>Continuous polar twilight does not manufacture night, while polar winter remains night.</summary>
    [Fact]
    public void PolarCyclesRespectTheActualSunPath()
    {
        var summer = Cycle(65, 23.44, 0);
        Assert.DoesNotContain(ApproximateTimePeriod.Night, summer);
        Assert.Contains(ApproximateTimePeriod.Dawn, summer);
        Assert.Contains(ApproximateTimePeriod.Dusk, summer);
        Assert.Equal(new[] { ApproximateTimePeriod.Night }, Cycle(80, -23.44, 0));
    }

    /// <summary>A persisted clock offset shifts the same seasonal phases rather than bypassing solar classification.</summary>
    [Fact]
    public void ClockDriftShiftsTheSeasonalCycle()
    {
        double offset = ApproximateTimeEstimate.GetDayOffset(1000, 24, "player")!.Value;
        var normal = Cycle(45, 0, 0);
        var shifted = Cycle(45, 0, offset);
        Assert.Contains(ApproximateTimePeriod.Night, shifted);
        Assert.Contains(ApproximateTimePeriod.Noon, shifted);
        Assert.NotEqual(normal[0], shifted[0]);
    }

    /// <summary>Half-degree boundary margins retain the old phase until the sun crosses the opposite edge.</summary>
    [Theory]
    [InlineData(-6.2, 5, false, 5)] [InlineData(-6.6, 5, false, 6)]
    [InlineData(-5.8, 6, true, 6)] [InlineData(-5.4, 6, true, 1)]
    [InlineData(.2, 1, true, 1)] [InlineData(.6, 1, true, 2)]
    [InlineData(-.2, 4, false, 4)] [InlineData(-.6, 4, false, 5)]
    public void BoundaryHysteresisPreventsFlicker(double elevation, int previous, bool rising, int expected)
    {
        float height = (float)Math.Sin(elevation * Math.PI / 180);
        float direction = rising ? .02f : -.02f;
        Assert.Equal((ApproximateTimePeriod)expected, ApproximateTimeSolarPeriod.FromSunHeight(
            height - direction, height, height + direction, (ApproximateTimePeriod)previous));
    }
    #endregion

    #region Private
    /// <summary>Samples one full daily path with the same neighboring-day offsets used by the overlay.</summary>
    private static List<ApproximateTimePeriod> Cycle(double latitude, double declination, double offset)
    {
        var phases = new List<ApproximateTimePeriod>();
        ApproximateTimePeriod? previous = null;
        for (int minute = 0; minute <= 1440; minute++)
        {
            double day = minute / 1440d + offset;
            var period = ApproximateTimeSolarPeriod.FromSunHeight(Height(latitude, declination, day - .02),
                Height(latitude, declination, day), Height(latitude, declination, day + .02), previous);
            if (period != previous) phases.Add(period);
            previous = period;
        }
        return phases;
    }

    /// <summary>Evaluates normalized solar height using the native latitude, declination, and hour-angle relationship.</summary>
    private static float Height(double latitude, double declination, double day)
    {
        double latitudeRadians = latitude * Math.PI / 180, declinationRadians = declination * Math.PI / 180;
        double hourAngle = 2 * Math.PI * (day - .5);
        return (float)(Math.Sin(latitudeRadians) * Math.Sin(declinationRadians)
            + Math.Cos(latitudeRadians) * Math.Cos(declinationRadians) * Math.Cos(hourAngle));
    }
    #endregion
}
