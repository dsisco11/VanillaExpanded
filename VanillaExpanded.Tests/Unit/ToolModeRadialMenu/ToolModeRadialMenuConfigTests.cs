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

    /// <summary>Every tool-mode layout preserves ring thickness and gaps when its center is resized.</summary>
    [Theory]
    [InlineData(1f, 1f, 1d, 1d)]
    [InlineData(0.5f, 2f, 0.5d, 2d)]
    [InlineData(2.5f, 0.15f, 2.5d, 0.15d)]
    [InlineData(99f, -1f, 2.5d, 0.15d)]
    [InlineData(float.NaN, float.PositiveInfinity, 1d, 1d)]
    public void GeometrySettingsKeepNestedRingsSeparated(float centerSetting, float ringSetting,
        double centerScale, double ringScale)
    {
        VanillaExpandedModSystem.Config.ToolModeCenterSize = centerSetting;
        VanillaExpandedModSystem.Config.ToolModeRingSize = ringSetting;
        foreach (IToolModeMenuLayoutStrategy strategy in new IToolModeMenuLayoutStrategy[]
            { GenericToolModeMenuLayoutStrategy.Instance, SmithingHammerToolModeMenuLayoutStrategy.Instance,
              BoatRollerToolModeMenuLayoutStrategy.Instance, ChiselToolModeMenuLayoutStrategy.Instance })
        {
            string[] codes = strategy switch
            {
                SmithingHammerToolModeMenuLayoutStrategy => ["upsetup", "action"],
                BoatRollerToolModeMenuLayoutStrategy => ["north", "east", "south", "west"],
                ChiselToolModeMenuLayoutStrategy => ["1size", "2size", "4size", "8size", "addmat", "material"],
                _ => ["first", "second"]
            };
            SkillItem[] modes = codes.Select(code => new SkillItem
                { Code = new AssetLocation(code), Name = code, Linebreak = code == "material" }).ToArray();
            Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 0, "Current", strategy, out ToolModeMenuContent? content));
            RadialMenuLayout ring = content!.Layout;
            double thickness = strategy is ChiselToolModeMenuLayoutStrategy ? 0.24 : 0.50;
            // Both chisel rings share the thickness control; other layouts have one option ring.
            while (!ring.IsSingleOption)
            {
                Assert.Equal(thickness * ringScale, ring.OuterRadius - ring.InnerRadius, 6);
                Assert.Equal(0.02, ring.InnerRadius - ring.InnerMenu!.OuterRadius, 6);
                (double x, double y) = ring.GetWedgeCenter(0, 0, 0, 600, (ring.InnerRadius + ring.OuterRadius) / 2d);
                Assert.Equal(ring.EntryIds[0], content.Layout.HitTest(x, y, 0, 0, 600));
                ring = ring.InnerMenu;
            }
            Assert.Equal(0.48 * centerScale, ring.OuterRadius, 6);
        }
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
