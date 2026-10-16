using System.Drawing;
using Moq;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.CalendarDate;

/// <summary>Checks native date rollover, three-letter month content, sandwich order, and world cleanup.</summary>
[Collection("HudOverlayGeometry")]
public sealed class CalendarDateTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    /// <summary>Provides deterministic native month translations without loading the game.</summary>
    public CalendarDateTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => key == "month-October" ? "October" : key);
        Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores the shared translation registry after each test.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }
    /// <summary>Variable-length game months use the native month and year rather than Gregorian day counts.</summary>
    [Theory]
    [InlineData(0, 9, 1)] [InlineData(8, 9, 9)] [InlineData(9, 9, 1)] [InlineData(35, 12, 12)]
    public void NativeDayWithinMonth(int dayOfYear, int daysPerMonth, int expected)
    {
        var calendar = new Mock<IGameCalendar>();
        calendar.SetupGet(value => value.DaysPerMonth).Returns(daysPerMonth);
        calendar.SetupGet(value => value.DayOfYear).Returns(dayOfYear);
        calendar.SetupGet(value => value.MonthName).Returns(EnumMonth.October);
        calendar.SetupGet(value => value.Year).Returns(1387);
        Assert.Equal(new CalendarDateReading(expected, EnumMonth.October, 1387), CalendarDateReading.Read(calendar.Object));
    }

    /// <summary>Calendar rendering scales every layer from one canvas and releases only owned lettering.</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void SandwichOrderAndScale(double scale)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var textValues = new List<string>();
        var owned = new List<LoadedTexture>();
        var backing = new LoadedTexture(api.Object) { TextureId = 1, IgnoreUndisposed = true };
        var glass = new LoadedTexture(api.Object) { TextureId = 2, IgnoreUndisposed = true };
        using var presentation = new CalendarDatePresentation(_ => [backing, glass], () => new WorkshopGaugeText((context, content) =>
        {
            textValues.Add(content);
            var texture = new LoadedTexture(context.Api) { TextureId = 10 + owned.Count, Width = 30, Height = 14 };
            owned.Add(texture); return texture;
        }));
        presentation.SetContent(new(14, EnumMonth.October, 1386));
        var context = new HudOverlayPreparationContext(api.Object, scale, "en");
        presentation.Prepare(context); presentation.Prepare(context);
        Assert.Equal(new[] { "14", "OCT", "1386" }, textValues);
        Assert.Equal(new SizeF(HudOverlaySizes.Calendar, HudOverlaySizes.Calendar * 176 / 512), presentation.Size);
        var renderer = new Mock<IRenderAPI>();
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
        bounds.absFixedX = 20; bounds.absFixedY = 30;
        bounds.absInnerWidth = HudOverlaySizes.Calendar * scale; bounds.absInnerHeight = HudOverlaySizes.Calendar * scale * 176 / 512;
        bounds.Initialized = true;
        presentation.Draw(renderer.Object, bounds);
        Assert.Equal(new[] { 1, 10, 11, 12, 2 }, renderer.Invocations.Select(call => (int)call.Arguments[0]));
        foreach (var call in renderer.Invocations.Where(call => call.Method.Name == "RenderTexture"))
        {
            Assert.Equal(20d, call.Arguments[1]); Assert.Equal(30d, call.Arguments[2]);
            Assert.Equal(HudOverlaySizes.Calendar * scale, call.Arguments[3]);
            Assert.Equal(HudOverlaySizes.Calendar * scale * 176 / 512, call.Arguments[4]);
        }
        presentation.Reset();
        Assert.All(owned, texture => Assert.True(texture.Disposed));
        Assert.False(backing.Disposed); Assert.False(glass.Disposed);
    }

    /// <summary>The independent overlay refreshes on date changes, never queries calendar during drawing, and clears on exit.</summary>
    [Fact]
    public void RolloverAndLifecycle()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); fixture.Entity.Alive = true;
        int day = 8, year = 1386;
        var calendar = new Mock<IClientGameCalendar>();
        calendar.SetupGet(value => value.DaysPerMonth).Returns(9);
        calendar.SetupGet(value => value.DayOfYear).Returns(() => day);
        calendar.SetupGet(value => value.MonthName).Returns(EnumMonth.December);
        calendar.SetupGet(value => value.Year).Returns(() => year);
        fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
        using var overlay = new CalendarDateOverlay(); overlay.BeginSession(fixture.Api.Object);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        day = 0; year = 1387;
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        calendar.Invocations.Clear();
        overlay.Draw(Mock.Of<IRenderAPI>(), ElementBounds.Empty, RectangleF.Empty, 0);
        Assert.Empty(calendar.Invocations);
        overlay.SetEnabled(false); Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.EndSession(); Assert.False(overlay.IsApplicable());
    }
    #endregion
}
