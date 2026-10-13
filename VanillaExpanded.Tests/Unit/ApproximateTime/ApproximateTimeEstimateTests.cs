using VanillaExpanded.ApproximateTime;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks persisted observation age, bounded drift, and solar classification.</summary>
public sealed class ApproximateTimeEstimateTests
{
    #region Public API
    /// <summary>Persisted age determines bounded clock error.</summary>
    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    public void AgeDeterminesCappedDrift(double hoursPerDay)
    {
        Assert.Equal(0d, ApproximateTimeEstimate.GetDayOffset(0, hoursPerDay, "player"));
        Assert.Equal(1 / hoursPerDay, Math.Abs(ApproximateTimeEstimate.GetDayOffset(10, hoursPerDay, "player")!.Value), 10);
        Assert.Equal(1d / 3, Math.Abs(ApproximateTimeEstimate.GetDayOffset(1000, hoursPerDay, "player")!.Value), 10);
        Assert.Equal(-ApproximateTimeEstimate.GetDayOffset(10, hoursPerDay, "p1")!.Value,
            ApproximateTimeEstimate.GetDayOffset(10, hoursPerDay, "p2")!.Value);
        Assert.Null(ApproximateTimeEstimate.GetDayOffset(double.NaN, hoursPerDay, "player"));
    }
    /// <summary>Entity serialization retains age and sunlight resets the restored counter.</summary>
    [Fact]
    public void ObservationAgeSurvivesSerialization()
    {
        var entity = new Vintagestory.API.Common.EntityPlayer();
        ApproximateTimeState.Update(entity, 10, false);
        ApproximateTimeState.Update(entity, 2, false);
        var restored = new Vintagestory.API.Common.EntityPlayer();
        restored.WatchedAttributes.FromBytes(entity.WatchedAttributes.ToBytes());
        Assert.Equal(12, ApproximateTimeState.Read(restored));
        ApproximateTimeState.Update(restored, double.NaN, false);
        Assert.Equal(12, ApproximateTimeState.Read(restored));
        ApproximateTimeState.Update(restored, 1, true);
        Assert.Equal(0, ApproximateTimeState.Read(restored));
    }
    /// <summary>Solar classification follows elevation and rising direction rather than fixed hour bands.</summary>
    [Theory]
    [InlineData(-.4f, -.3f, -.2f, 6)]
    [InlineData(-.1f, -.05f, 0, 1)]
    [InlineData(0, -.05f, -.1f, 5)]
    [InlineData(.2f, .4f, .6f, 2)]
    [InlineData(.6f, .4f, .2f, 4)]
    [InlineData(.3f, .31f, .3f, 3)]
    [InlineData(.08f, .09f, .08f, 3)]
    [InlineData(.9f, 1, .9f, 3)]
    public void SolarElevationChoosesArtwork(float earlier, float current, float later, int expected)
    {
        Assert.Equal(expected, (int)ApproximateTimeSolarPeriod.FromSunHeight(earlier, current, later));
    }
    /// <summary>Invalid solar samples remain unknown instead of replacing cached valid artwork.</summary>
    [Theory]
    [InlineData(float.NaN, 0, 0)]
    [InlineData(0, float.PositiveInfinity, 0)]
    [InlineData(0, 0, float.NegativeInfinity)]
    public void InvalidElevationIsUnknown(float earlier, float current, float later)
    {
        Assert.Equal(ApproximateTimePeriod.Unknown, ApproximateTimeSolarPeriod.FromSunHeight(earlier, current, later));
    }
    #endregion
}
