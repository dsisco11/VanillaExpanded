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
    private readonly bool[] keys = new bool[Enum.GetValues<GlKeys>().Max(value => (int)value) + 1];
    private readonly PointF initial;
    private PointF pointer;
    private bool grabbed, hidden, drawable = true;

    #region Public API
    /// <summary>A hidden stand-in can be body-dragged into a live group and preserves the same commit/cancel boundary.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HiddenPlaceholderBodySupportsGrouping(bool cancel) => ExerciseGroupingPreview(cancel, hiddenPlaceholder: true);

    /// <summary>Right presses cancel captured host and titlebar-free body drags, including a joined topology preview.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RightClickCancelsJoinedPreviewThroughHost(bool hiddenPlaceholder) =>
        ExerciseGroupingPreview(true, 8, hiddenPlaceholder);

    /// <summary>A right press outside a drag remains available to ordinary HUD and game interaction.</summary>
    [Fact]
    public void IdleRightClickRemainsUnhandled()
    {
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + clip.Width / 2, clip.Top + clip.Height / 2);
        var down = Event(EnumMouseButton.Right);
        controls.OnMouseDown(api.Object, down);
        Assert.False(down.Handled);
        Assert.False(controls.Active);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }

    /// <summary>A raw overlapping release rolls back without persistence, while Shift explicitly permits the same placement.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostRejectsOverlappingDropUnlessShiftHeld(bool bypass)
    {
        using var fixture = new HudOverlayGroupControlsTests(GroupId);
        fixture.ExerciseGroupingPreview(!bypass, bypass ? 7 : 6);
    }
    /// <summary>Dragging drawable indicator content uses the same persisted movement and cancellation boundary as its titlebar.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IndicatorBodyDragCommitsOrCancels(bool cancel)
    {
        movement.SetMovable(true); gui.Invocations.Clear();
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + clip.Width / 2, clip.Top + clip.Height / 2);
        var down = Event(EnumMouseButton.Left); controls.OnContentMouseDown(api.Object, down);
        Assert.True(down.Handled); Assert.True(movement.Dragging);
        var delta = new PointF(70, 45); pointer = new PointF(pointer.X + delta.X, pointer.Y + delta.Y);
        controls.OnMouseMove(api.Object, Event(EnumMouseButton.None));
        Assert.Equal(new PointF(initial.X + delta.X, initial.Y + delta.Y), Origin());
        if (cancel) Assert.True(controls.Cancel()); else controls.OnMouseUp(api.Object, Event(EnumMouseButton.Left));
        Assert.False(movement.Dragging);
        Assert.Equal(cancel ? initial : new PointF(initial.X + delta.X, initial.Y + delta.Y), Origin());
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), cancel ? Times.Never() : Times.Once());
    }

    /// <summary>Direct content interaction respects movement permission, input ownership, visibility and the actual drawable clip.</summary>
    [Theory]
    [InlineData("fixed")] [InlineData("captured")] [InlineData("hidden")] [InlineData("undrawable")]
    [InlineData("foreground")] [InlineData("right")] [InlineData("outside")]
    public void IneligibleIndicatorBodyDoesNotCapture(string cause)
    {
        movement.SetMovable(cause != "fixed"); gui.Invocations.Clear();
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + clip.Width / 2, clip.Top + clip.Height / 2);
        switch (cause)
        {
            case "captured": grabbed = true; break;
            case "hidden": hidden = true; break;
            case "undrawable": drawable = false; break;
            case "outside": pointer = new PointF(clip.Right + 1, clip.Bottom + 1); break;
            case "foreground":
                var dialog = new Mock<GuiDialog>(api.Object); dialog.Setup(value => value.IsOpened()).Returns(true);
                dialog.Setup(value => value.CaptureAllInputs()).Returns(true); opened.Add(dialog.Object); break;
        }
        var down = Event(cause == "right" ? EnumMouseButton.Right : EnumMouseButton.Left);
        controls.OnContentMouseDown(api.Object, down);
        Assert.False(down.Handled); Assert.False(movement.Dragging);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }

    /// <summary>An active native grip survives disappearance of its source container during a grouping preview.</summary>
    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 1.5)] [InlineData(true, 1.5)]
    public void GroupingPreviewSurvivesHostSynchronizationAndCommitsOrCancels(bool cancel, float scale)
    {
        using var fixture = new HudOverlayGroupControlsTests(GroupId, scale);
        fixture.ExerciseGroupingPreview(cancel);
    }

    /// <summary>Interaction-loss boundaries roll back both participants of an already visible grouping preview.</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void JoinedPreviewRollsBackOnInteractionLoss(int cause)
    {
        using var fixture = new HudOverlayGroupControlsTests(GroupId);
        fixture.ExerciseGroupingPreview(true, cause);
    }

    /// <summary>Provides headless engine services and one measured world-status member.</summary>
    public HudOverlayGroupControlsTests() : this(GroupId) { }

    /// <summary>Creates native controls for any registered group.</summary>
    private HudOverlayGroupControlsTests(string groupId, float scale = 1)
    {
        RuntimeEnv.GUIScale = scale;
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
        input.SetupGet(value => value.KeyboardKeyStateRaw).Returns(keys);
        var window = new Window();
        gui.SetupGet(value => value.WindowBounds).Returns(window);
        gui.SetupGet(value => value.OpenedGuis).Returns(opened);
        gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { api.Object, bounds, name }, null)!);

        anchors = new HudOverlayAnchorContext(window, () => RuntimeEnv.GUIScale, () => null);
        group = new HudOverlayGroup(groupId,
            new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, 80, 80));
        registration = new HudOverlayRegistration("test:one", overlay.Object, () => true, group.Id);                var mirrors = new Dictionary<string, Vec2i>();
        gui.Setup(value => value.GetDialogPosition(It.IsAny<string>())).Returns((string key) => mirrors.GetValueOrDefault(key)!);
        gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>())).Callback<string, Vec2i>((key, value) => { if (value == null) mirrors.Remove(key); else mirrors[key] = value; });
        var strings = new Mock<ISettingsClass<string>>();
        HudLayoutFixture.BindSettings(api, strings, new Dictionary<string, string>());
        var registry = new HudOverlayRegistry(); registry.RegisterGroup(group); registry.Register(registration);
        movement = new HudOverlayContainers(api.Object, registry).Get(groupId);
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

    /// <summary>Arbitrary identities retain the actual native dropdown and selective drag behavior.</summary>
    [Theory]
    [InlineData("held-item-status")]
    [InlineData("mod:custom")]
    public void ArbitraryGroupsUseNativeControlsAndIsolatedPersistence(string groupId)
    {
        using var fixture = new HudOverlayGroupControlsTests(groupId);
        fixture.NativeTitlebarDropdownSelectsMovementMode();
        fixture.gui.Invocations.Clear();
        fixture.DragOutsideHandlePersistsContentOriginOnlyOnLeftRelease();
        fixture.gui.Verify(value => value.SetDialogPosition(It.Is<string>(key => key != HudOverlayPositionKeys.Group(groupId)), It.IsAny<Vec2i>()), Times.Never);
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
        var positions = new HudOverlayContainers(api.Object, registry);
        var host = new HudOverlayHost(api.Object, registry, _ => true, (_, failure) => throw failure, containers: positions);
        try
        {
            host.Synchronize(sharedLayouts);
            var field = typeof(HudOverlayHost).GetField("groupControls", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var owned = (Dictionary<string, HudOverlayGroupControls>)field.GetValue(host)!;
            var firstControl = owned["container:" + group.Id]; var secondControl = owned["container:" + secondGroup.Id];
            host.Synchronize(sharedLayouts);
            Assert.Same(firstControl, owned["container:" + group.Id]); Assert.Same(secondControl, owned["container:" + secondGroup.Id]);
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
            gui.Verify(value => value.SetDialogPosition(HudOverlayPositionKeys.Group(secondGroup.Id),
                It.Is<Vec2i>(position => position.X == (int)secondOrigin.X && position.Y == (int)secondOrigin.Y)), Times.Once);
            gui.Verify(value => value.SetDialogPosition(It.Is<string>(key => key != HudOverlayPositionKeys.Group(secondGroup.Id)), It.IsAny<Vec2i>()), Times.Never);
            gui.Invocations.Clear();
            secondMovement.Begin(PointF.Empty, PointF.Empty, 1);
            sharedLayouts.Remove(secondGroup.Id); host.Synchronize(sharedLayouts);
            Assert.False(secondMovement.Dragging); Assert.False(secondControl.Active);
            Assert.DoesNotContain("container:" + secondGroup.Id, owned.Keys);
            var firstMovement = positions.Get(group.Id);
            firstMovement.SetMovable(true); gui.Invocations.Clear(); firstMovement.Begin(PointF.Empty, PointF.Empty, 1);
            host.Dispose();
            Assert.False(firstMovement.Dragging); Assert.False(firstControl.Active); Assert.Empty(owned);
            gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        }
        finally { host.Dispose(); }
    }
    /// <summary>Container chrome and body-only member controllers retain distinct movement identities.</summary>
    [Fact]
    public void HostRetainsDistinctMemberAndContainerMovementIdentities()
    {
        using var registry = new HudOverlayRegistry(); registry.RegisterGroup(group); registry.Register(registration);
        var second = new HudOverlayRegistration("test:second", Mock.Of<IHudOverlay>(), () => true, group.Id); registry.Register(second);
        var containers = new HudOverlayContainers(api.Object, registry);
        layout.Apply(anchors, group, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(30, 20)), new KeyValuePair<HudOverlayRegistration, SizeF>(second, new SizeF(40, 25)) });
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, failure) => throw failure, containers: containers);
        host.Synchronize(layouts);
        var owned = (Dictionary<string, HudOverlayGroupControls>)typeof(HudOverlayHost).GetField("groupControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Assert.Contains("container:" + group.Id, owned.Keys); Assert.Contains("member:" + registration.Id, owned.Keys); Assert.Contains("member:" + second.Id, owned.Keys);
        Assert.NotSame(owned["container:" + group.Id], owned["member:" + registration.Id]);
        Assert.False(containers.Get(group.Id, registration.Id).Movable);
        containers.SetMovable(group.Id, true, initial); Assert.True(containers.Get(group.Id, registration.Id).Movable);
        containers.SetMovable(group.Id, false, initial); Assert.False(containers.Get(group.Id, registration.Id).Movable);
        overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
    }    /// <summary>Member body drags survive host repacking into singleton controls and can either commit or cancel atomically.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void IndividualBodyDragSurvivesPreviewHostSynchronization(bool cancel, bool hidden)
    {
        using var registry = new HudOverlayRegistry(); registry.RegisterGroup(group); registry.Register(registration);
        var second = new HudOverlayRegistration("test:second", Mock.Of<IHudOverlay>(), () => true, group.Id); registry.Register(second);
        var containers = new HudOverlayContainers(api.Object, registry); containers.SetMovable(group.Id, true, initial);
        var members = new[] { registration, second }; var effective = new Dictionary<string, HudOverlayGroupLayout>();
        using var previews = new HudOverlayEditPreviews(api.Object, registry, value => !hidden || value != registration, () => true,
            (_, failure) => throw failure, () => new WorkshopGaugeText((context, _) => new LoadedTexture(context.Api) { TextureId = 42, Width = 50, Height = 12, IgnoreUndisposed = true }));
        previews.Update(new HudOverlayPreparationContext(api.Object, RuntimeEnv.GUIScale, "en"));
        using var host = new HudOverlayHost(api.Object, registry, value => !hidden || value != registration || previews.IsDrawable(value),
            (_, failure) => throw failure, containers: containers, editPreviews: previews);
        // Reproduce session publication: one effective native root per container, then synchronize host controls.
        Action repack = () => {
            foreach (string id in effective.Keys.Where(id => containers.ActiveContainer == null && containers.Resolved.All(value => value.Id != id)).ToArray()) { effective[id].Dispose(); effective.Remove(id); }
            foreach (var container in containers.Resolved)
            {
                if (!effective.TryGetValue(container.Id, out var item)) effective[container.Id] = item = new HudOverlayGroupLayout();
                var measured = members.Where(value => container.Tree.Leaves().Contains(value.Id)).Select(value => new KeyValuePair<HudOverlayRegistration, SizeF>(value,
                    hidden && value == registration ? previews.GetMembers().Single().Value : new SizeF(50, 35))).ToArray();
                item.Apply(anchors, containers.Definition(container), measured, containers.Origin(container), container.Tree, container.Padding);
            }
            host.Synchronize(effective.Where(pair => containers.Resolved.Any(value => value.Id == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
        };
        host.BeforeRender = repack; repack();
        try
        {
            var owned = (Dictionary<string, HudOverlayGroupControls>)typeof(HudOverlayHost).GetField("groupControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            var control = owned["member:" + registration.Id]; var member = effective[group.Id].Members.Single(value => value.Registration.Id == registration.Id);
            pointer = new PointF((float)member.Bounds.renderX + 5, (float)member.Bounds.renderY + 5); control.Synchronize(effective);
            var down = Event(EnumMouseButton.Left);
            host.OnMouseDown(down);
            Assert.True(down.Handled);
            Assert.True(containers.Get(group.Id, registration.Id).Dragging);
            pointer = new PointF(pointer.X + 110, pointer.Y + 80); var move = Event(EnumMouseButton.None); host.OnMouseMove(move);
            Assert.True(move.Handled); Assert.Same(control, owned["member:" + registration.Id]); Assert.True(containers.Get(group.Id, registration.Id).Dragging);
            if (cancel) Assert.True(host.OnEscapePressed()); else { var up = Event(EnumMouseButton.Left); host.OnMouseUp(up); Assert.True(up.Handled); }
            Assert.False(containers.Get(group.Id, registration.Id).Dragging);
            Assert.Equal(cancel ? 1 : 2, containers.Resolved.Count);
            Assert.Equal(cancel, containers.Owner(registration.Id)!.Id == group.Id);
            overlay.Verify(value => value.Refresh(), Times.Never); overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        }
        finally { foreach (var item in effective.Values) item.Dispose(); }
    }

    /// <summary>Body-only member controllers retain container permissions and foreground window input ownership.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MemberBodyCannotBypassFixedGroupOrForegroundWindow(bool foreground)
    {
        using var registry = new HudOverlayRegistry(); registry.RegisterGroup(group); registry.Register(registration);
        registry.Register(new HudOverlayRegistration("test:second", Mock.Of<IHudOverlay>(), () => true, group.Id));
        var containers = new HudOverlayContainers(api.Object, registry);
        containers.SetMovable(group.Id, foreground, initial);
        if (foreground)
        {
            var dialog = new Mock<GuiDialog>(api.Object); dialog.Setup(value => value.IsOpened()).Returns(true);
            dialog.Setup(value => value.CaptureAllInputs()).Returns(true); opened.Add(dialog.Object);
        }
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, failure) => throw failure, containers: containers);
        host.Synchronize(layouts);
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + clip.Width / 2, clip.Top + clip.Height / 2);
        var down = Event(EnumMouseButton.Left); host.OnMouseDown(down);
        Assert.False(down.Handled); Assert.False(containers.Get(group.Id, registration.Id).Dragging);
        Assert.Null(containers.ActiveContainer);
    }
    /// <summary>Collapsed members retain membership while only their container exposes prepared native chrome.</summary>
    [Fact]
    public void OnlyContainerTitlebarIsPreparedAndMemberChromeCannotCapture()
    {
        using var registry = new HudOverlayRegistry(); registry.RegisterGroup(group); registry.Register(registration);
        registry.Register(new HudOverlayRegistration("test:hidden", Mock.Of<IHudOverlay>(), () => false, group.Id));
        var containers = new HudOverlayContainers(api.Object, registry); containers.SetMovable(group.Id, true, initial);
        using var host = new HudOverlayHost(api.Object, registry, value => value.Id == registration.Id, (_, failure) => throw failure, containers: containers);
        var clip = Assert.Single(layout.Members).Clip;
        pointer = new PointF(clip.Left + clip.Width / 2, clip.Top + clip.Height / 2); host.Synchronize(layouts);
        var owned = (Dictionary<string, HudOverlayGroupControls>)typeof(HudOverlayHost).GetField("groupControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var groupControl = owned["container:" + group.Id]; var memberControl = owned["member:" + registration.Id];
        var composerField = typeof(HudOverlayGroupControls).GetField("composer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(((GuiComposer)composerField.GetValue(groupControl)!).Composed);
        Assert.False(((GuiComposer)composerField.GetValue(memberControl)!).Composed);
        // Even a direct call bypassing host routing cannot revive a member's absent native grip or dropdown.
        pointer = new PointF((float)groupControl.Bounds.renderX + 5, (float)groupControl.Bounds.renderY + 5);
        var down = Event(EnumMouseButton.Left); memberControl.OnMouseDown(api.Object, down);
        Assert.False(down.Handled); Assert.False(containers.Get(group.Id, registration.Id).Dragging);
        var movement = containers.Get(group.Id); movement.Begin(initial, PointF.Empty, 1); movement.Move(new PointF(30, 40)); movement.Commit(new PointF(initial.X + 30, initial.Y + 40));
        Assert.Equal(new[] { "test:hidden", registration.Id }, Assert.Single(containers.Resolved).Tree.Leaves());
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
        Assert.Equal(new[] { "auto", "manual", "reset-layout" }, titleBar.Menu.Values);
        Assert.False(movement.Movable);
        SelectNativeMode(1);
        Assert.True(titleBar.Movable);
        Assert.True(movement.Movable);
        Assert.False(titleBar.Menu.IsOpened);
        gui.Invocations.Clear(); controls.Draw(0);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        SelectNativeMode(0);
        Assert.False(titleBar.Movable);
        Assert.False(movement.Movable);
        Assert.Equal(initial, movement.Position);
        Assert.Equal(initial, movement.Position);
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
        movement.SetMovable(true); gui.Invocations.Clear(); var origin = Origin();
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

    /// <summary>Keyboard focus goes to the actual native menu and its navigation callbacks select Movable.</summary>
    [Fact]
    public void NativeDropdownSupportsKeyboardSelection()
    {
        OpenNativeMenu();
        Assert.True(NativeTitleBar().Menu.HasFocus);
        var end = new KeyEvent { KeyCode = (int)GlKeys.End };
        controls.OnKeyDown(end);
        controls.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Up });
        Assert.True(end.Handled);
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        controls.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.True(movement.Movable);
        Assert.False(NativeTitleBar().Menu.IsOpened);
        Assert.Equal(initial, movement.Position);
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
        gui.Verify(value => value.SetDialogPosition(HudOverlayPositionKeys.Group(group.Id),
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
        gui.Verify(value => value.SetDialogPosition(HudOverlayPositionKeys.Group(group.Id),
            It.Is<Vec2i>(point => point.X == (int)requestedOrigin.X && point.Y == (int)requestedOrigin.Y)), Times.Once);
    }

    /// <summary>Invalidated interaction never commits partial geometry and restores the captured content placement.</summary>
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
        Assert.Equal(initial, movement.Position);
        if (cause != "scale") Assert.Equal(initial, Origin());
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }
    #endregion

    #region Private
    /// <summary>Exercises native drag routing with a freshly constructed, consistently scaled fixture.</summary>
    private void ExerciseGroupingPreview(bool cancel, int cancellation = 0, bool hiddenPlaceholder = false)
    {
        var stored = new Dictionary<string, string>(); int writes = 0;
        HudLayoutFixture.BindSettings(api, new Mock<ISettingsClass<string>>(), stored, () => writes++);
        using var registry = new HudOverlayRegistry(); registry.RegisterGroup(group); var sourceRegistration = registry.Register(registration);
        // A retained collapsed sibling forces body routing through the individual control with no titlebar.
        if (cancellation == 8 && hiddenPlaceholder)
            registry.Register(new HudOverlayRegistration("test:collapsed", Mock.Of<IHudOverlay>(), () => false, group.Id));
        var targetGroup = new HudOverlayGroup("target", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, 300, 200));
        var target = new HudOverlayRegistration("test:target", Mock.Of<IHudOverlay>(), () => true, targetGroup.Id); registry.RegisterGroup(targetGroup); registry.Register(target);
        var containers = new HudOverlayContainers(api.Object, registry); containers.SetMovable(group.Id, true, initial);
        bool targetDrawable = true;
        var members = new List<HudOverlayRegistration> { registration, target }; var effective = new Dictionary<string, HudOverlayGroupLayout>();
        using var previews = new HudOverlayEditPreviews(api.Object, registry, value => value != registration || !hiddenPlaceholder, () => true,
            (_, failure) => throw failure, () => new WorkshopGaugeText((context, _) => new LoadedTexture(context.Api) { TextureId = 42, Width = 50, Height = 12, IgnoreUndisposed = true }));
        previews.Update(new HudOverlayPreparationContext(api.Object, RuntimeEnv.GUIScale, "en"));
        using var host = new HudOverlayHost(api.Object, registry, value => value == registration && hiddenPlaceholder ? previews.IsDrawable(value) : value != target || targetDrawable,
            (_, failure) => throw failure, containers: containers, editPreviews: previews);
        // Reproduce the session's actual native-root publication after topology changes.
        Action repack = () => {
            foreach (string id in effective.Keys.Where(id => containers.ActiveContainer == null && containers.Resolved.All(value => value.Id != id)).ToArray()) { effective[id].Dispose(); effective.Remove(id); }
            foreach (var container in containers.Resolved)
            {
                if (!effective.TryGetValue(container.Id, out var item)) effective[container.Id] = item = new HudOverlayGroupLayout();
                var measured = members.Where(value => container.Tree.Leaves().Contains(value.Id) && (value != target || targetDrawable)).Select(value => new KeyValuePair<HudOverlayRegistration, SizeF>(value,
                    hiddenPlaceholder && value == registration ? previews.GetMembers().Single().Value : new SizeF(50, 35))).ToArray();
                item.Apply(anchors, containers.Definition(container), measured, containers.Origin(container), container.Tree, container.Padding);
            }
            host.Synchronize(effective.Where(pair => containers.Resolved.Any(value => value.Id == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
        };
        host.BeforeRender = repack; repack();
        try
        {
            var owned = (Dictionary<string, HudOverlayGroupControls>)typeof(HudOverlayHost).GetField("groupControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            var control = owned["container:" + group.Id];
            var sourceClip = effective[group.Id].Members.Single().Clip;
            pointer = new PointF(sourceClip.Left + sourceClip.Width / 2, sourceClip.Top + sourceClip.Height / 2); control.Synchronize(effective);
            var handle = (RectangleF)typeof(HudOverlayGroupControls).GetField("handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            if (!hiddenPlaceholder) pointer = new PointF(handle.X + handle.Width / 3, handle.Y + handle.Height / 2);
            var source = effective[group.Id].Root; var destination = effective[targetGroup.Id].Root;
            var delta = new PointF((float)(destination.renderX + destination.OuterWidth + targetGroup.Packing.Gap * RuntimeEnv.GUIScale - source.renderX), (float)(destination.renderY - source.renderY));
            if (cancellation is 6 or 7)
                delta = new PointF((float)(destination.renderX + destination.OuterWidth / 2 - pointer.X), (float)(destination.renderY + destination.OuterHeight / 2 - pointer.Y));
            if (cancellation is not (6 or 7))
                delta = new PointF((float)(destination.renderX + destination.OuterWidth - pointer.X), (float)(destination.renderY + destination.OuterHeight / 2 - pointer.Y));
            if (hiddenPlaceholder) host.OnMouseDown(Event(EnumMouseButton.Left)); else control.OnMouseDown(api.Object, Event(EnumMouseButton.Left));
            var captured = cancellation == 8 && hiddenPlaceholder ? containers.Get(group.Id, registration.Id) : containers.Get(group.Id);
            Assert.True(captured.Dragging);
            var snapshot = stored.ToDictionary(pair => pair.Key, pair => pair.Value); int startingWrites = writes;
            if (cancellation == 7) keys[(int)GlKeys.ShiftLeft] = true;
            gui.Invocations.Clear(); pointer = new PointF(pointer.X + delta.X, pointer.Y + delta.Y); host.OnMouseMove(Event(EnumMouseButton.None));
            if (cancellation is 6 or 7)
            {
                Assert.Equal(2, containers.Resolved.Count);
                var up = Event(EnumMouseButton.Left); host.OnMouseUp(up);
                Assert.True(up.Handled); Assert.False(containers.Get(group.Id).Dragging);
                Assert.Equal(2, containers.Resolved.Count);
                var restored = effective[group.Id].Root;
                if (cancel)
                {
                    Assert.Equal(initial.X, (float)restored.renderX); Assert.Equal(initial.Y, (float)restored.renderY);
                    gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
                }
                else
                {
                    // A fractional grab offset is rounded to native integer GUI coordinates on commit.
                    Assert.InRange(Math.Abs(initial.X + delta.X - (float)restored.renderX), 0, RuntimeEnv.GUIScale / 2); Assert.InRange(Math.Abs(initial.Y + delta.Y - (float)restored.renderY), 0, RuntimeEnv.GUIScale / 2);
                    gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.AtLeastOnce);
                }
                return;
            }
            Assert.Equal(cancellation == 8 && hiddenPlaceholder ? 2 : 1, containers.Resolved.Count);
            if (cancellation == 8 && hiddenPlaceholder)
            {
                var right = Event(EnumMouseButton.Right); host.OnMouseDown(right);
                Assert.True(right.Handled); Assert.False(captured.Dragging);
                host.OnMouseUp(Event(EnumMouseButton.Left));
                Assert.Equal(startingWrites, writes);
                Assert.Equal(snapshot.OrderBy(pair => pair.Key), stored.OrderBy(pair => pair.Key));
                Assert.Equal(new[] { "test:collapsed", registration.Id }, containers.Resolved.Single(value => value.Id == group.Id).Tree.Leaves());
                Assert.Equal(new[] { target.Id }, containers.Resolved.Single(value => value.Id == targetGroup.Id).Tree.Leaves());
                Assert.Equal(initial.X, (float)effective[group.Id].Root.renderX);
                Assert.Equal(initial.Y, (float)effective[group.Id].Root.renderY);
                gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
                return;
            }
            Assert.Same(control, owned["container:" + group.Id]); Assert.True(containers.Get(group.Id).Dragging);
            gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
            keys[(int)GlKeys.ShiftLeft] = true; host.OnMouseMove(Event(EnumMouseButton.None)); Assert.Equal(2, containers.Resolved.Count);
            keys[(int)GlKeys.ShiftLeft] = false; host.OnMouseMove(Event(EnumMouseButton.None)); Assert.Single(containers.Resolved);
            targetDrawable = false; host.OnMouseMove(Event(EnumMouseButton.None)); Assert.Equal(2, containers.Resolved.Count); Assert.True(containers.Get(group.Id).Dragging);
            keys[(int)GlKeys.ShiftLeft] = true; host.OnMouseMove(Event(EnumMouseButton.None));
            targetDrawable = true; keys[(int)GlKeys.ShiftLeft] = false; host.OnMouseMove(Event(EnumMouseButton.None)); Assert.Single(containers.Resolved);
            Assert.Same(source, effective[group.Id].Root);
            if (!cancel) host.OnMouseUp(Event(EnumMouseButton.Left));
            else switch (cancellation)
            {
                case 1: grabbed = true; host.OnMouseMove(Event(EnumMouseButton.None)); break;
                case 2: hidden = true; host.OnMouseMove(Event(EnumMouseButton.None)); break;
                case 3: host.UnFocus(); break;
                case 4: sourceRegistration.Dispose(); members.Remove(registration); containers.Synchronize(); repack(); host.OnMouseMove(Event(EnumMouseButton.None)); break;
                case 5: host.Dispose(); break;
                case 8:
                    var right = Event(EnumMouseButton.Right); host.OnMouseDown(right);
                    Assert.True(right.Handled);
                    Assert.False(control.Active);
                    host.OnMouseUp(Event(EnumMouseButton.Left));
                    Assert.Equal(startingWrites, writes);
                    Assert.Equal(snapshot.OrderBy(pair => pair.Key), stored.OrderBy(pair => pair.Key));
                    Assert.Equal(initial.X, (float)effective[group.Id].Root.renderX);
                    Assert.Equal(initial.Y, (float)effective[group.Id].Root.renderY);
                    Assert.Equal(new[] { registration.Id }, containers.Resolved.Single(value => value.Id == group.Id).Tree.Leaves());
                    Assert.Equal(new[] { target.Id }, containers.Resolved.Single(value => value.Id == targetGroup.Id).Tree.Leaves());
                    gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
                    break;
                default: Assert.True(host.OnEscapePressed()); break;
            }
            Assert.False(containers.Get(group.Id).Dragging); Assert.Equal(cancel ? 2 : 1, containers.Resolved.Count);
            if (!cancel) Assert.False(Assert.Single(containers.Resolved).Movable);
            overlay.Verify(value => value.Refresh(), Times.Never); overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        }
        finally { foreach (var item in effective.Values) item.Dispose(); }
    }

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

    /// <summary>Begins a left drag from the grip after persisting permission and clearing setup receipts.</summary>
    private void StartDrag()
    {
        movement.SetMovable(true); gui.Invocations.Clear(); var origin = Origin();
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
