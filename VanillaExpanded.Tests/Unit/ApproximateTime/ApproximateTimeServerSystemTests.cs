using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks server observation persistence through online, sunlight, and listener lifetimes.</summary>
public sealed class ApproximateTimeServerSystemTests
{
    #region Public API
    /// <summary>Deferred calendar startup preserves saved age, online time accumulates, and disposal removes the listener.</summary>
    [Fact]
    public void OnlineObservationAndListenerLifecycle()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var api = new Mock<ICoreServerAPI>();
        var world = new Mock<IServerWorldAccessor>();
        var events = new Mock<IServerEventAPI>();
        var calendar = new Mock<IGameCalendar>();
        var blocks = new Mock<IBlockAccessor>();
        double hours = 1000;
        IGameCalendar? availableCalendar = null;
        int localSunlight = 0;
        IPlayer[] online = [fixture.Player.Object];
        Action<float>? tick = null;
        api.SetupGet(x => x.World).Returns(world.Object);
        api.SetupGet(x => x.Event).Returns(events.Object);
        world.SetupGet(x => x.Calendar).Returns(() => availableCalendar!);
        world.SetupGet(x => x.BlockAccessor).Returns(blocks.Object);
        world.SetupGet(x => x.AllOnlinePlayers).Returns(() => online);
        calendar.SetupGet(x => x.TotalHours).Returns(() => hours);
        calendar.SetupGet(x => x.TotalDays).Returns(() => hours / 24);
        calendar.Setup(x => x.GetSunPosition(It.IsAny<Vec3d>(), It.IsAny<double>())).Returns(new Vec3f(0, 1, 0));
        blocks.Setup(x => x.GetLightLevel(It.IsAny<BlockPos>(), EnumLightLevelType.OnlySunLight))
            .Returns(() => localSunlight);
        events.Setup(x => x.RegisterGameTickListener(It.IsAny<Action<float>>(), 1000, 0))
            .Callback<Action<float>, int, int>((callback, interval, offset) => tick = callback).Returns(42);
        var system = new ApproximateTimeServerSystem();
        try
        {
            Assert.True(system.ShouldLoad(EnumAppSide.Server));
            Assert.False(system.ShouldLoad(EnumAppSide.Client));
            system.StartServerSide(api.Object);
            Assert.NotNull(tick);
            // Startup and early ticks precede calendar creation and must preserve the persisted counter.
            ApproximateTimeState.Update(fixture.Entity, 4, false);
            tick!(1);
            Assert.Equal(4, ApproximateTimeState.Read(fixture.Entity));
            availableCalendar = calendar.Object;
            tick(1);
            Assert.Equal(4, ApproximateTimeState.Read(fixture.Entity));
            hours = 1002;
            tick(1);
            Assert.Equal(6, ApproximateTimeState.Read(fixture.Entity));
            // Begin the existing sunlight/online lifecycle scenario with a fresh counter and clock.
            ApproximateTimeState.Update(fixture.Entity, 0, true);
            hours = 0;
            tick(1);
            hours = 2;
            tick!(1);
            Assert.Equal(2, ApproximateTimeState.Read(fixture.Entity));
            // Offline ticks advance the server clock but must leave the saved player's age unchanged.
            online = [];
            hours = 12;
            tick(1);
            Assert.Equal(2, ApproximateTimeState.Read(fixture.Entity));
            online = [fixture.Player.Object];
            hours = 13;
            tick(1);
            Assert.Equal(3, ApproximateTimeState.Read(fixture.Entity));
            localSunlight = 1;
            hours = 14;
            tick(1);
            Assert.Equal(0, ApproximateTimeState.Read(fixture.Entity));
            // Residual local skylight at night cannot count as a sunlight observation.
            calendar.Setup(x => x.GetSunPosition(It.IsAny<Vec3d>(), It.IsAny<double>())).Returns(new Vec3f(0, -1, 0));
            hours = 15;
            tick(1);
            Assert.Equal(1, ApproximateTimeState.Read(fixture.Entity));
        }
        finally
        {
            system.Dispose();
        }
        events.Verify(x => x.UnregisterGameTickListener(42), Times.Once);
        hours = 20;
        tick!(1);
        Assert.Equal(1, ApproximateTimeState.Read(fixture.Entity));
        // Restart establishes a new tick baseline without charging elapsed downtime to saved age.
        var restarted = new ApproximateTimeServerSystem();
        try
        {
            restarted.StartServerSide(api.Object);
            tick!(1);
            hours = 21;
            tick!(1);
            Assert.Equal(2, ApproximateTimeState.Read(fixture.Entity));
        }
        finally
        {
            restarted.Dispose();
        }
        events.Verify(x => x.UnregisterGameTickListener(42), Times.Exactly(2));
    }
    #endregion
}
