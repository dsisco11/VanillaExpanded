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

    /// <summary>Pointer refreshes reuse the face; changed coverage replaces it and reset releases owned resources.</summary>
    [Fact]
    public void PointerChangesReuseFaceWhileCoverageChangesReplaceOwnedPixels()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var faces = new List<LoadedTexture>();
        var dial = new Mock<IApproximateTimeDialRenderer>();
        using var presentation = new ApproximateTimePresentation(context => Enumerable.Range(1, 3)
            .Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray(), dial.Object,
            (context, _) => { var texture = new LoadedTexture(context.Api) { TextureId = 10 + faces.Count }; faces.Add(texture); return texture; });
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.SetContent(ApproximateTimePeriod.Noon, .5, ApproximateTimeDayPhases.Sample(0, _ => .5f));
        presentation.Prepare(context);
        presentation.SetContent(ApproximateTimePeriod.Afternoon, .6, ApproximateTimeDayPhases.Sample(0, _ => .5f));
        presentation.Prepare(context);
        Assert.Single(faces);
        presentation.SetContent(ApproximateTimePeriod.Night, .9, ApproximateTimeDayPhases.Sample(0, _ => -.5f));
        presentation.Prepare(context);
        Assert.Equal(2, faces.Count); Assert.True(faces[0].Disposed);
        presentation.Reset();
        Assert.True(faces[1].Disposed);
    }
    #endregion
}
