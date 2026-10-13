using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Registration;

using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks native sunlight sampling and cached presentation without graphics calls.</summary>
[Collection("HudOverlayGeometry")]
public sealed class ApproximateTimeOverlayTests
{
    #region Public API
    /// <summary>The scenery rotates a full turn per day with noon upright and midnight inverted.</summary>
    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(0.75, Math.PI / 2)]
    [InlineData(0, -Math.PI)]
    [InlineData(0.25, -Math.PI / 2)]
    public void DialRotation(double day, double expected)
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        var dial = new Mock<IApproximateTimeDialRenderer>();
        using var presentation = new ApproximateTimePresentation(context =>
            Enumerable.Range(0, 2).Select(index => new LoadedTexture(context.Api)
                { TextureId = index + 1, IgnoreUndisposed = true }).ToArray(), _ => dial.Object);
        presentation.SetContent(ApproximateTimePeriod.Noon, day);
        presentation.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", 0));
        presentation.Draw(Mock.Of<IRenderAPI>(), ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent());
        dial.Verify(x => x.Draw(It.IsAny<IRenderAPI>(), 2, It.IsAny<RectangleF>(), 1,
            It.Is<float>(angle => Math.Abs(angle - expected) < .00001)), Times.Once);
        presentation.Reset();
        dial.Verify(x => x.Dispose(), Times.Once);
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
        int uploads = 0;

        using var overlay = new ApproximateTimeOverlay(new ApproximateTimePresentation(context =>
        {
            uploads++;
            return Enumerable.Range(0, 2).Select(index => new LoadedTexture(context.Api)
                { TextureId = index + 1, Width = 96, Height = 96, IgnoreUndisposed = true }).ToArray();
        }, _ => Mock.Of<IApproximateTimeDialRenderer>()));
        overlay.BeginSession(fixture.Api.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", 0);
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(new SizeF(24, 24), overlay.Measure());
        sunlight = 10;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        sunlight = 0; hours = 18;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        int previousUploads = uploads;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(previousUploads, uploads);
        blocks.Invocations.Clear(); calendar.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 40, 14).WithEmptyParent(), new RectangleF(0, 0, 40, 14), 0);
        Assert.Empty(blocks.Invocations); Assert.Empty(calendar.Invocations);
        var iconCall = Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50);
        Assert.Equal(1, (int)Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50).Arguments[0]);
        sunlight = 10; sun.Y = -1; hours = 19;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        sun.Y = 1;
        blocks.Setup(x => x.GetRainMapHeightAt(It.IsAny<BlockPos>())).Returns(100);
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        blocks.Verify(x => x.GetRainMapHeightAt(It.IsAny<BlockPos>()), Times.Never);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.SetEnabled(true); sunlight = 10; sun.Y = -1; hours = 2;
        overlay.Refresh(); overlay.Prepare(preparation);
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(), new RectangleF(0, 0, 24, 24), 0);
        Assert.Equal(1, (int)Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50).Arguments[0]);
        overlay.SetEnabled(false);
        overlay.SetEnabled(true); sunlight = 0;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(new SizeF(24, 24), overlay.Measure());
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(), new RectangleF(0, 0, 24, 24), 0);
        Assert.Equal(1, (int)Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50).Arguments[0]);
        hours = double.NaN;
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(), new RectangleF(0, 0, 24, 24), 0);
        Assert.Equal(1, (int)Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50).Arguments[0]);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.EndSession(); Assert.False(overlay.IsApplicable());
        Assert.Equal(4, ApproximateTimeState.Read(fixture.Entity));
        blocks.Verify(x => x.GetLightLevel(It.IsAny<BlockPos>(), It.IsAny<EnumLightLevelType>()), Times.Never);
    }
    #endregion
}
