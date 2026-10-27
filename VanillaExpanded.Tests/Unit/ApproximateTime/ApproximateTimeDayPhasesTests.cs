using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks daily solar coverage, polar cases, and independently retained face pixels.</summary>
public sealed class ApproximateTimeDayPhasesTests
{
    #region Public API
    /// <summary>A complete native solar path supplies distinct noon, midnight, dawn, and dusk colors.</summary>
    [Fact]
    public void SolarPathProducesFourPhasesAndFractionalBoundaryHours()
    {
        var phases = ApproximateTimeDayPhases.Sample(7.5,
            day => (float)Math.Cos((day - .5) * 2 * Math.PI))!;
        Assert.Equal(197d / 255, phases.Channel(12, 0), 8);
        Assert.Equal(52d / 255, phases.Channel(0, 0), 8);
        Assert.True(phases.Channel(6, 0) > phases.Channel(18, 0));
        Assert.InRange(phases.Channel(6, 0), 52d / 255, 208d / 255);
    }

    /// <summary>Seasonal sun elevation changes the number of daylight-dominated hours.</summary>
    [Fact]
    public void SeasonalCoverageAndPolarDaysDoNotInventMissingPhases()
    {
        var summer = ApproximateTimeDayPhases.Sample(0, day => .4f + (float)Math.Cos((day - .5) * 2 * Math.PI))!;
        var winter = ApproximateTimeDayPhases.Sample(0, day => -.4f + (float)Math.Cos((day - .5) * 2 * Math.PI))!;
        Assert.True(Enumerable.Range(0, 24).Count(hour => summer.Channel(hour, 0) > .7)
            > Enumerable.Range(0, 24).Count(hour => winter.Channel(hour, 0) > .7));
        var polarDay = ApproximateTimeDayPhases.Sample(0, _ => .5f)!;
        var polarNight = ApproximateTimeDayPhases.Sample(0, _ => -.5f)!;
        for (int hour = 0; hour < 24; hour++)
        {
            Assert.Equal(197d / 255, polarDay.Channel(hour, 0), 8);
            Assert.Equal(52d / 255, polarNight.Channel(hour, 0), 8);
        }
        Assert.Null(ApproximateTimeDayPhases.Sample(double.NaN, _ => 1));
        Assert.Null(ApproximateTimeDayPhases.Sample(0, _ => float.NaN));
    }

    /// <summary>Scenery follows seasonal daylight expansion and the corresponding contraction of night.</summary>
    [Fact]
    public void SceneryCoverageExpandsAndContractsWithSeasonalSunPath()
    {
        var summer = ApproximateTimeDayPhases.Sample(0, day => .4f + (float)Math.Cos((day - .5) * 2 * Math.PI))!;
        var winter = ApproximateTimeDayPhases.Sample(0, day => -.4f + (float)Math.Cos((day - .5) * 2 * Math.PI))!;
        int summerDay = 0, winterDay = 0, summerNight = 0, winterNight = 0;
        // Compare every clipping sample so boundary hours retain their partial seasonal coverage.
        for (int hour = 0; hour < 24; hour++)
        for (int sample = 0; sample < ApproximateTimeDayPhases.SamplesPerHour; sample++)
        {
            summerDay += summer.AllowsScenery(hour, sample, false) ? 1 : 0;
            winterDay += winter.AllowsScenery(hour, sample, false) ? 1 : 0;
            summerNight += summer.AllowsScenery(hour, sample, true) ? 1 : 0;
            winterNight += winter.AllowsScenery(hour, sample, true) ? 1 : 0;
        }
        Assert.True(summerDay > winterDay);
        Assert.True(summerNight < winterNight);
    }

    /// <summary>Polar daylight and darkness reject opposite scenery, while twilight and fallback show neither.</summary>
    [Fact]
    public void PolarTwilightAndNeutralSceneryRespectSolarClassification()
    {
        var polarDay = ApproximateTimeDayPhases.Sample(0, _ => .5f)!;
        var polarNight = ApproximateTimeDayPhases.Sample(0, _ => -.5f)!;
        var twilight = ApproximateTimeDayPhases.Sample(0, _ => -.05f)!;
        for (int hour = 0; hour < 24; hour++)
        for (int sample = 0; sample < ApproximateTimeDayPhases.SamplesPerHour; sample++)
        {
            Assert.True(polarDay.AllowsScenery(hour, sample, false));
            Assert.False(polarDay.AllowsScenery(hour, sample, true));
            Assert.True(polarNight.AllowsScenery(hour, sample, true));
            Assert.False(polarNight.AllowsScenery(hour, sample, false));
            Assert.False(twilight.AllowsScenery(hour, sample, false));
            Assert.False(twilight.AllowsScenery(hour, sample, true));
            Assert.False(ApproximateTimeDayPhases.Neutral.AllowsScenery(hour, sample, false));
            Assert.False(ApproximateTimeDayPhases.Neutral.AllowsScenery(hour, sample, true));
        }
    }




    #endregion
}
