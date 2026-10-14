using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks the combined thermometer's independent numeric, liquid, and resource contracts.</summary>
[Collection("HudOverlayGeometry")]
public sealed class ApproximateTimeTemperatureTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    /// <summary>Provides deterministic localized Celsius readings without game startup.</summary>
    public ApproximateTimeTemperatureTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] arguments) => arguments.Length == 1 ? $"{arguments[0]}°C" : key);
        Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores the shared language registry.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }
    /// <summary>The rim clamps to its endpoints while the number retains the actual temperature.</summary>
    [Theory]
    [InlineData(-40, 0, "-40°C")]
    [InlineData(10, .5f, "10°C")]
    [InlineData(55, 1, "55°C")]
    public void RimClampsWithoutClampingReading(float temperature, float fill, string expected)
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        string? reading = null;
        using var text = new HudOverlayIconTextPresentation((context, value) =>
        {
            reading = value;
            return new LoadedTexture(context.Api) { TextureId = 10, Width = 30, Height = 14 };
        });
        var dial = new Mock<IApproximateTimeDialRenderer>();
        using var presentation = new ApproximateTimePresentation(Load, _ => dial.Object, text);
        presentation.SetContent(ApproximateTimePeriod.Noon, .5);
        presentation.SetTemperature(temperature);
        presentation.Prepare(new(api.Object, 1, "en"));
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, Bounds());
        Assert.Equal(expected, reading);
        Assert.Equal(new SizeF(64, 64), presentation.Size);
        dial.Verify(value => value.DrawTemperature(renderer.Object, new RectangleF(0, 0, 64, 64), 1, fill), Times.Once);
        Assert.Equal(3, Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture").Arguments[0]);
    }
    /// <summary>Rounded readings reuse owned pixels, missing readings restore plain housing, and reset releases resources.</summary>
    [Fact]
    public void ReadingCacheMissingTemperatureAndCleanup()
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        var owned = new List<LoadedTexture>();
        using var text = new HudOverlayIconTextPresentation((context, _) =>
        {
            var texture = new LoadedTexture(context.Api) { TextureId = 10 + owned.Count, Width = 30, Height = 14 };
            owned.Add(texture); return texture;
        });
        var dial = new Mock<IApproximateTimeDialRenderer>();
        using var presentation = new ApproximateTimePresentation(Load, _ => dial.Object, text);
        var preparation = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.SetContent(ApproximateTimePeriod.Noon, .5);
        foreach (float value in new[] { 12.1f, 12.4f }) { presentation.SetTemperature(value); presentation.Prepare(preparation); }
        Assert.Single(owned);
        presentation.SetTemperature(12.6f); presentation.Prepare(preparation);
        Assert.Equal(2, owned.Count); Assert.True(owned[0].Disposed);
        presentation.SetTemperature(null); presentation.Prepare(preparation);
        Assert.True(owned[1].Disposed);
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, Bounds());
        Assert.Equal(1, Assert.Single(renderer.Invocations).Arguments[0]);
        dial.Verify(value => value.DrawTemperature(It.IsAny<IRenderAPI>(), It.IsAny<RectangleF>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
        presentation.Reset();
        dial.Verify(value => value.Dispose(), Times.Once);
        Assert.Equal(SizeF.Empty, presentation.Size);
    }
    /// <summary>Current climate updates independently of remembered time and config-off removes the combined thermometer.</summary>
    [Fact]
    public void ClimateMissingAndDisabledRemoveThermometer()
    {
        bool previous = VanillaExpandedModSystem.Config.EnableEnvironmentalTemperatureOverlay;
        try
        {
            VanillaExpandedModSystem.Config.EnableEnvironmentalTemperatureOverlay = true;
            var fixture = new VanillaExpanded.Tests.Unit.BowAmmunition.BowAmmunitionSamplerTests.Fixture();
            fixture.Entity.Alive = true;
            ApproximateTimeState.Update(fixture.Entity, 4, false);
            var calendar = new Mock<IClientGameCalendar>();
            calendar.SetupGet(value => value.TotalDays).Returns(.5);
            calendar.SetupGet(value => value.HoursPerDay).Returns(24);
            calendar.Setup(value => value.GetSunPosition(It.IsAny<Vintagestory.API.MathTools.Vec3d>(), It.IsAny<double>()))
                .Returns(new Vintagestory.API.MathTools.Vec3f(0, 1, 0));
            fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
            var blocks = new Mock<Vintagestory.API.Common.IBlockAccessor>();
            Vintagestory.API.Common.ClimateCondition? climate = new() { Temperature = 10 };
            blocks.Setup(value => value.GetClimateAt(It.IsAny<Vintagestory.API.MathTools.BlockPos>(),
                Vintagestory.API.Common.EnumGetClimateMode.NowValues, It.IsAny<double>())).Returns(() => climate!);
            fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
            using var text = new HudOverlayIconTextPresentation((context, _) => new LoadedTexture(context.Api)
                { TextureId = 10, Width = 30, Height = 14 });
            var dial = new Mock<IApproximateTimeDialRenderer>();
            using var overlay = new ApproximateTimeOverlay(new ApproximateTimePresentation(Load, _ => dial.Object, text));
            overlay.BeginSession(fixture.Api.Object);
            var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
            var renderer = new Mock<IRenderAPI>();
            overlay.Refresh(); overlay.Prepare(preparation); overlay.Draw(renderer.Object, Bounds(), new(0, 0, 64, 64), 0);
            dial.Verify(value => value.DrawTemperature(renderer.Object, It.IsAny<RectangleF>(), 1, .5f), Times.Once);
            climate = null;
            overlay.Refresh(); overlay.Prepare(preparation); renderer.Invocations.Clear();
            overlay.Draw(renderer.Object, Bounds(), new(0, 0, 64, 64), 0);
            Assert.Equal(1, Assert.Single(renderer.Invocations).Arguments[0]);
            climate = new() { Temperature = 30 };
            VanillaExpandedModSystem.Config.EnableEnvironmentalTemperatureOverlay = false;
            blocks.Invocations.Clear();
            overlay.Refresh(); overlay.Prepare(preparation); renderer.Invocations.Clear();
            overlay.Draw(renderer.Object, Bounds(), new(0, 0, 64, 64), 0);
            Assert.Empty(blocks.Invocations);
            Assert.Equal(1, Assert.Single(renderer.Invocations).Arguments[0]);
            dial.Verify(value => value.DrawTemperature(renderer.Object, It.IsAny<RectangleF>(), It.IsAny<float>(), It.IsAny<float>()), Times.Once);
        }
        finally { VanillaExpandedModSystem.Config.EnableEnvironmentalTemperatureOverlay = previous; }
    }
    #endregion
    #region Private
    /// <summary>Supplies borrowed artwork without GPU allocation.</summary>
    private static LoadedTexture[] Load(HudOverlayPreparationContext context) => Enumerable.Range(1, 3)
        .Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray();
    /// <summary>Creates native pixel bounds independent of shared GUI scaling.</summary>
    private static ElementBounds Bounds()
    {
        var bounds = ElementBounds.Fixed(0, 0, 64, 64).WithEmptyParent();
        bounds.absFixedX = bounds.absFixedY = 0; bounds.absInnerWidth = bounds.absInnerHeight = 64; bounds.Initialized = true;
        return bounds;
    }
    #endregion
}
