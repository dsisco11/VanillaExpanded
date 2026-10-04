using System.Reflection;
using Moq;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

[CollectionDefinition("ToolModeRadialMenuConfig", DisableParallelization = true)]
public sealed class ToolModeRadialMenuConfigCollection;

[Collection("ToolModeRadialMenuConfig")]
[Trait("Category", "Unit")]
public sealed class ToolModeRadialMenuConfigTests : IDisposable
{
    private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableToolModeRadialMenu;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryOpen_WhenDisabled_FallsThroughWithoutAccessingMenuOrPlayer(bool isOpen)
    {
        VanillaExpandedModSystem.Config.EnableToolModeRadialMenu = false;
        var menu = new Mock<IRadialMenu>(MockBehavior.Strict);
        menu.SetupGet(value => value.IsOpen).Returns(isOpen);
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        ToolModeRadialMenuSystem system = CreateSystem(api.Object, menu.Object);

        Assert.False(system.TryOpen());

        menu.VerifyNoOtherCalls();
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public void TryOpen_WhenEnabled_ConsumesToggleAndClosesOpenMenu()
    {
        VanillaExpandedModSystem.Config.EnableToolModeRadialMenu = true;
        var menu = new Mock<IRadialMenu>(MockBehavior.Strict);
        menu.SetupGet(value => value.IsOpen).Returns(true);
        menu.Setup(value => value.Cancel());
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        ToolModeRadialMenuSystem system = CreateSystem(api.Object, menu.Object);

        Assert.True(system.TryOpen());

        menu.Verify(value => value.Cancel(), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public void ConfigReload_WhenDisabled_CancelsMenuAndBlocksToggles()
    {
        var menu = new Mock<IRadialMenu>(MockBehavior.Strict);
        menu.SetupGet(value => value.IsOpen).Returns(true);
        menu.Setup(value => value.Cancel());
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        ToolModeRadialMenuSystem system = CreateSystem(api.Object, menu.Object);

        VanillaExpandedModSystem.Config.EnableToolModeRadialMenu = false;
        system.OnConfigReloaded(api.Object);
        Assert.False(system.TryOpen());
        menu.Verify(value => value.Cancel(), Times.Once);

        api.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        VanillaExpandedModSystem.Config.EnableToolModeRadialMenu = originalEnabled;
    }

    private static ToolModeRadialMenuSystem CreateSystem(ICoreClientAPI api, IRadialMenu menu)
    {
        var system = new ToolModeRadialMenuSystem();
        typeof(ToolModeRadialMenuSystem).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(system, api);
        typeof(ToolModeRadialMenuSystem).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(system, menu);
        return system;
    }
}
