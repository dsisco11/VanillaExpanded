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
    /// <summary>Cover and night prevent calibration, daylight restores it, and drawing performs no sampling.</summary>
    [Fact]
    public void SunlightSamplingAndLifecycle()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        fixture.Entity.EntityId = 2;
        var calendar = new Mock<IClientGameCalendar>();
        double hours = 8;
        var sun = new Vec3f(0, 1, 0);
        int roof = 100;
        calendar.SetupGet(x => x.TotalDays).Returns(() => hours / 24);
        calendar.SetupGet(x => x.TotalHours).Returns(() => hours);
        calendar.SetupGet(x => x.HourOfDay).Returns(() => (float)(hours % 24));
        calendar.SetupGet(x => x.HoursPerDay).Returns(24);
        calendar.SetupGet(x => x.SunPositionNormalized).Returns(sun);
        calendar.Setup(x => x.GetSunPosition(It.IsAny<Vec3d>(), It.IsAny<double>()))
            .Returns((Vec3d position, double day) => new Vec3f(0, (float)Math.Cos((day % 1 - .5) * 2 * Math.PI), 0));
        fixture.World.SetupGet(x => x.Calendar).Returns(calendar.Object);
        var blocks = new Mock<IBlockAccessor>();
        blocks.Setup(x => x.GetRainMapHeightAt(It.IsAny<BlockPos>())).Returns(() => roof);
        fixture.World.SetupGet(x => x.BlockAccessor).Returns(blocks.Object);
        var renderer = new Mock<IRenderAPI>();
        fixture.Api.SetupGet(x => x.Render).Returns(renderer.Object);
        int uploads = 0;

        using var overlay = new ApproximateTimeOverlay(new ApproximateTimePresentation(context =>
        {
            uploads++;
            return Enumerable.Range(0, 7).Select(index => new LoadedTexture(context.Api)
                { TextureId = index + 1, Width = 96, Height = 96, IgnoreUndisposed = true }).ToArray();
        }));
        overlay.BeginSession(fixture.Api.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", 0);
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(new SizeF(24, 24), overlay.Measure());
        roof = -1;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        roof = 100; hours = 18;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        int previousUploads = uploads;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(previousUploads, uploads);
        blocks.Invocations.Clear(); calendar.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 40, 14).WithEmptyParent(), new RectangleF(0, 0, 40, 14), 0);
        Assert.Empty(blocks.Invocations); Assert.Empty(calendar.Invocations);
        Assert.Equal(6, (int)Assert.Single(renderer.Invocations).Arguments[0]);
        roof = -1; sun.Y = -1; hours = 19;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        sun.Y = 1;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, uploads);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.SetEnabled(true); roof = -1; sun.Y = -1; hours = 2;
        overlay.Refresh(); overlay.Prepare(preparation);
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(), new RectangleF(0, 0, 24, 24), 0);
        Assert.Equal(7, (int)Assert.Single(renderer.Invocations).Arguments[0]);
        overlay.SetEnabled(false);
        overlay.SetEnabled(true); roof = 100;
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(new SizeF(24, 24), overlay.Measure());
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.EndSession(); Assert.False(overlay.IsApplicable());
    }
    #endregion
}
