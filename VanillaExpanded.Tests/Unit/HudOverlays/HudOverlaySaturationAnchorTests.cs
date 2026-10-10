using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises the installed native statbar adapter without constructing graphics resources.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlaySaturationAnchorTests
{
    #region Public API
    /// <summary>Reads the meter element including parent padding, margins and render offsets.</summary>
    [Fact]
    public void ResolvesNativeMeterRectangle()
    {
        var fixture = new Fixture();
        Assert.Equal(new RectangleF(131, 158, 106, 48), fixture.Anchor.Resolve());
        fixture.Bounds.renderOffsetX += 9;
        fixture.Bounds.absInnerWidth += 20;
        Assert.Equal(new RectangleF(140, 158, 126, 48), fixture.Anchor.Resolve());
    }

    /// <summary>Unavailable native states hide quietly and recover using the current composer.</summary>
    [Theory]
    [InlineData("hidden")]
    [InlineData("missing")]
    [InlineData("closed")]
    [InlineData("composer")]
    [InlineData("disabled")]
    [InlineData("bounds")]
    [InlineData("meter")]
    [InlineData("uninitialized")]
    [InlineData("dirty")]
    [InlineData("ambiguous")]
    public void UnavailableTargetsRecover(string state)
    {
        var fixture = new Fixture();
        switch (state)
        {
            case "hidden": fixture.Api.SetupGet(api => api.HideGuis).Returns(true); break;
            case "missing": fixture.Dialogs.Clear(); break;
            case "closed": SetOpened(fixture.Statbar, false); break;
            case "composer": fixture.Statbar.Composers.Remove("statbar"); break;
            case "disabled": fixture.Composer.Enabled = false; break;
            case "bounds": fixture.Meter.Bounds = null!; break;
            case "meter": fixture.Elements.Clear(); break;
            case "uninitialized": fixture.Bounds.Initialized = false; break;
            case "dirty": fixture.Bounds.Dirty = true; break;
            case "ambiguous": fixture.Dialogs.Add(CreateStatbar(false)); break;
        }
        Assert.Null(fixture.Anchor.Resolve());
        // Restore public native state and a fresh composer binding; the adapter must retain no stale authority.
        fixture.Api.SetupGet(api => api.HideGuis).Returns(false);
        fixture.Dialogs.Clear(); fixture.Dialogs.Add(fixture.Statbar);
        SetOpened(fixture.Statbar, true);
        fixture.Composer.Enabled = true;
        fixture.Meter.Bounds = fixture.Bounds;
        fixture.Elements["saturationstatbar"] = fixture.Meter;
        fixture.Bounds.Initialized = true;
        fixture.Bounds.Dirty = false;
        fixture.Statbar.Composers["statbar"] = fixture.Composer;
        Assert.Equal(new RectangleF(131, 158, 106, 48), fixture.Anchor.Resolve());
    }

    /// <summary>Mirrors the native statbar spectator suppression and resumes in survival.</summary>
    [Fact]
    public void SpectatorMeterIsUnavailable()
    {
        var fixture = new Fixture();
        var world = new Mock<IClientWorldAccessor>();
        var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        var data = new Mock<IWorldPlayerData>();
        world.SetupGet(value => value.Player).Returns(player.Object);
        player.SetupGet(value => value.WorldData).Returns(data.Object);
        fixture.Api.SetupGet(value => value.World).Returns(world.Object);
        data.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Spectator);
        Assert.Null(fixture.Anchor.Resolve());
        data.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Survival);
        Assert.NotNull(fixture.Anchor.Resolve());
    }

    /// <summary>Invalid or empty native rectangles never become placement inputs.</summary>
    [Theory]
    [InlineData(double.NaN, 40)]
    [InlineData(double.PositiveInfinity, 40)]
    [InlineData(0, 40)]
    [InlineData(-10, 40)]
    [InlineData(40, 0)]
    public void InvalidNativeDimensionsHide(double width, double height)
    {
        var fixture = new Fixture();
        fixture.Bounds.absPaddingX = fixture.Bounds.absPaddingY = 0;
        fixture.Bounds.absInnerWidth = width;
        fixture.Bounds.absInnerHeight = height;
        Assert.Null(fixture.Anchor.Resolve());
    }
    #endregion

    #region Private
    /// <summary>Creates only the native state read by the adapter, avoiding engine constructors.</summary>
    private static HudStatbar CreateStatbar(bool opened)
    {
        var statbar = (HudStatbar)RuntimeHelpers.GetUninitializedObject(typeof(HudStatbar));
        statbar.Composers = new GuiDialog.DlgComposers(statbar);
        SetOpened(statbar, opened);
        return statbar;
    }

    /// <summary>Sets the installed protected open-state field exclusively in a headless fixture.</summary>
    private static void SetOpened(HudStatbar statbar, bool opened) =>
        typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(statbar, opened);

    /// <summary>Assigns borrowed bounds without allocating native composer graphics resources.</summary>
    private static void SetBounds(GuiComposer composer, ElementBounds? bounds) =>
        typeof(GuiComposer).GetField("bounds", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(composer, bounds);

    /// <summary>Owns inert native objects and strict API mocks for the adapter boundary.</summary>
    private sealed class Fixture
    {
        public Mock<ICoreClientAPI> Api { get; } = new(MockBehavior.Strict);
        public List<GuiDialog> Dialogs { get; } = new();
        public HudStatbar Statbar { get; } = CreateStatbar(true);
        public GuiComposer Composer { get; } = (GuiComposer)RuntimeHelpers.GetUninitializedObject(typeof(GuiComposer));
        public GuiElementStatbar Meter { get; } = (GuiElementStatbar)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementStatbar));
        public NativeBounds Bounds { get; }
        public Dictionary<string, GuiElement> Elements { get; } = new();
        public HudOverlaySaturationAnchor Anchor { get; }

        #region Public API
        /// <summary>Builds native hierarchy whose inner dimensions differ from its full rendered rectangle.</summary>
        public Fixture()
        {
            var parent = new Origin { absPaddingX = 2, absPaddingY = 3 };
            Bounds = new NativeBounds
            {
                ParentBounds = parent, Initialized = true, Dirty = false,
                absFixedX = 100, absFixedY = 120, absMarginX = 5, absMarginY = 6,
                absOffsetX = 4, absOffsetY = 5, renderOffsetX = 13, renderOffsetY = 13,
                absInnerWidth = 100, absInnerHeight = 40, absPaddingX = 3, absPaddingY = 4
            };
            Composer.Enabled = true;
            Meter.Bounds = Bounds;
            Elements["saturationstatbar"] = Meter;
            SetBounds(Composer, new ElementBounds());
            typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Composer, Elements);
            typeof(GuiComposer).GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Composer, new Dictionary<string, GuiElement>());
            Statbar.Composers["statbar"] = Composer;
            Dialogs.Add(Statbar);
            var gui = new Mock<IGuiAPI>(MockBehavior.Strict);
            gui.SetupGet(api => api.LoadedGuis).Returns(Dialogs);
            Api.SetupGet(api => api.Gui).Returns(gui.Object);
            Api.SetupGet(api => api.HideGuis).Returns(false);
            Api.SetupGet(api => api.World).Returns((IClientWorldAccessor)null!);
            Anchor = new HudOverlaySaturationAnchor(Api.Object);
        }
        #endregion
    }

    /// <summary>Exposes native dirtiness for isolated adapter state transitions.</summary>
    private sealed class NativeBounds : ElementBounds
    {
        #region Public API
        /// <summary>Sets the native recalculation flag without invoking platform services.</summary>
        public bool Dirty { set => requiresrelculation = value; }
        #endregion
    }

    /// <summary>Supplies native parent pixel render offsets without platform initialization.</summary>
    private sealed class Origin : ElementBounds
    {
        #region Public API
        /// <summary>Returns the parent pixel X origin.</summary>
        public override double renderX => 7;
        /// <summary>Returns the parent pixel Y origin.</summary>
        public override double renderY => 11;
        #endregion
    }
    #endregion
}
