using System.Drawing;
using Moq;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks refresh invalidation when presentation preferences change between identical gameplay samples.</summary>
[Collection("HudOverlayGeometry")]
public sealed class EnvironmentalGaugeStyleRefreshTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Provides deterministic language content without initializing a native client.</summary>
    public EnvironmentalGaugeStyleRefreshTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => args.Length == 1 ? $"{args[0]}°C" : key == "month-October" ? "October" : key);
        Lang.AvailableLanguages["en"] = service.Object;
    }

    /// <summary>Restores shared language state after the overlay contract tests.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }
    /// <summary>An unchanged climate sample still schedules preparation and measurement after a live style switch.</summary>
    [Fact]
    public void UnchangedTemperatureInvalidatesStyleAndMeasuresCompactPresentation()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var blocks = new Mock<IBlockAccessor>();
        blocks.Setup(value => value.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.NowValues, 0))
            .Returns(new ClimateCondition { Temperature = 12 });
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        using var overlay = new EnvironmentalTemperatureOverlay(new EnvironmentalTemperaturePresentation(measure: (_, value) => new(value.Length * 7, 13)));
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        overlay.SetStyle(EnvironmentalGaugeStyle.Icon);
        Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
        overlay.Prepare(new(fixture.Api.Object, 1, "en"));
        Assert.NotEqual(SizeF.Empty, overlay.Measure());
        Assert.Equal(12f, overlay.Temperature);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        overlay.SetStyle(EnvironmentalGaugeStyle.Immersive);
        Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
    }

    /// <summary>A fixed calendar date still schedules preparation and measurement after a live style switch.</summary>
    [Fact]
    public void UnchangedDateInvalidatesStyleAndMeasuresCompactPresentation()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var calendar = new Mock<IClientGameCalendar>();
        calendar.SetupGet(value => value.DaysPerMonth).Returns(9);
        calendar.SetupGet(value => value.DayOfYear).Returns(8);
        calendar.SetupGet(value => value.MonthName).Returns(EnumMonth.October);
        fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
        using var overlay = new CalendarDateOverlay(new CalendarDatePresentation(measure: (_, value) => new(value.Length * 7, 13)));
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        overlay.SetStyle(EnvironmentalGaugeStyle.Icon);
        Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
        overlay.Prepare(new(fixture.Api.Object, 1, "en"));
        Assert.NotEqual(SizeF.Empty, overlay.Measure());
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        overlay.SetStyle(EnvironmentalGaugeStyle.Immersive);
        Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
    }
    #endregion

}
