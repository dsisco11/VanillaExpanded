using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks selective native hover controls, cancellation and layout preview using actual native bounds/composers.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayGroupControlsTests : IDisposable
{
    private const string GroupId = "world-status";
    private static readonly string DialogKey = HudOverlayPositionKeys.Group(GroupId);
    private readonly float oldScale = RuntimeEnv.GUIScale;
    private readonly string? oldLocale = Lang.CurrentLocale;
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    private readonly Mock<ICoreClientAPI> api = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IGuiAPI> gui = new();
    private readonly Mock<IInputAPI> input = new();
    private readonly Mock<IHudOverlay> overlay = new();
    private readonly HudOverlayGroupMovement movement;
    private readonly HudOverlayGroupControls controls;
    private readonly HudOverlayGroupLayout layout = new();
    private readonly HudOverlayAnchorContext anchors;
    private readonly HudOverlayGroup group;
    private readonly HudOverlayRegistration registration;
    private readonly Dictionary<string, HudOverlayGroupLayout> layouts = new();
    private readonly List<GuiDialog> opened = new();
    private readonly MouseButtonState buttons = new() { Left = true };
    private readonly PointF initial;
    private PointF pointer;
    private bool grabbed, hidden, drawable = true;

    #region Public API
    /// <summary>Provides headless engine services and one measured world-status member.</summary>
    public HudOverlayGroupControlsTests() : this(GroupId) { }

    /// <summary>Creates native controls for any registered group.</summary>
    private HudOverlayGroupControlsTests(string groupId)
    {
        RuntimeEnv.GUIScale = 1;
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = language.Object;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Input).Returns(input.Object);
        api.SetupGet(value => value.HideGuis).Returns(() => hidden);
        api.SetupGet(value => value.Render.FrameWidth).Returns(800);
        api.SetupGet(value => value.Render.FrameHeight).Returns(600);
        api.SetupGet(value => value.Render.ScissorStack).Returns(new Stack<ElementBounds>());
        gui.SetupGet(value => value.Icons).Returns(new IconUtil(api.Object));
        input.SetupGet(value => value.MouseX).Returns(() => (int)pointer.X);
        input.SetupGet(value => value.MouseY).Returns(() => (int)pointer.Y);
        input.SetupGet(value => value.MouseGrabbed).Returns(() => grabbed);
        input.SetupGet(value => value.MouseButton).Returns(buttons);
        var window = new Window();
        gui.SetupGet(value => value.WindowBounds).Returns(window);
        gui.SetupGet(value => value.OpenedGuis).Returns(opened);
        gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { api.Object, bounds, name }, null)!);
        movement = new HudOverlayGroupMovement(gui.Object, groupId);
        anchors = new HudOverlayAnchorContext(window, () => RuntimeEnv.GUIScale, () => null);
        group = new HudOverlayGroup(groupId,
            new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, 80, 80));
        registration = new HudOverlayRegistration("test:one", overlay.Object, () => true, group.Id);
        Relayout();
        initial = Origin();
        layouts[group.Id] = layout;
        controls = new HudOverlayGroupControls(api.Object, new Mock<GuiDialog>(api.Object).Object, movement,
            _ => drawable, Relayout);
        controls.Synchronize(layouts);
    }

    /// <summary>Releases separate control resources and restores native globals.</summary>
    public void Dispose()
    {
        controls.Dispose(); layout.Dispose();
        RuntimeEnv.GUIScale = oldScale;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, oldLocale);
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Nonprototype identities retain the actual native dropdown and selective drag behavior.</summary>
    [Theory]
    [InlineData("held-item-status")]
    [InlineData("mod:custom")]
    public void ArbitraryGroupsUseNativeControlsAndIsolatedPersistence(string groupId)
    {
        using var fixture = new HudOverlayGroupControlsTests(groupId);
        fixture.NativeTitlebarDropdownSelectsMovementMode();
        fixture.gui.Invocations.Clear();
        fixture.DragOutsideHandlePersistsContentOriginOnlyOnLeftRelease();
        fixture.gui.Verify(value => value.SetDialogPosition(It.Is<string>(key => key != fixture.movement.Key), It.IsAny<Vec2i>()), Times.Never);
    }
    /// <summary>The host routes an active nonfirst group, retains control instances, and cancels removed or disposed targets.</summary>
    [Fact]
    public void SharedHostRoutesSecondGroupAndReleasesAllControls()
    {
        using var registry = new HudOverlayRegistry();
        registry.RegisterGroup(group);
        registry.Register(registration);
        var secondGroup = new HudOverlayGroup("mod:second", group.Placement, group.Packing);
        registry.RegisterGroup(secondGroup);
        var secondRegistration = new HudOverlayRegistration("test:second", new Mock<IHudOverlay>().Object, () => true, secondGroup.Id);
        registry.Register(secondRegistration);
        using var secondLayout = new HudOverlayGroupLayout();
        secondLayout.Apply(anchors, secondGroup, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(secondRegistration, new SizeF(30, 25)) });
        var sharedLayouts = new Dictionary<string, HudOverlayGroupLayout> { [group.Id] = layout, [secondGroup.Id] = secondLayout };
        var positions = new HudOverlayGroupPositions(gui.Object);
        var host = new HudOverlayHost(api.Object, registry, _ => true, (_, failure) => throw failure, positions: positions);
        try
        {
            host.Synchronize(sharedLayouts);
            var field = typeof(HudOverlayHost).GetField("groupControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var owned = (Dictionary<string, HudOverlayGroupControls>)field.GetValue(host)!;
            var firstControl = owned[group.Id]; var secondControl = owned[secondGroup.Id];
            host.Synchronize(sharedLayouts);
            Assert.Same(firstControl, owned[group.Id]); Assert.Same(secondControl, owned[secondGroup.Id]);
            var secondMovement = positions.Get(secondGroup.Id);
            secondMovement.SetMovable(true);
            secondMovement.Begin(new PointF((float)secondLayout.Root.renderX, (float)secondLayout.Root.renderY), PointF.Empty, 1);
            Assert.True(host.ShouldReceiveKeyboardEvents());
            Assert.True(host.OnEscapePressed());
            Assert.False(secondMovement.Dragging); Assert.False(host.ShouldReceiveKeyboardEvents());
            var secondOrigin = new PointF((float)secondLayout.Root.renderX, (float)secondLayout.Root.renderY);
            secondMovement.Begin(secondOrigin, PointF.Empty, 1);
            pointer = new PointF(19, 23);
            var move = Event(EnumMouseButton.None); host.OnMouseMove(move);
            Assert.True(move.Handled);
            Assert.Equal(new PointF(secondOrigin.X + pointer.X, secondOrigin.Y + pointer.Y), secondMovement.Position);
            var release = Event(EnumMouseButton.Left); host.OnMouseUp(release);
            Assert.True(release.Handled); Assert.False(secondMovement.Dragging);
            // This host fixture has no relayout callback, so release persists the currently displayed layout origin.
            gui.Verify(value => value.SetDialogPosition(secondMovement.Key,
                It.Is<Vec2i>(position => position.X == (int)secondOrigin.X && position.Y == (int)secondOrigin.Y)), Times.Once);
            gui.Verify(value => value.SetDialogPosition(It.Is<string>(key => key != secondMovement.Key), It.IsAny<Vec2i>()), Times.Never);
            gui.Invocations.Clear();
            secondMovement.Begin(PointF.Empty, PointF.Empty, 1);
            sharedLayouts.Remove(secondGroup.Id); host.Synchronize(sharedLayouts);
            Assert.False(secondMovement.Dragging); Assert.False(secondControl.Active);
            Assert.DoesNotContain(secondGroup.Id, owned.Keys);
            var firstMovement = positions.Get(group.Id);
            firstMovement.SetMovable(true); firstMovement.Begin(PointF.Empty, PointF.Empty, 1);
            host.Dispose();
            Assert.False(firstMovement.Dragging); Assert.False(firstControl.Active); Assert.Empty(owned);
            gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        }
        finally { host.Dispose(); }
    }
    /// <summary>Pointer activity away from controls remains untouched; hover rendering does not persist or prepare content.</summary>
    [Fact]
    public void OrdinaryInputAndHoverArePassive()
    {
        foreach (var position in new[] { PointF.Empty, new PointF(initial.X + 5, initial.Y + 5) })
        {
            pointer = position;
            var down = Event(EnumMouseButton.Left); controls.OnMouseDown(api.Object, down);
            var move = Event(EnumMouseButton.None); controls.OnMouseMove(api.Object, move);
            var up = Event(EnumMouseButton.Left); controls.OnMouseUp(api.Object, up);
            controls.Draw(0);
            Assert.False(down.Handled || move.Handled || up.Handled);
        }
        Assert.Equal(initial, Origin());
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        overlay.Verify(value => value.Refresh(), Times.Never);
    }

    /// <summary>An unrevealed handle cannot capture a click; entering drawable content establishes the bridge to it.</summary>
    [Fact]
    public void HandleCanOnlyBeClickedAfterDrawableContentRevealsIt()
    {
        movement.SetMovable(true);
        var handlePointer = new PointF((float)controls.Bounds.renderX + 5, (float)controls.Bounds.renderY + 5);
        pointer = handlePointer;
        var invisiblePress = Event(EnumMouseButton.Left);
        controls.OnMouseDown(api.Object, invisiblePress);
        Assert.False(invisiblePress.Handled);
        Assert.False(movement.Dragging);
        pointer = new PointF(initial.X + 5, initial.Y + 5);
        controls.Synchronize(layouts);
        pointer = handlePointer;
        var visiblePress = Event(EnumMouseButton.Left);
        controls.OnMouseDown(api.Object, visiblePress);
        Assert.True(visiblePress.Handled);
        Assert.True(movement.Dragging);
    }

    /// <summary>The actual native titlebar and its actual dropdown switch Fixed/Movable without a custom toggle or render-time save.</summary>
    [Fact]
    public void NativeTitlebarDropdownSelectsMovementMode()
    {
        var titleBar = NativeTitleBar();
        Assert.IsAssignableFrom<GuiElementDialogTitleBar>(titleBar);
        Assert.IsType<GuiElementListMenu>(titleBar.Menu);
        Assert.Equal(new[] { "auto", "manual" }, titleBar.Menu.Values);
        Assert.False(movement.Movable);
        SelectNativeMode(1);
        Assert.True(titleBar.Movable);
        Assert.True(movement.Movable);
        Assert.False(titleBar.Menu.IsOpened);
        controls.Draw(0);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        SelectNativeMode(0);
        Assert.False(titleBar.Movable);
        Assert.False(movement.Movable);
        Assert.Null(movement.Position);
        gui.Verify(value => value.SetDialogPosition(movement.Key, null!), Times.AtLeastOnce);
    }

    /// <summary>The real native bar caches prepared artwork across frames while preserving its dropdown interaction.</summary>
    [Fact]
    public void NativeBarCachesArtworkAndPreservesDropdownInteraction()
    {
        int uploads = 0;
        gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<Cairo.ImageSurface>(), true, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new CairoUpload((Cairo.ImageSurface surface, bool _, ref LoadedTexture texture) =>
                uploads++));
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + 5, clip.Top + 5);
        controls.Synchronize(layouts);
        Assert.True(uploads > 0);
        int preparedUploads = uploads;
        controls.Draw(0); controls.Draw(0);
        Assert.Equal(preparedUploads, uploads);
        SelectNativeMode(1);
        Assert.True(movement.Movable);
    }

    /// <summary>Movable outlines remain visible away from hover, use exact group bounds, and disappear when eligibility is lost.</summary>
    [Theory]
    [InlineData("fixed")]
    [InlineData("grabbed")]
    [InlineData("hidden")]
    [InlineData("undrawable")]
    [InlineData("removed")]
    [InlineData("foreground")]
    public void OutlineUsesGroupBoundsAndRespectsInteractionEligibility(string unavailable)
    {
        movement.SetMovable(true);
        pointer = PointF.Empty;
        var renderer = Mock.Get(api.Object.Render);
        renderer.Invocations.Clear();
        controls.DrawBackground();
        var outline = Assert.Single(renderer.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderRectangle));
        Assert.Equal((float)layout.Root.renderX, outline.Arguments[0]);
        Assert.Equal((float)layout.Root.renderY, outline.Arguments[1]);
        Assert.Equal((float)layout.Root.OuterWidth, outline.Arguments[3]);
        Assert.Equal((float)layout.Root.OuterHeight, outline.Arguments[4]);
        Assert.DoesNotContain(renderer.Invocations, call => call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
        switch (unavailable)
        {
            case "fixed": movement.SetMovable(false); break;
            case "grabbed": grabbed = true; break;
            case "hidden": hidden = true; break;
            case "undrawable": drawable = false; break;
            case "removed": layouts.Clear(); controls.Synchronize(layouts); break;
            case "foreground":
                var modal = new Mock<GuiDialog>(api.Object);
                modal.Setup(value => value.IsOpened()).Returns(true);
                modal.Setup(value => value.CaptureAllInputs()).Returns(true);
                opened.Add(modal.Object); break;
        }
        renderer.Invocations.Clear();
        controls.DrawBackground();
        Assert.Empty(renderer.Invocations);
    }

    /// <summary>A nonmodal window owns hover input without suppressing the movable group's global outline cue.</summary>
    [Fact]
    public void NonmodalForegroundKeepsOutlineWithoutHoverPanel()
    {
        movement.SetMovable(true);
        var origin = Origin();
        var foreground = new Mock<GuiDialog>(api.Object);
        foreground.Setup(value => value.IsOpened()).Returns(true);
        var bounds = ElementBounds.Fixed(origin.X, origin.Y, 200, 200).WithEmptyParent();
        bounds.CalcWorldBounds();
        foreground.Object.Composers["foreground"] = gui.Object.CreateCompo("foreground", bounds);
        opened.Add(foreground.Object);
        pointer = new PointF(origin.X + 10, origin.Y + 10);
        var renderer = Mock.Get(api.Object.Render);
        renderer.Invocations.Clear();
        controls.DrawBackground();
        var outline = Assert.Single(renderer.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderRectangle));
        Assert.Equal((float)layout.Root.renderX, outline.Arguments[0]);
        Assert.Equal((float)layout.Root.renderY, outline.Arguments[1]);
        Assert.Equal((float)layout.Root.OuterWidth, outline.Arguments[3]);
        Assert.Equal((float)layout.Root.OuterHeight, outline.Arguments[4]);
        Assert.DoesNotContain(renderer.Invocations, call => call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
        var down = Event(EnumMouseButton.Left);
        controls.OnMouseDown(api.Object, down);
        Assert.False(down.Handled);
        Assert.False(controls.Active);
    }

    /// <summary>The real native popup and its clipping stay inside the viewport near every corner at both GUI scales.</summary>
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 10000, 0)]
    [InlineData(1, 0, 10000)]
    [InlineData(1, 10000, 10000)]
    [InlineData(2, 10000, 10000)]
    public void NativePopupFitsViewportAndStillSelects(double scale, float x, float y)
    {
        RuntimeEnv.GUIScale = (float)scale;
        movement.SetMovable(true);
        movement.Begin(initial, PointF.Empty, scale);
        movement.Commit(new PointF(x, y));
        Relayout();
        OpenNativeMenu();
        var menu = NativeTitleBar().Menu;
        var clip = (ElementBounds)typeof(GuiElementListMenu).GetField("visibleBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var viewport = gui.Object.WindowBounds;
        Assert.InRange(clip.renderX, viewport.renderX, viewport.renderX + viewport.OuterWidth);
        Assert.InRange(clip.renderY, viewport.renderY, viewport.renderY + viewport.OuterHeight);
        Assert.True(clip.renderX + clip.OuterWidth <= viewport.renderX + viewport.OuterWidth + .01);
        Assert.True(clip.renderY + clip.OuterHeight <= viewport.renderY + viewport.OuterHeight + .01);
        ClickNativeRow(0);
        Assert.False(movement.Movable);
        Assert.False(menu.IsOpened);
    }

    /// <summary>Keyboard focus goes to the actual native menu and its End/Enter callbacks select Movable.</summary>
    [Fact]
    public void NativeDropdownSupportsKeyboardSelection()
    {
        OpenNativeMenu();
        Assert.True(NativeTitleBar().Menu.HasFocus);
        var end = new KeyEvent { KeyCode = (int)GlKeys.End };
        controls.OnKeyDown(end);
        Assert.True(end.Handled);
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        controls.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.True(movement.Movable);
        Assert.False(NativeTitleBar().Menu.IsOpened);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }

    /// <summary>Release and cancellation clear a captured native scrollbar instead of retaining its drag flag.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeScrollbarCaptureIsReleased(bool cancel)
    {
        OpenNativeMenu();
        var scrollbar = (GuiElementCompactScrollbar)typeof(GuiElementListMenu)
            .GetField("scrollbar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(NativeTitleBar().Menu)!;
        // Model an already captured native scrollbar; only release routing is under test here.
        scrollbar.mouseDownOnScrollbarHandle = true;
        if (cancel) controls.Cancel(); else controls.OnMouseUp(api.Object, Event(EnumMouseButton.Left));
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
    }

    /// <summary>Dragging continues outside the initial bounds, survives layout synchronization and writes only on left release.</summary>
    [Fact]
    public void DragOutsideHandlePersistsContentOriginOnlyOnLeftRelease()
    {
        StartDrag();
        pointer = new PointF(pointer.X + 120, pointer.Y + 100);
        var move = Event(EnumMouseButton.None); controls.OnMouseMove(api.Object, move);
        Assert.True(move.Handled);
        Assert.Equal(new PointF(initial.X + 120, initial.Y + 100), Origin());
        Relayout(); controls.Synchronize(layouts);
        Assert.Equal(new PointF(initial.X + 120, initial.Y + 100), Origin());
        var otherRelease = Event(EnumMouseButton.Right); controls.OnMouseUp(api.Object, otherRelease);
        Assert.True(movement.Dragging);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        var release = Event(EnumMouseButton.Left); controls.OnMouseUp(api.Object, release);
        Assert.True(release.Handled);
        Assert.False(movement.Dragging);
        gui.Verify(value => value.SetDialogPosition(movement.Key,
            It.Is<Vec2i>(point => point.X == (int)Origin().X && point.Y == (int)Origin().Y)), Times.Once);
    }

    /// <summary>A below-content grip still saves the content origin, and unrelated foreground windows do not block it.</summary>
    [Fact]
    public void BelowContentHandleKeepsContentOriginAndNonoverlappingWindowAllowsDrag()
    {
        movement.SetMovable(true);
        movement.Begin(initial, PointF.Empty, 1);
        var requestedOrigin = new PointF(80, 12);
        movement.Commit(requestedOrigin);
        Relayout();
        gui.Invocations.Clear();
        var foreground = new Mock<GuiDialog>(api.Object);
        foreground.Setup(value => value.IsOpened()).Returns(true);
        var windowBounds = ElementBounds.Fixed(500, 300, 100, 100).WithEmptyParent();
        windowBounds.CalcWorldBounds();
        foreground.Object.Composers["other"] = gui.Object.CreateCompo("other", windowBounds);
        opened.Add(foreground.Object);
        StartDrag();
        Assert.True(controls.Bounds.renderY > layout.Root.renderY + layout.Root.OuterHeight);
        controls.OnMouseUp(api.Object, Event(EnumMouseButton.Left));
        Assert.Equal(requestedOrigin, Origin());
        gui.Verify(value => value.SetDialogPosition(movement.Key,
            It.Is<Vec2i>(point => point.X == (int)requestedOrigin.X && point.Y == (int)requestedOrigin.Y)), Times.Once);
    }

    /// <summary>Invalidated interaction never commits partial geometry and restores the default content placement.</summary>
    [Theory]
    [InlineData("capture")]
    [InlineData("hidden")]
    [InlineData("removed")]
    [InlineData("undrawable")]
    [InlineData("missedrelease")]
    [InlineData("scale")]
    [InlineData("foreground")]
    [InlineData("escape")]
    public void CancellationRestoresStartingPreferenceWithoutWriting(string cause)
    {
        StartDrag();
        pointer = new PointF(pointer.X + 30, pointer.Y + 40);
        controls.OnMouseMove(api.Object, Event(EnumMouseButton.None));
        switch (cause)
        {
            case "capture": grabbed = true; break;
            case "hidden": hidden = true; break;
            case "removed": layouts.Clear(); break;
            case "undrawable": drawable = false; break;
            case "missedrelease": buttons.Left = false; break;
            case "scale": RuntimeEnv.GUIScale = 2; break;
            case "foreground":
                var dialog = new Mock<GuiDialog>(api.Object);
                dialog.Setup(value => value.IsOpened()).Returns(true);
                dialog.Setup(value => value.CaptureAllInputs()).Returns(true);
                opened.Add(dialog.Object); break;
            case "escape": Assert.True(controls.Cancel()); break;
        }
        controls.Synchronize(layouts);
        controls.Draw(0);
        Assert.False(controls.Active);
        Assert.False(movement.Dragging);
        Assert.Null(movement.Position);
        if (cause != "scale") Assert.Equal(initial, Origin());
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }
    #endregion

    #region Private
    /// <summary>Matches the native ref-texture upload signature while keeping real Cairo composition active.</summary>
    private delegate void CairoUpload(Cairo.ImageSurface surface, bool linear, ref LoadedTexture texture);

    /// <summary>Accesses the concrete production native widget rather than substituting a fake control.</summary>
    private HudOverlayNativeTitleBar NativeTitleBar() =>
        (HudOverlayNativeTitleBar)typeof(HudOverlayGroupControls).GetField("titleBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controls)!;

    /// <summary>Clicks the native menu glyph and the native dropdown row through production pointer routing.</summary>
    private void SelectNativeMode(int index)
    {
        OpenNativeMenu();
        ClickNativeRow(index);
    }

    /// <summary>Reveals the controls and clicks the actual native menu glyph through its composed hit rectangle.</summary>
    private void OpenNativeMenu()
    {
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + 5, clip.Top + 5);
        controls.Synchronize(layouts);
        var titleBar = NativeTitleBar();
        var menuRect = (Vintagestory.API.Datastructures.Rectangled)typeof(GuiElementDialogTitleBar)
            .GetField("menuIconRect", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(titleBar)!;
        pointer = new PointF((float)(titleBar.Bounds.absX + menuRect.X + menuRect.Width / 2),
            (float)(titleBar.Bounds.absY + menuRect.Y + menuRect.Height / 2));
        controls.OnMouseDown(api.Object, Event(EnumMouseButton.Left));
        controls.OnMouseUp(api.Object, Event(EnumMouseButton.Left));
        Assert.True(titleBar.Menu.IsOpened);
        // The live session synchronizes controls after its layout callback; apply that same boundary before inspection.
        controls.Synchronize(layouts);
    }

    /// <summary>Clicks the selected real native popup row using the active GUI scale.</summary>
    private void ClickNativeRow(int index)
    {
        var titleBar = NativeTitleBar();
        pointer = new PointF((float)titleBar.Menu.Bounds.renderX + 10,
            (float)(titleBar.Menu.Bounds.renderY + titleBar.Menu.Bounds.InnerHeight + (30 * index + 15) * RuntimeEnv.GUIScale));
        var selection = Event(EnumMouseButton.Left);
        controls.OnMouseDown(api.Object, selection);
        controls.OnMouseUp(api.Object, Event(EnumMouseButton.Left));
        Assert.True(selection.Handled);
    }

    /// <summary>Begins a left drag from the grip after enabling movement without writing a saved position.</summary>
    private void StartDrag()
    {
        movement.SetMovable(true);
        var origin = Origin();
        pointer = new PointF(origin.X + 5, origin.Y + 5);
        controls.Synchronize(layouts);
        pointer = new PointF((float)controls.Bounds.renderX + 5, (float)controls.Bounds.renderY + 5);
        var down = Event(EnumMouseButton.Left);
        controls.OnMouseDown(api.Object, down);
        Assert.True(down.Handled);
        Assert.True(movement.Dragging);
    }

    /// <summary>Refreshes native geometry using preview precedence without touching the feature reading.</summary>
    private void Relayout()
    {
        anchors.BeginFrame();
        layout.Apply(anchors, group, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(90, 90)) }, movement.Position);
    }

    /// <summary>Reports displayed content coordinates rather than the titlebar's location.</summary>
    private PointF Origin() => new((float)layout.Root.renderX, (float)layout.Root.renderY);

    /// <summary>Creates native pointer delivery using the mutable mock cursor location.</summary>
    private MouseEvent Event(EnumMouseButton button) => new((int)pointer.X, (int)pointer.Y, button, 0);

    /// <summary>Supplies a fixed native window for compositing and clamping.</summary>
    private sealed class Window : ElementBounds
    {
        #region Public API
        /// <summary>Initializes an actual native bounds parent with fixed framebuffer dimensions.</summary>
        public Window() { IsWindowBounds = true; CalcWorldBounds(); }
        /// <summary>Provides the native screen origin without a platform.</summary>
        public override double renderX => 0;
        /// <summary>Provides the native screen origin without a platform.</summary>
        public override double renderY => 0;
        /// <summary>Provides native hit-test coordinates.</summary>
        public override double absX => 0;
        /// <summary>Provides native hit-test coordinates.</summary>
        public override double absY => 0;
        /// <summary>Provides enough viewport space for content and its separate controls.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 800; absInnerHeight = 600; Initialized = true; }
        #endregion
    }
    #endregion
}
