using System.Reflection;
using Moq;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

[CollectionDefinition("ToolModeRadialMenuConfig", DisableParallelization = true)]
public sealed class ToolModeRadialMenuConfigCollection;

[Collection("ToolModeRadialMenuConfig")]
[Trait("Category", "Unit")]
public sealed class ToolModeRadialMenuConfigTests : IDisposable
{
    private readonly float originalRingSize = VanillaExpandedModSystem.Config.ToolModeRingSize;
    private readonly float originalCenterSize = VanillaExpandedModSystem.Config.ToolModeCenterSize;
    private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableToolModeRadialMenu;

    /// <summary>Shared geometry settings bound finite inputs and fall back safely for non-finite values.</summary>
    [Theory]
    [InlineData(1f, 1f, 1d, 1d)]
    [InlineData(0.5f, 2f, 0.5d, 2d)]
    [InlineData(2.5f, 0.15f, 2.5d, 0.15d)]
    [InlineData(99f, -1f, 2.5d, 0.15d)]
    [InlineData(float.NaN, float.PositiveInfinity, 1d, 1d)]
    public void GeometrySettingsClampAndNormalizeInputs(float centerSetting, float ringSetting,
        double centerScale, double ringScale)
    {
        VanillaExpandedModSystem.Config.ToolModeCenterSize = centerSetting;
        VanillaExpandedModSystem.Config.ToolModeRingSize = ringSetting;
        Assert.Equal(centerScale, ToolModeMenuGeometry.GetCenterRadius(1d), 6);
        Assert.Equal(ringScale,
            (ToolModeMenuGeometry.GetOuterRadius(0.5) - 0.5) / ToolModeMenuGeometry.DefaultRingThickness, 6);
    }

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
        VanillaExpandedModSystem.Config.ToolModeRingSize = originalRingSize;
        VanillaExpandedModSystem.Config.ToolModeCenterSize = originalCenterSize;
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
