using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies that registered defaults remain attached when permission changes materialize them.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudDefaultAttachmentTests : IDisposable
{
    private readonly string? oldLocale = Lang.CurrentLocale;
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    /// <summary>Provides native localization for system hotkey registration.</summary>
    public HudDefaultAttachmentTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = language.Object;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
    }
    /// <summary>Restores engine localization after the system integration checks.</summary>
    public void Dispose()
    {
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, oldLocale);
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }
    /// <summary>Actual built-in registrations preserve their attachment, pivot and inset through collapse, toggles and reload.</summary>
    [Theory]
    [InlineData("player-status", "hotbar", 1, 1200, 900, 400, 700, 400, 70, 35, 20)]
    [InlineData("player-status", "hotbar", 2, 1600, 1200, 600, 900, 400, 80, 35, 20)]
    [InlineData("held-item-status", "saturation", 1, 1200, 900, 400, 700, 400, 70, 35, 20)]
    [InlineData("held-item-status", "saturation", 2, 1600, 1200, 600, 900, 400, 80, 35, 20)]
    [InlineData("world-status", "screen", 1, 1200, 900, 400, 700, 400, 70, 35, 20)]
    [InlineData("world-status", "screen", 2, 1600, 1200, 600, 900, 400, 80, 35, 20)]
    public void BuiltInDefaultsRetainAttachment(string groupId, string targetId, float scale, float windowWidth, float windowHeight,
        float x, float y, float targetWidth, float targetHeight, float width, float height)
    {
        float old = RuntimeEnv.GUIScale; int left = GuiStyle.LeftDialogMargin, right = GuiStyle.RightDialogMargin; var system = new HudOverlaySystem();
        try
        {
            RuntimeEnv.GUIScale = scale; GuiStyle.LeftDialogMargin = GuiStyle.RightDialogMargin = 0;
            using var fixture = new HudLayoutFixture();
            fixture.Api.SetupGet(api => api.Event).Returns(Mock.Of<IClientEventAPI>());
            fixture.Api.SetupGet(api => api.Input).Returns(Mock.Of<IInputAPI>());
            system.StartClientSide(fixture.Api.Object);
            var group = system.Registry.GetGroups().Single(value => value.Id == groupId);
            Assert.Equal(targetId, group.Placement.TargetId);
            fixture.Registry.RegisterGroup(group);
            var first = fixture.Add("test:first", groupId); var second = fixture.Add("test:second", groupId);
            var members = new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(first, new(width, height)),
                new KeyValuePair<HudOverlayRegistration, SizeF>(second, new(width, height)) };
            var viewport = new RectangleF(0, 0, windowWidth, windowHeight);
            var target = targetId == HudOverlayTargets.Screen ? viewport : new RectangleF(x, y, targetWidth, targetHeight);
            var context = new HudOverlayAnchorContext(new Window(windowWidth, windowHeight), () => scale, () => target,
                readBoundary: _ => target);
            var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
            using var layout = new HudOverlayGroupLayout();
            // Defaults are derived rather than persisted, and both full and collapsed layouts use their declared pivot.
            foreach (bool expanded in new[] { true, false, true })
                ApplyAndAssert(containers, layout, context, groupId, expanded ? members : members.Take(1).ToArray(), target, scale);
            var checkpoint = new PointF((float)layout.Root.renderX / scale, (float)layout.Root.renderY / scale);
            Assert.True(containers.SetMovable(groupId, true, checkpoint));
            Assert.Equal(group.Placement, containers.Owner(first.Id)!.Attachment);
            containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
            foreach (bool expanded in new[] { false, true, false })
                ApplyAndAssert(containers, layout, context, groupId, expanded ? members : members.Take(1).ToArray(), target, scale);
            Assert.True(containers.SetMovable(groupId, false, checkpoint));
            containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
            Assert.Equal(group.Placement, containers.Owner(first.Id)!.Attachment);
            ApplyAndAssert(containers, layout, context, groupId, members, target, scale);
            Assert.True(containers.Reset());
            Assert.Null(containers.Owner(first.Id)!.Attachment); Assert.Null(containers.Owner(first.Id)!.X);
            ApplyAndAssert(containers, layout, context, groupId, members.Take(1).ToArray(), target, scale);
        }
        finally { system.Dispose(); RuntimeEnv.GUIScale = old; GuiStyle.LeftDialogMargin = left; GuiStyle.RightDialogMargin = right; }
    }

    /// <summary>Every held-item configuration can become a persisted attachment, including default GUI-scaled screen margins.</summary>
    [Theory]
    [InlineData("saturation", 11, 17, 30, 40)] [InlineData("hotbar", 11, 17, 30, 40)]
    [InlineData("screen-left-top", 11, 17, 30, 40)] [InlineData("screen-center-top", 11, 17, 30, 40)]
    [InlineData("screen-right-top", 11, 17, 30, 40)] [InlineData("screen-left-middle", 11, 17, 30, 40)]
    [InlineData("screen-center-middle", 11, 17, 30, 40)] [InlineData("screen-right-middle", 11, 17, 30, 40)]
    [InlineData("screen-left-bottom", 11, 17, 30, 40)] [InlineData("screen-center-bottom", 11, 17, 30, 40)]
    [InlineData("screen-right-bottom", 11, 17, 30, 40)]
    public void ConfiguredPlacementSurvivesPermissionAndReset(string choice, float x, float y, float checkpointX, float checkpointY)
    {
        using var fixture = new HudLayoutFixture();
        var config = new VanillaExpandedConfig { HeldItemStatusAnchor = choice, HeldItemStatusOffsetX = x, HeldItemStatusOffsetY = y };
        var original = HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config);
        fixture.Registry.RegisterGroup(new("held-item-status", original)); fixture.Add("test:first", "held-item-status");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.True(HudContainerAttachment.IsSupported(original));
        Assert.True(containers.SetMovable("held-item-status", true, new(checkpointX, checkpointY)));
        containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.False(containers.ReadOnly); Assert.Equal(original, containers.Owner("test:first")!.Attachment);
        // Explicit materialization freezes the selected placement; resetting returns to the current configured default.
        config.HeldItemStatusOffsetX = x * 2;
        var replacement = HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config);
        fixture.Registry.UpdateGroup(new("held-item-status", replacement)); containers.Synchronize();
        Assert.Equal(original, containers.Owner("test:first")!.Attachment);
        Assert.True(containers.Reset());
        Assert.Equal(replacement, containers.Definition(containers.Owner("test:first")!).Placement);
    }

    /// <summary>Legacy and explicitly moved coordinates do not acquire an invented default attachment.</summary>
    [Theory]
    [InlineData(false, 40, 50)] [InlineData(true, 60, 70)]
    public void ExistingFreePositionsStayFree(bool legacy, int x, int y)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:first", anchor: HudOverlayTargets.Hotbar);
        if (legacy) fixture.Positions[HudOverlayPositionKeys.Group("test")] = new(x, y);
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        if (!legacy)
        {
            Assert.True(containers.SetMovable("test", true, new(x, y)));
            Assert.True(containers.Begin("test", null, new(x, y), new Dictionary<string, PointF>()));
            Assert.True(containers.Commit(new(x, y)));
        }
        Assert.True(containers.SetMovable("test", false, new(x, y)));
        Assert.Null(containers.Owner("test:first")!.Attachment);
    }
    /// <summary>Native dialog margins survive materialization without a jump, including fractional GUI scales.</summary>
    [Theory]
    [InlineData(HudOverlayPoint.LeftTop, 1, 23, 31, 1200, 900, 35, 20)]
    [InlineData(HudOverlayPoint.RightTop, 2, 23, 31, 1200, 900, 35, 20)]
    [InlineData(HudOverlayPoint.CenterTop, 1.5f, 23, 31, 1200, 900, 35, 20)]
    public void NativeMarginsRetainPosition(HudOverlayPoint corner, float scale, int left, int right,
        float width, float height, float memberWidth, float memberHeight)
    {
        float old = RuntimeEnv.GUIScale; int oldLeft = GuiStyle.LeftDialogMargin, oldRight = GuiStyle.RightDialogMargin;
        try
        {
            RuntimeEnv.GUIScale = scale; GuiStyle.LeftDialogMargin = left; GuiStyle.RightDialogMargin = right;
            using var fixture = new HudLayoutFixture();
            var group = new HudOverlayGroup("test", new(HudOverlayTargets.Screen, corner, corner));
            fixture.Registry.RegisterGroup(group); var member = fixture.Add("test:first");
            var members = new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(member, new(memberWidth, memberHeight)) };
            var context = new HudOverlayAnchorContext(new Window(width, height), () => scale, () => null);
            var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
            using var layout = new HudOverlayGroupLayout(); context.BeginFrame(); layout.Apply(context, group, members);
            var before = new PointF((float)layout.Root.renderX, (float)layout.Root.renderY);
            Assert.True(containers.SetMovable("test", true, new(before.X / scale, before.Y / scale)));
            containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
            var container = Assert.Single(containers.Resolved);
            context.BeginFrame(); layout.Apply(context, group, members, containers.Origin(container), container.Tree, container.Padding, container.Attachment);
            Assert.InRange(Math.Abs(before.X - layout.Root.renderX), 0, scale + .001);
            Assert.InRange(Math.Abs(before.Y - layout.Root.renderY), 0, scale + .001);
        }
        finally { RuntimeEnv.GUIScale = old; GuiStyle.LeftDialogMargin = oldLeft; GuiStyle.RightDialogMargin = oldRight; }
    }

    /// <summary>Joining an untouched fixed default persists its attachment and permission for the merged container.</summary>
    [Theory]
    [InlineData("hotbar", HudOverlayPoint.LeftBottom, HudOverlayPoint.RightBottom, 80, 90, 200, 100, 40)]
    [InlineData("screen", HudOverlayPoint.RightTop, HudOverlayPoint.RightTop, 80, 90, 200, 100, 40)]
    public void FixedDefaultJoinRetainsAttachment(string targetId, HudOverlayPoint anchor, HudOverlayPoint pivot,
        float x, float y, float targetWidth, float targetHeight, float incomingWidth)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            using var fixture = new HudLayoutFixture(); fixture.Registry.RegisterGroup(new("target", new(targetId, anchor, pivot)));
            fixture.Add("test:target", "target"); fixture.Add("test:incoming", "source");
            var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
            var expected = containers.Attachment(containers.Owner("test:target")!);
            Assert.True(containers.SetMovable("source", true, new(x, y)));
            Assert.True(containers.Begin("source", null, new(x, y), new Dictionary<string, PointF>()));
            var target = new RectangleF(x, y, targetWidth, targetHeight);
            var candidate = new HudSnapCandidate("group", new("target", null, target, 0), HudSnapSide.Left,
                HudOverlayCrossAlignment.Start, new(x - incomingWidth, y), 0, target);
            Assert.True(containers.PreviewJoin(candidate, new(x - incomingWidth, y)));
            Assert.True(containers.Commit(new(x - incomingWidth, y)));
            var joined = new HudOverlayContainers(fixture.Api.Object, fixture.Registry).Owner("test:target")!;
            Assert.Equal(expected, joined.Attachment); Assert.False(joined.Movable); Assert.Contains("test:incoming", joined.Tree.Leaves());
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    #endregion

    #region Private
    /// <summary>Compares measured native bounds with the configured attachment points rather than prescribed visual dimensions.</summary>
    private static void ApplyAndAssert(HudOverlayContainers containers, HudOverlayGroupLayout layout, HudOverlayAnchorContext context,
        string groupId, KeyValuePair<HudOverlayRegistration, SizeF>[] members, RectangleF target, float scale)
    {
        var container = containers.Resolved.Single(value => value.Id == groupId); var group = containers.Definition(container);
        context.BeginFrame(); layout.Apply(context, group, members, containers.Origin(container), container.Tree, container.Padding, containers.Attachment(container));
        Assert.True(layout.Available); Assert.Equal(members.Length, layout.Members.Count);
        var placement = container.Attachment ?? group.Placement;
        if (placement.TargetId == HudOverlayTargets.Screen)
            target = HudOverlayAnchorContext.Inset(target, placement.ScreenPaddingPixels ?? HudOverlayAnchorContext.SafeInset * scale);
        var anchor = HudOverlayPoints.Normalized(placement.Attachment); var pivot = HudOverlayPoints.Normalized(placement.Pivot);
        Assert.InRange(Math.Abs(target.Left + anchor.X * target.Width + placement.OffsetX * scale - layout.Root.renderX - pivot.X * layout.Root.OuterWidth), 0, scale + .001);
        Assert.InRange(Math.Abs(target.Top + anchor.Y * target.Height + placement.OffsetY * scale - layout.Root.renderY - pivot.Y * layout.Root.OuterHeight), 0, scale + .001);
    }

    /// <summary>Supplies a physical viewport without starting the graphical platform.</summary>
    private sealed class Window : ElementBounds
    {
        #region Public API
        /// <summary>Installs the caller-supplied viewport dimensions.</summary>
        public Window(float width, float height) { IsWindowBounds = true; absInnerWidth = width; absInnerHeight = height; Initialized = true; requiresrelculation = false; }
        /// <summary>Excludes unrelated dialog margins from the viewport.</summary>
        public override double renderX => 0;
        /// <summary>Excludes unrelated dialog margins from the viewport.</summary>
        public override double renderY => 0;
        /// <summary>Preserves the synthetic viewport when native child bounds recalculate.</summary>
        public override void CalcWorldBounds() { Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}


