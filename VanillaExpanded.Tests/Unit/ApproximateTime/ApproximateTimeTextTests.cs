using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks actual calendar text independently of the drifting hand and cached artwork.</summary>
[Collection("HudOverlayGeometry")]
public sealed class ApproximateTimeTextTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Provides a short label without native language initialization.</summary>
    public ApproximateTimeTextTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("Time");
        Lang.AvailableLanguages["en"] = language.Object;
    }

    /// <summary>Restores the shared language service.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Actual calendar hours override hand drift and retain midnight, leading zeros, and complete minutes.</summary>
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(3.084, "03:05")]
    [InlineData(18.999, "18:59")]
    [InlineData(24, "00:00")]
    public void ActualHourFormatsIndependentOfEstimatedHand(double actualHour, string expected)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var readings = new List<string>();
        using var presentation = new ApproximateTimePresentation(textStyle: CaptureText(readings));
        presentation.SetStyle(EnvironmentalGaugeStyle.Text);
        presentation.SetContent(ApproximateTimePeriod.Afternoon, .75, actualHour: actualHour);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 1, "en"));
        Assert.Equal(expected, Assert.Single(readings));
    }

    /// <summary>A minute change updates cached text while reset clears the previous reading until new content arrives.</summary>
    [Fact]
    public void MinuteProgressionAndResetRefreshText()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var readings = new List<string>();
        using var presentation = new ApproximateTimePresentation(textStyle: CaptureText(readings));
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.SetStyle(EnvironmentalGaugeStyle.Text);
        Assert.True(presentation.SetContent(ApproximateTimePeriod.Noon, .5, actualHour: 12.009));
        presentation.Prepare(context);
        Assert.False(presentation.SetContent(ApproximateTimePeriod.Noon, .5, actualHour: 12.01));
        Assert.True(presentation.SetContent(ApproximateTimePeriod.Noon, .5, actualHour: 12.02));
        presentation.Prepare(context);
        Assert.Equal(new[] { "12:00", "12:01" }, readings);
        presentation.Reset();
        presentation.Prepare(context);
        Assert.Equal(2, readings.Count);
        presentation.SetContent(ApproximateTimePeriod.Night, .9, actualHour: .017);
        presentation.Prepare(context);
        Assert.Equal("00:01", readings.Last());
    }

    /// <summary>The overlay forwards the calendar's displayed hour even when its total days and observation age differ.</summary>
    [Fact]
    public void OverlayForwardsActualCalendarHour()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        ApproximateTimeState.Update(fixture.Entity, 4, false);
        var calendar = new Mock<IClientGameCalendar>();
        float actualHour = 4.5f;
        calendar.SetupGet(value => value.TotalDays).Returns(.75);
        calendar.SetupGet(value => value.HoursPerDay).Returns(24);
        calendar.SetupGet(value => value.HourOfDay).Returns(() => actualHour);
        calendar.Setup(value => value.GetSunPosition(It.IsAny<Vec3d>(), It.IsAny<double>())).Returns(new Vec3f(0, .5f, 0));
        fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
        var readings = new List<string>();
        using var overlay = new ApproximateTimeOverlay(new ApproximateTimePresentation(textStyle: CaptureText(readings)));
        overlay.SetStyle(EnvironmentalGaugeStyle.Text);
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        overlay.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"));
        Assert.Equal("04:30", Assert.Single(readings));
        actualHour = 4.51f;
        Assert.False(overlay.Refresh().HasFlag(HudOverlayChange.Measurement));
        actualHour = 4.52f;
        Assert.True(overlay.Refresh().HasFlag(HudOverlayChange.Measurement));
        overlay.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"));
        Assert.Equal("04:31", readings.Last());
    }
    #endregion

    #region Private
    /// <summary>Captures reading text through the real text presentation while bypassing native texture creation.</summary>
    private static HudIndicatorTextPresentation CaptureText(List<string> readings)
    {
        LoadedTexture Create(HudOverlayPreparationContext context, string value) =>
            new(context.Api) { TextureId = 1, Width = value.Length * 7, Height = 14 };
        return new(new WorkshopGaugeText(Create), new WorkshopGaugeText((context, value) =>
        {
            readings.Add(value);
            return Create(context, value);
        }));
    }
    #endregion
}
