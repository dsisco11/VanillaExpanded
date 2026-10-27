using VanillaExpanded.HudOverlays.Rendering;
using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Registration;

using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.Tests.Unit.HudOverlays;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks native sunlight sampling and cached presentation without graphics calls.</summary>
[Collection("HudOverlayGeometry")]
public sealed class ApproximateTimeOverlayTests
{
    #region Public API
    /// <summary>The pointer turns once per day, with noon up and midnight down.</summary>
    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(0.75, Math.PI / 2)]
    [InlineData(0, -Math.PI)]
    [InlineData(0.25, -Math.PI / 2)]
    public void DialRotation(double day, double expected)
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        using var presentation = new ApproximateTimePresentation();
        presentation.SetContent(ApproximateTimePeriod.Noon, day);
        presentation.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", 0));
        Assert.Equal(expected, presentation.Rotation, 5);
        presentation.Reset();
    }
    /// <summary>Rendering consumes saved observation age without client terrain sampling or lifetime mutation.</summary>
    [Fact]
    public void SunlightSamplingAndLifecycle()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        fixture.Entity.EntityId = 2;
        ApproximateTimeState.Update(fixture.Entity, 4, false);
        var calendar = new Mock<IClientGameCalendar>();
        double hours = 8;
        var sun = new Vec3f(0, 1, 0);
        int sunlight = 0;
        calendar.SetupGet(x => x.TotalDays).Returns(() => hours / 24);
        calendar.SetupGet(x => x.TotalHours).Returns(() => hours);
        calendar.SetupGet(x => x.HourOfDay).Returns(() => (float)(hours % 24));
        calendar.SetupGet(x => x.HoursPerDay).Returns(24);
        calendar.SetupGet(x => x.SunPositionNormalized).Returns(sun);
        calendar.SetupGet(x => x.SunLightStrength).Returns(() => sun.Y > 0 ? .8f : .05f);
        calendar.Setup(x => x.GetSunPosition(It.IsAny<Vec3d>(), It.IsAny<double>()))
            .Returns((Vec3d position, double day) => new Vec3f(0, (float)Math.Cos((day % 1 - .5) * 2 * Math.PI), 0));
        fixture.World.SetupGet(x => x.Calendar).Returns(calendar.Object);
        var blocks = new Mock<IBlockAccessor>();
        blocks.Setup(x => x.GetLightLevel(It.IsAny<BlockPos>(), EnumLightLevelType.OnlySunLight)).Returns(() => sunlight);
        fixture.World.SetupGet(x => x.BlockAccessor).Returns(blocks.Object);
        var renderer = new Mock<IRenderAPI>();
        fixture.Api.SetupGet(x => x.Render).Returns(renderer.Object);
        using var native = new InstrumentIndicatorsFixture();
        var (sampled, presentation, _, _) = native.Create("time", VanillaExpanded.HudOverlays.Configuration.EnvironmentalGaugeStyle.Immersive);
        using var overlay = (ApproximateTimeOverlay)sampled;
        overlay.BeginSession(fixture.Api.Object);
        var preparation = native.Context;
        overlay.Refresh(); overlay.Prepare(preparation);
        using var composition = native.Mount(overlay, presentation);
        sunlight = 10;
        overlay.Refresh(); overlay.Prepare(preparation);
        sunlight = 0; hours = 18;
        overlay.Refresh(); overlay.Prepare(preparation);
        overlay.Refresh(); overlay.Prepare(preparation);
        blocks.Invocations.Clear(); calendar.Invocations.Clear();
        composition.View.Prepare(preparation); composition.Composer.Render(0);
        Assert.Empty(blocks.Invocations); Assert.Empty(calendar.Invocations);
        sunlight = 10; sun.Y = -1; hours = 19;
        overlay.Refresh(); overlay.Prepare(preparation);
        sun.Y = 1;
        blocks.Setup(x => x.GetRainMapHeightAt(It.IsAny<BlockPos>())).Returns(100);
        overlay.Refresh(); overlay.Prepare(preparation);
        blocks.Verify(x => x.GetRainMapHeightAt(It.IsAny<BlockPos>()), Times.Never);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.SetEnabled(true); sunlight = 10; sun.Y = -1; hours = 2;
        overlay.Refresh(); overlay.Prepare(preparation);
        renderer.Invocations.Clear();
        composition.View.Prepare(preparation); composition.Composer.Render(0);
        overlay.SetEnabled(false);
        overlay.SetEnabled(true); sunlight = 0;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.NotEqual(SizeF.Empty, overlay.Measure());
        renderer.Invocations.Clear();
        composition.View.Prepare(preparation); composition.Composer.Render(0);
        hours = double.NaN;
        var previousSize = overlay.Measure();
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(previousSize, overlay.Measure());
        renderer.Invocations.Clear();
        composition.View.Prepare(preparation); composition.Composer.Render(0);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.EndSession(); Assert.False(overlay.IsApplicable());
        Assert.Equal(4, ApproximateTimeState.Read(fixture.Entity));
        blocks.Verify(x => x.GetLightLevel(It.IsAny<BlockPos>(), It.IsAny<EnumLightLevelType>()), Times.Never);
        Assert.Empty(native.Resources.Native.Failures);
    }
    #endregion
}
