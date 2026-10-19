using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises real host targeting and popup input ownership without graphics allocation.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayMenuInputTests : IDisposable
{
    private readonly float previousScale = Vintagestory.API.Config.RuntimeEnv.GUIScale;
    private readonly string previousLocale = Vintagestory.API.Config.Lang.CurrentLocale;
    private readonly Vintagestory.API.Config.ITranslationService? previousLanguage;
    #region Public API
    /// <summary>Supplies native group-caption localization for host compositions.</summary>
    public HudOverlayMenuInputTests()
    {
        Vintagestory.API.Config.RuntimeEnv.GUIScale = 1;
        Vintagestory.API.Config.Lang.AvailableLanguages.TryGetValue("en", out previousLanguage);
        Vintagestory.API.Config.Lang.AvailableLanguages["en"] = Mock.Of<Vintagestory.API.Config.ITranslationService>();
        typeof(Vintagestory.API.Config.Lang).GetProperty("CurrentLocale")!.SetValue(null, "en");
    }
    /// <summary>Restores native language globals.</summary>
    public void Dispose()
    {
        Vintagestory.API.Config.RuntimeEnv.GUIScale = previousScale;
        typeof(Vintagestory.API.Config.Lang).GetProperty("CurrentLocale")!.SetValue(null, previousLocale);
        if (previousLanguage == null) Vintagestory.API.Config.Lang.AvailableLanguages.Remove("en");
        else Vintagestory.API.Config.Lang.AvailableLanguages["en"] = previousLanguage;
    }
    /// <summary>The opening press never activates native rows and Escape belongs to the menu.</summary>
    [Fact]
    public void HostOpeningAndEscapeOwnOnlyMenuInput()
    {
        using var f = new Fixture();
        var press = f.Press(); f.Host.OnMouseDown(press);
        Assert.True(press.Handled); Assert.True(f.Menu.IsOpened); Assert.Equal(0, f.Popup.Presses);
        Assert.True(f.Host.Focusable); Assert.True(f.Host.CaptureAllInputs());
        var openingRelease = f.Press(); f.Host.OnMouseUp(openingRelease); Assert.True(openingRelease.Handled);
        var key = new KeyEvent { KeyCode = (int)GlKeys.Escape }; f.Host.OnKeyDown(key);
        Assert.True(key.Handled); Assert.False(f.Menu.IsOpened); Assert.True(f.Host.Focusable);
        var keyUp = new KeyEvent { KeyCode = key.KeyCode }; f.Host.OnKeyUp(keyUp);
        Assert.True(keyUp.Handled); Assert.False(f.Host.Focusable);
        Assert.Equal(1, f.Popup.Disposals);
    }

    /// <summary>Focused popup rendering stays outside the passive artwork transform and cannot raise its artwork slot.</summary>
    [Fact]
    public void FocusedPopupUsesSeparateNativeDialogPass()
    {
        using var f = new Fixture();
        var render = new Mock<IRenderAPI>();
        f.Layout.Api.SetupGet(value => value.Render).Returns(render.Object);
        var artwork = (GuiDialog)typeof(HudOverlayHost).GetField("artworkDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Host)!;
        double passiveOrder = artwork.DrawOrder;
        f.Host.OnMouseDown(f.Press());
        Assert.True(f.Host.Focusable);
        Assert.False(artwork.Focusable);
        Assert.True(artwork.DrawOrder < f.Host.DrawOrder);
        Assert.Equal(passiveOrder, artwork.DrawOrder);
        int draws = 0;
        f.Popup.Render = () =>
        {
            draws++;
            render.Verify(value => value.GlTranslate(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
            render.Verify(value => value.GlTranslate(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
        };
        f.Host.OnRenderGUI(.1f);
        Assert.Equal(1, draws);
        f.Feature.Verify(value => value.Draw(It.IsAny<IRenderAPI>(), It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), It.IsAny<float>()), Times.Never);
    }
    /// <summary>Reset targets only the selected indicator and retires stale menu ownership after dispatch.</summary>
    [Fact]
    public void IndividualResetPreservesSiblingAndFeatureSettings()
    {
        using var f = new Fixture(disabled: true);
        Assert.True(f.Containers.SetMovable("test", true, new PointF(120, 80)));
        var owner = f.Containers.Owner("test:a")!;
        f.Host.OnMouseDown(f.Press());
        Assert.Contains(f.Rows, value => value.Code == "reset" && value.Kind == HudMenuEntryKind.Action);
        f.Popup.Press = () => f.Select!("reset", true);
        var click = new MouseEvent(1, 1, EnumMouseButton.Left, 0); f.Host.OnMouseDown(click);
        Assert.False(f.Menu.IsOpened); Assert.Equal(1, f.Popup.Disposals);
        Assert.False(f.EnabledState); Assert.Equal(0, f.SettingsWrites);
        var selected = f.Containers.Owner("test:a")!;
        Assert.Null(selected.X); Assert.False(selected.Movable);
        Assert.Equal(owner with { Tree = owner.Tree.Remove("test:a")! }, f.Containers.Owner("test:b"));
        f.Feature.VerifyNoOtherCalls();
    }

    /// <summary>Handled presses, unavailable content and foreground modal ownership cannot open menus.</summary>
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void IneligiblePressRetainsExistingOwnership(bool handled, bool drawable, bool modal)
    {
        using var f = new Fixture(); f.Drawable = drawable;
        if (modal) f.Opened.Add(new Modal(f.Layout.Api.Object));
        var press = f.Press(); press.Handled = handled; f.Host.OnMouseDown(press);
        Assert.False(f.Menu.IsOpened); Assert.Equal(handled, press.Handled);
    }

    /// <summary>Native focus loss closes resources and ordinary outside right clicks pass through.</summary>
    [Fact]
    public void FocusLossAndOutsideTargetDoNotRetainCapture()
    {
        using var f = new Fixture(); f.Host.OnMouseDown(f.Press()); f.Host.UnFocus();
        Assert.False(f.Menu.IsOpened); Assert.Equal(1, f.Popup.Disposals);
        var outside = new MouseEvent(-1, -1, EnumMouseButton.Right, 0); f.Host.OnMouseDown(outside);
        Assert.False(outside.Handled); Assert.False(f.Menu.IsOpened);
    }

    /// <summary>Changing permission through a member menu affects all group members at the displayed origin.</summary>
    [Fact]
    public void MemberLockPreservesWholeGroupOrigin()
    {
        using var f = new Fixture(); f.Host.OnMouseDown(f.Press());
        var origin = new PointF((float)f.Group.Root.renderX, (float)f.Group.Root.renderY);
        f.Popup.Press = () => f.Select!("lock", false);
        f.Host.OnMouseDown(f.Press());
        Assert.False(f.Containers.Get("test", "test:a").Locked);
        Assert.False(f.Containers.Get("test", "test:b").Locked);
        Assert.Equal(origin, f.Containers.Get("test").Position); Assert.False(f.Menu.IsOpened);
    }

    /// <summary>Selection closure retains matching button release ownership instead of leaking it into gameplay.</summary>
    [Fact]
    public void SelectionClosureSwallowsPairedRelease()
    {
        using var f = new Fixture(); f.Host.OnMouseDown(f.Press()); f.Host.OnMouseUp(f.Press());
        f.Popup.Press = () => f.Select!("lock", false);
        var point = f.Press(); var click = new MouseEvent(point.X, point.Y, EnumMouseButton.Left, 0);
        f.Host.OnMouseDown(click); Assert.True(click.Handled); Assert.False(f.Menu.IsOpened);
        Assert.True(f.Host.CaptureAllInputs());
        var release = new MouseEvent(point.X, point.Y, EnumMouseButton.Left, 0); f.Host.OnMouseUp(release);
        Assert.True(release.Handled); Assert.False(f.Host.CaptureAllInputs());
    }

    /// <summary>A held modifier's protected release must not prevent another right-click menu in the same edit session.</summary>
    [Theory]
    [InlineData(GlKeys.AltLeft)]
    [InlineData(GlKeys.ShiftLeft)]
    public void PendingModifierReleaseAllowsReopeningAndRetainsOriginalFocus(GlKeys modifier)
    {
        using var f = new Fixture();
        using var window = new Inventory(f.Layout.Api.Object, ElementBounds.Fixed(200, 200, 100, 100).WithParent(f.Layout.Gui.Object.WindowBounds));
        f.Opened.Add(window); window.Focus();
        f.Layout.Gui.Setup(api => api.RequestFocus(It.IsAny<GuiDialog>())).Callback<GuiDialog>(dialog =>
        { if (ReferenceEquals(dialog, f.Host)) window.UnFocus(); dialog.Focus(); });
        f.Host.OnMouseDown(f.Press()); f.Host.OnMouseUp(f.Press());
        f.Host.OnKeyDown(new KeyEvent { KeyCode = (int)modifier });
        f.Popup.Press = () => f.Select!("lock", false);
        var point = f.Press();
        var selection = new MouseEvent(point.X, point.Y, EnumMouseButton.Left, 0); f.Host.OnMouseDown(selection);
        var selectionRelease = new MouseEvent(point.X, point.Y, EnumMouseButton.Left, 0); f.Host.OnMouseUp(selectionRelease);
        Assert.True(selection.Handled); Assert.True(selectionRelease.Handled);
        Assert.False(f.Menu.IsOpened); Assert.True(f.Host.CaptureAllInputs()); Assert.False(window.Focused);
        f.Popup.Press = null;
        var reopen = f.Press(); f.Host.OnMouseDown(reopen);
        Assert.True(reopen.Handled); Assert.True(f.Menu.IsOpened);
        var reopenRelease = f.Press(); f.Host.OnMouseUp(reopenRelease); Assert.True(reopenRelease.Handled);
        f.Host.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Escape });
        var escapeRelease = new KeyEvent { KeyCode = (int)GlKeys.Escape }; f.Host.OnKeyUp(escapeRelease);
        Assert.True(escapeRelease.Handled); Assert.False(f.Menu.IsOpened);
        Assert.False(window.Focused); Assert.True(f.Host.CaptureAllInputs());
        var modifierRelease = new KeyEvent { KeyCode = (int)modifier }; f.Host.OnKeyUp(modifierRelease);
        Assert.True(modifierRelease.Handled); Assert.True(window.Focused); Assert.False(f.Host.CaptureAllInputs());
        f.Layout.Gui.Verify(api => api.RequestFocus(window), Times.Once);
        Assert.Null(f.Containers.ActiveContainer);
    }

    /// <summary>Disabled content remains a menu target through host-owned editable geometry without running the feature.</summary>
    [Fact]
    public void DisabledProxyOpensAndEnablesThroughOwnedSetting()
    {
        using var f = new Fixture(disabled: true);
        Assert.False(f.Group.Members[0].Registration.IsEnabled());
        f.Host.OnMouseDown(f.Press()); Assert.True(f.Menu.IsOpened);
        Assert.False(f.Rows.Single(row => row.Code == "enabled").Selected);
        f.Host.OnMouseUp(f.Press());
        f.Popup.Press = () => f.Select!("enabled", true);
        var point = f.Press(); f.Host.OnMouseDown(new MouseEvent(point.X, point.Y, EnumMouseButton.Left, 0));
        Assert.True(f.EnabledState); Assert.Equal(1, f.SettingsWrites);
        f.Feature.VerifyNoOtherCalls();
    }

    /// <summary>A captured drag consumes right-click cancellation before opening a popup.</summary>
    [Fact]
    public void DragCancellationNeverOpensMenu()
    {
        using var f = new Fixture();
        f.Containers.SetMovable("test", true, PointF.Empty);
        var controls = (Dictionary<string, HudOverlayGroupControls>)typeof(HudOverlayHost).GetField("groupControls", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Host)!;
        controls["container:test"].Movement.Begin(PointF.Empty, PointF.Empty, 1);
        Assert.NotNull(f.Containers.ActiveContainer);
        var right = f.Press(); f.Host.OnMouseDown(right);
        Assert.True(right.Handled); Assert.Null(f.Containers.ActiveContainer); Assert.False(f.Menu.IsOpened);
        Assert.Equal(0, f.Popup.Presses);
    }

    /// <summary>Target precedence follows content draw order, while clipping removes an otherwise topmost target.</summary>
    [Theory]
    [InlineData(true, "test:b")]
    [InlineData(false, "test:a")]
    public void TopmostClippedContentDeterminesTarget(bool overlap, string expected)
    {
        using var f = new Fixture();
        f.Group.Members[1].Clip = overlap ? f.Group.Members[0].Clip : RectangleF.Empty;
        f.Host.OnMouseDown(f.Press());
        Assert.Equal(expected, f.Menu.TargetId);
    }

    /// <summary>Hover uses each topmost clipped indicator even without a tooltip label and never selects its entire group.</summary>
    [Theory]
    [InlineData(true, "test:b")]
    [InlineData(false, "test:a")]
    public void HoverFindsClippedMemberWithoutTooltipMetadata(bool overlap, string expected)
    {
        using var f = new Fixture();
        var press = f.Press();
        Mock.Get(f.Layout.Api.Object.Input).SetupGet(value => value.MouseX).Returns(press.X);
        Mock.Get(f.Layout.Api.Object.Input).SetupGet(value => value.MouseY).Returns(press.Y);
        f.Group.Members[1].Clip = overlap ? f.Group.Members[0].Clip : RectangleF.Empty;
        var input = (HudOverlayMenuInput)typeof(HudOverlayHost).GetField("menuInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Host)!;
        var hovered = input.HoveredMember();
        Assert.NotNull(hovered);
        Assert.Equal(expected, hovered.Registration.Id);
        Assert.Null(hovered.Registration.HoverLabelKey);
        Assert.Same(f.Group.Members.Single(value => value.Registration.Id == expected), hovered);
    }

    /// <summary>Native interaction ownership and edit eligibility suppress the indicator hover target.</summary>
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void HoverYieldsToLockedMouseForegroundModalAndPopup(bool grabbed, bool foreground, bool modal, bool popup)
    {
        using var f = new Fixture();
        var press = f.Press();
        var mouse = Mock.Get(f.Layout.Api.Object.Input);
        mouse.SetupGet(value => value.MouseX).Returns(press.X);
        mouse.SetupGet(value => value.MouseY).Returns(press.Y);
        mouse.SetupGet(value => value.MouseGrabbed).Returns(grabbed);
        if (foreground) f.Opened.Add(new Inventory(f.Layout.Api.Object, f.Group.Root.FlatCopy().WithParent(f.Layout.Gui.Object.WindowBounds)));
        if (modal) f.Opened.Add(new Modal(f.Layout.Api.Object));
        if (popup) f.Host.OnMouseDown(press);
        var input = (HudOverlayMenuInput)typeof(HudOverlayHost).GetField("menuInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Host)!;
        Assert.Null(input.HoveredMember());
    }
    /// <summary>Ordinary inventory bounds block only overlapping targets, preserving edit access elsewhere.</summary>
    [Theory]
    [InlineData(0, 0, 100, 100, false)]
    [InlineData(200, 200, 100, 100, true)]
    public void NonmodalWindowsBlockOnlyTheirBounds(double x, double y, double width, double height, bool opens)
    {
        using var f = new Fixture();
        using var window = new Inventory(f.Layout.Api.Object, ElementBounds.Fixed(x, y, width, height).WithParent(f.Layout.Gui.Object.WindowBounds));
        f.Opened.Add(window);
        var press = f.Press(); f.Host.OnMouseDown(press);
        Assert.Equal(opens, f.Menu.IsOpened); Assert.Equal(opens, press.Handled);
    }

    /// <summary>Mouse locking and host close release owned popup resources without another pointer event.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HostLifecycleClosesMenu(bool mouseLocked)
    {
        using var f = new Fixture(); f.Host.OnMouseDown(f.Press());
        if (mouseLocked) Mock.Get(f.Layout.Api.Object.Input).SetupGet(x => x.MouseGrabbed).Returns(true);
        else f.Host.TryClose();
        Assert.False(f.Host.CaptureAllInputs()); Assert.False(f.Menu.IsOpened); Assert.Equal(1, f.Popup.Disposals);
    }

    /// <summary>Temporary menu focus returns to an open nonoverlapping inventory after consumed releases.</summary>
    [Theory]
    [InlineData(200, 200, 100, 100)]
    public void ClosingMenuRestoresPreviousWindowFocus(double x, double y, double width, double height)
    {
        using var f = new Fixture();
        using var window = new Inventory(f.Layout.Api.Object, ElementBounds.Fixed(x, y, width, height).WithParent(f.Layout.Gui.Object.WindowBounds));
        f.Opened.Add(window); window.Focus();
        f.Layout.Gui.Setup(api => api.RequestFocus(It.IsAny<GuiDialog>())).Callback<GuiDialog>(dialog =>
        { if (ReferenceEquals(dialog, f.Host)) window.UnFocus(); dialog.Focus(); });
        f.Host.OnMouseDown(f.Press()); Assert.True(f.Host.Focused);
        f.Host.OnMouseUp(f.Press());
        f.Host.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Escape });
        Assert.False(window.Focused);
        f.Host.OnKeyUp(new KeyEvent { KeyCode = (int)GlKeys.Escape });
        Assert.True(window.Focused);
        f.Layout.Gui.Verify(api => api.RequestFocus(window), Times.Once);
    }
    #endregion

    #region Private
    /// <summary>Provides native nonmodal composer hit bounds without allocating artwork.</summary>
    private sealed class Inventory : GuiDialog
    {
        /// <summary>Registers one initialized native rectangle and opens the window.</summary>
        public Inventory(ICoreClientAPI api, ElementBounds bounds) : base(api)
        { bounds.CalcWorldBounds(); Composers["inventory"] = api.Gui.CreateCompo("inventory", bounds); TryOpen(false); }
        /// <inheritdoc />
        public override string ToggleKeyCombinationCode => "";
        /// <inheritdoc />
        public override bool CaptureAllInputs() => false;
    }
    /// <summary>Provides a modal native dialog without preparing artwork.</summary>
    private sealed class Modal : GuiDialog
    {
        /// <summary>Borrows the fixture API and marks the window opened.</summary>
        public Modal(ICoreClientAPI api) : base(api) { TryOpen(false); }
        /// <inheritdoc />
        public override string ToggleKeyCombinationCode => "";
        /// <inheritdoc />
        public override bool CaptureAllInputs() => true;
    }

    /// <summary>Owns native compositions, target geometry and a fake popup lifecycle.</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly HudLayoutFixture Layout = new();
        public readonly List<GuiDialog> Opened = new();
        public readonly HudOverlayGroupLayout Group = new();
        public readonly HudOverlayContainers Containers;
        public readonly HudOverlayHost Host;
        public readonly HudOverlayContextMenu Menu;
        public readonly Popup Popup = new();
        public readonly Mock<IHudOverlay> Feature = new();
        public IReadOnlyList<HudMenuEntry> Rows = Array.Empty<HudMenuEntry>();
        public bool EnabledState = true;
        public int SettingsWrites;
        public Action<string, bool>? Select;
        public bool Drawable = true;
        /// <summary>Initializes native composer and host seams without a graphics context.</summary>
        public Fixture(bool disabled = false)
        {
            Layout.Api.SetupGet(x => x.Input).Returns(Mock.Of<IInputAPI>());
            Layout.Gui.SetupGet(x => x.WindowBounds).Returns(new Window());
            Layout.Gui.SetupGet(x => x.OpenedGuis).Returns(Opened);
            Layout.Gui.SetupGet(x => x.LoadedGuis).Returns(new List<GuiDialog>());
            Layout.Gui.Setup(x => x.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
                .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { Layout.Api.Object, bounds, name }, null)!);
            EnabledState = !disabled;
            var second = Layout.Add("test:b");
            var first = new HudOverlayRegistration("test:a", Feature.Object, () => EnabledState, "test",
                new HudOverlayMenuSettings(new HudOverlaySetting<bool>(() => EnabledState, value => EnabledState = value, () => SettingsWrites++)));
            Layout.Registry.Register(first);
            Containers = new(Layout.Api.Object, Layout.Registry);
            var anchors = new HudOverlayAnchorContext(Layout.Gui.Object.WindowBounds, () => 1, () => null); anchors.BeginFrame();
            Group.Apply(anchors, Layout.Registry.GetGroups().Single(), new[] { new KeyValuePair<VanillaExpanded.HudOverlays.Registration.HudOverlayRegistration, SizeF>(first, new(20, 10)), new(second, new(20, 10)) });
            Menu = new(Layout.Api.Object, (_, rows, select, _, _) => { Rows = rows; Select = select; return Popup; }, (_, key) => key);
            Host = new(Layout.Api.Object, Layout.Registry, _ => Drawable, (_, error) => throw error, containers: Containers, contextMenu: Menu);
            Host.TryOpen(false); Host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { ["test"] = Group });
        }
        /// <summary>Targets the first member's clipped content rather than titlebar chrome.</summary>
        public MouseEvent Press() { var b = Group.Members[0].Clip; return new((int)b.Left + 1, (int)b.Top + 1, EnumMouseButton.Right, 0); }
        /// <summary>Releases host ownership before registration and layout cleanup.</summary>
        public void Dispose() { Host.Dispose(); Group.Dispose(); Layout.Dispose(); }
    }
    /// <summary>Provides initialized native screen geometry.</summary>
    private sealed class Window : ElementBounds
    {
        /// <summary>Sets independent synthetic screen dimensions.</summary>
        public Window() { IsWindowBounds = true; CalcWorldBounds(); }
        public override double renderX => 0;
        public override double renderY => 0;
        public override double absX => 0;
        public override double absY => 0;
        /// <inheritdoc />
        public override void CalcWorldBounds() { absInnerWidth = 500; absInnerHeight = 400; Initialized = true; }
    }
    /// <summary>Records native popup delegation and synchronous callbacks.</summary>
    private sealed class Popup : IHudMenuPopup
    {
        public int Presses, Disposals; public Action? Press; public Action? Render;
        public bool IsOpened => true;
        public RectangleF VisibleBounds => new(0, 0, 500, 400);
        /// <inheritdoc />
        public void Draw(float dt) => Render?.Invoke();
        /// <inheritdoc />
        public void MouseDown(MouseEvent args) { Presses++; Press?.Invoke(); }
        /// <inheritdoc />
        public void MouseUp(MouseEvent args) { }
        /// <inheritdoc />
        public void MouseMove(MouseEvent args) { }
        /// <inheritdoc />
        public void MouseWheel(MouseWheelEventArgs args) { }
        /// <inheritdoc />
        public void KeyDown(KeyEvent args) { }
        /// <inheritdoc />
        public void KeyPress(KeyEvent args) { }
        /// <inheritdoc />
        public void Dispose() => Disposals++;
    }
    #endregion
}
