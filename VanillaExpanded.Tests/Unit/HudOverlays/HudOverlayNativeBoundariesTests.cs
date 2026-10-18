using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks native fixed-HUD boundary sampling against actual installed dialog and composer types.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayNativeBoundariesTests
{
    #region Public API
    /// <summary>Reads caller-supplied hotbar and individual meter rectangles without treating the empty composer area as occupied.</summary>
    [Fact]
    public void SamplesDistinctNativeRectanglesAndCurrentGeometry()
    {
        var fixture = new Fixture();
        var first = fixture.Provider.Resolve();
        Assert.Equal(3, first.Count);
        Assert.Contains(first, item => item.Bounds == fixture.HotbarBounds.Rectangle);
        Assert.Contains(first, item => item.Bounds == fixture.HealthBounds.Rectangle);
        Assert.Contains(first, item => item.Bounds == fixture.HungerBounds.Rectangle);
        Assert.Equal(first.Count, first.Select(item => item.Id).Distinct().Count());
        fixture.HealthBounds.absFixedX += 17;
        var second = fixture.Provider.Resolve();
        Assert.Contains(second, item => item.Bounds == fixture.HealthBounds.Rectangle);
        Assert.DoesNotContain(second, item => item.Bounds == first.Single(old => old.Bounds.Y == fixture.HealthBounds.Rectangle.Y).Bounds);
    }

    /// <summary>Hidden, closed and ambiguous native dialogs do not remain magnetic obstacles.</summary>
    [Theory]
    [InlineData("hidden")]
    [InlineData("closed")]
    [InlineData("disabled")]
    [InlineData("ambiguous")]
    public void UnavailableNativeStateDisappearsAndRecovers(string state)
    {
        var fixture = new Fixture();
        switch (state)
        {
            case "hidden": fixture.Api.SetupGet(api => api.HideGuis).Returns(true); break;
            case "closed": SetOpened(fixture.Hotbar, false); SetOpened(fixture.Statbar, false); break;
            case "disabled": fixture.HotbarComposer.Enabled = fixture.StatbarComposer.Enabled = false; break;
            case "ambiguous": fixture.Dialogs.Add(CreateDialog<HudHotbar>()); fixture.Dialogs.Add(CreateDialog<HudStatbar>()); break;
        }
        Assert.Empty(fixture.Provider.Resolve());
        fixture.Api.SetupGet(api => api.HideGuis).Returns(false);
        SetOpened(fixture.Hotbar, true); SetOpened(fixture.Statbar, true);
        fixture.HotbarComposer.Enabled = fixture.StatbarComposer.Enabled = true;
        fixture.Dialogs.Clear(); fixture.Dialogs.Add(fixture.Hotbar); fixture.Dialogs.Add(fixture.Statbar);
        Assert.Equal(3, fixture.Provider.Resolve().Count);
    }

    /// <summary>Uninitialized, stale or malformed meter geometry cannot become a snapping boundary.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("uninitialized")]
    [InlineData("dirty")]
    [InlineData("empty")]
    [InlineData("nonfinite")]
    public void InvalidSingleMeterLeavesOtherNativeBoundariesAvailable(string state)
    {
        var fixture = new Fixture();
        switch (state)
        {
            case "missing": fixture.Elements.Remove("healthstatbar"); break;
            case "uninitialized": fixture.HealthBounds.Initialized = false; break;
            case "dirty": fixture.HealthBounds.Dirty = true; break;
            case "empty": fixture.HealthBounds.absInnerWidth = 0; break;
            case "nonfinite": fixture.HealthBounds.absFixedX = double.NaN; break;
        }
        var actual = fixture.Provider.Resolve();
        Assert.Equal(2, actual.Count);
        Assert.Contains(actual, item => item.Bounds == fixture.HotbarBounds.Rectangle);
        Assert.Contains(actual, item => item.Bounds == fixture.HungerBounds.Rectangle);
    }

    /// <summary>The native stability gear's enabled hover footprint is read without copying its private render offsets.</summary>
    [Fact]
    public void StabilityGearUsesCurrentNativeHitAreaOnlyWhilePresent()
    {
        var fixture = new Fixture();
        var hover = (GuiElementHoverText)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementHoverText));
        var bounds = new Bounds(350, 410, 70, 60); hover.Bounds = bounds;
        var elements = new Dictionary<string, GuiElement> { ["tempStabHoverText"] = hover };
        typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.HotbarComposer, elements);
        Assert.Contains(fixture.Provider.Resolve(), item => item.Bounds == bounds.Rectangle);
        bounds.absFixedX += 11;
        Assert.Contains(fixture.Provider.Resolve(), item => item.Bounds == bounds.Rectangle);
        elements.Clear();
        Assert.Equal(3, fixture.Provider.Resolve().Count);
    }
    /// <summary>Conditional oxygen meters are not mistaken for a permanent native HUD surface.</summary>
    [Fact]
    public void ConditionalMeterDoesNotBecomeFixedBoundary()
    {
        var fixture = new Fixture();
        var oxygen = (GuiElementStatbar)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementStatbar));
        oxygen.Bounds = new Bounds(300, 430, 120, 16);
        fixture.Elements["oxygenstatbar"] = oxygen;
        Assert.DoesNotContain(fixture.Provider.Resolve(), item => item.Bounds == new RectangleF(300, 430, 120, 16));
    }
    /// <summary>Native spectator suppression applies even while the cursor is free and old composers still exist.</summary>
    [Fact]
    public void SpectatorSuppressesNativeBoundaries()
    {
        var fixture = new Fixture();
        var world = new Mock<IClientWorldAccessor>();
        var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        var data = new Mock<IWorldPlayerData>();
        world.SetupGet(value => value.Player).Returns(player.Object);
        player.SetupGet(value => value.WorldData).Returns(data.Object);
        fixture.Api.SetupGet(value => value.World).Returns(world.Object);
        data.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Spectator);
        Assert.Empty(fixture.Provider.Resolve());
        data.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Survival);
        Assert.Equal(3, fixture.Provider.Resolve().Count);
    }
    #endregion

    #region Private
    /// <summary>Creates only the native dialog state consumed by the provider.</summary>
    private static T CreateDialog<T>() where T : GuiDialog
    {
        var dialog = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        dialog.Composers = new GuiDialog.DlgComposers(dialog);
        SetOpened(dialog, true);
        return dialog;
    }

    /// <summary>Sets native open state without invoking client lifecycle services.</summary>
    private static void SetOpened(GuiDialog dialog, bool opened) =>
        typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, opened);

    /// <summary>Builds an inert native composer with public element lookup and borrowed bounds.</summary>
    private static GuiComposer CreateComposer(ElementBounds bounds, Dictionary<string, GuiElement>? elements = null)
    {
        var composer = (GuiComposer)RuntimeHelpers.GetUninitializedObject(typeof(GuiComposer));
        composer.Enabled = true;
        typeof(GuiComposer).GetField("bounds", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(composer, bounds);
        typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(composer, elements ?? new Dictionary<string, GuiElement>());
        typeof(GuiComposer).GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(composer, new Dictionary<string, GuiElement>());
        return composer;
    }

    /// <summary>Owns synthetic geometry while retaining the real native lookup surfaces.</summary>
    private sealed class Fixture
    {
        public readonly Mock<ICoreClientAPI> Api = new(MockBehavior.Strict);
        public readonly List<GuiDialog> Dialogs = new();
        public readonly HudHotbar Hotbar = CreateDialog<HudHotbar>();
        public readonly HudStatbar Statbar = CreateDialog<HudStatbar>();
        public readonly Bounds HotbarBounds = new(180, 500, 400, 50);
        public readonly Bounds HealthBounds = new(180, 460, 140, 18);
        public readonly Bounds HungerBounds = new(420, 465, 160, 18);
        public readonly Dictionary<string, GuiElement> Elements = new();
        public readonly GuiComposer HotbarComposer;
        public readonly GuiComposer StatbarComposer;
        public readonly HudOverlayNativeBoundaries Provider;
        #region Public API
        /// <summary>Registers supported native dialogs with independently positioned meter elements.</summary>
        public Fixture()
        {
            var health = (GuiElementStatbar)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementStatbar)); health.Bounds = HealthBounds;
            var hunger = (GuiElementStatbar)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementStatbar)); hunger.Bounds = HungerBounds;
            Elements["healthstatbar"] = health; Elements["saturationstatbar"] = hunger;
            HotbarComposer = CreateComposer(HotbarBounds);
            StatbarComposer = CreateComposer(new Bounds(0, 0, 800, 600), Elements);
            Hotbar.Composers["hotbar"] = HotbarComposer; Statbar.Composers["statbar"] = StatbarComposer;
            Dialogs.Add(Hotbar); Dialogs.Add(Statbar);
            var gui = new Mock<IGuiAPI>(MockBehavior.Strict);
            gui.SetupGet(value => value.LoadedGuis).Returns(Dialogs);
            Api.SetupGet(value => value.Gui).Returns(gui.Object);
            Api.SetupGet(value => value.HideGuis).Returns(false);
            Api.SetupGet(value => value.World).Returns((IClientWorldAccessor)null!);
            Provider = new HudOverlayNativeBoundaries(Api.Object);
        }
        #endregion
    }

    /// <summary>Supplies independent native physical rectangles without initializing a window.</summary>
    private sealed class Bounds : ElementBounds
    {
        public RectangleF Rectangle => new((float)renderX, (float)renderY, (float)OuterWidth, (float)OuterHeight);
        public bool Dirty { set => requiresrelculation = value; }
        #region Public API
        /// <summary>Initializes caller-selected measured geometry.</summary>
        public Bounds(double x, double y, double width, double height)
        {
            absFixedX = x; absFixedY = y; absInnerWidth = width; absInnerHeight = height;
            Initialized = true; requiresrelculation = false;
        }
        /// <summary>Returns the supplied physical horizontal origin.</summary>
        public override double renderX => absFixedX;
        /// <summary>Returns the supplied physical vertical origin.</summary>
        public override double renderY => absFixedY;
        #endregion
    }
    #endregion
}
