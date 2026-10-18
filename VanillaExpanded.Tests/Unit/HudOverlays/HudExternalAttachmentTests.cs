using System.Drawing;
using System.Text.Json;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises the same external placement contract for individual and composite native layouts.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudExternalAttachmentTests
{
    #region Public API
    /// <summary>Detaching from a padded pair preserves the destination attachment and padding through singleton normalization.</summary>
    [Theory]
    [InlineData(7, 40, 30, false)] [InlineData(9, 60, 25, true)]
    public void DetachedRemainderRetainsDockingAndPadding(double padding, float x, float y, bool cancel)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b");
        var attachment = new HudOverlayPlacement("native:hotbar", HudOverlayPoint.LeftBottom, HudOverlayPoint.RightBottom, screenPaddingPixels: 0);
        var original = Record(attachment) with { Padding = padding, Tree = new() { First = new() { Indicator = "test:a" }, Second = new() { Indicator = "test:b" } } };
        fixture.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] { original } });
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        var origins = new Dictionary<string, PointF> { ["test:a"] = new(x, y), ["test:b"] = new(x + x, y) };
        Assert.True(containers.Begin(original.Id, "test:a", origins["test:a"], origins, new PointF(original.X!.Value, original.Y!.Value)));
        var remainder = containers.Owner("test:b")!;
        Assert.Equal(attachment, remainder.Attachment); Assert.Equal(padding, remainder.Padding); Assert.Null(containers.Owner("test:a")!.Attachment);
        if (cancel) { containers.Cancel(); Assert.Equal(original, Assert.Single(containers.Resolved)); }
        else
        {
            Assert.True(containers.Commit(new PointF(x, y)));
            remainder = new HudOverlayContainers(fixture.Api.Object, fixture.Registry).Owner("test:b")!;
            Assert.Equal(attachment, remainder.Attachment); Assert.Equal(padding, remainder.Padding);
        }
    }

    /// <summary>Equal-size alignment guides retain their selected edge or center when later content measurements differ.</summary>
    [Theory]
    [InlineData(true, 0, 300, 200, 100, 60, 40, 20)]
    [InlineData(true, 1, 300, 200, 100, 60, 40, 20)]
    [InlineData(true, 2, 300, 200, 100, 60, 40, 20)]
    [InlineData(false, 0, 300, 200, 100, 60, 40, 20)]
    [InlineData(false, 1, 300, 200, 100, 60, 40, 20)]
    [InlineData(false, 2, 300, 200, 100, 60, 40, 20)]
    public void EqualSizeGuideRetainsAlignmentIntent(bool horizontalAxis, int alignment, float x, float y, float width, float height, float smallWidth, float smallHeight)
    {
        var target = new RectangleF(x, y, width, height);
        var candidate = new HudSnapCandidate("align:native", null, HudSnapSide.Left, HudOverlayCrossAlignment.Start, target.Location, 0, target)
            { Boundary = new("hotbar", target), Axis = horizontalAxis ? HudSnapAxis.X : HudSnapAxis.Y, GuideAlignment = (HudOverlayCrossAlignment)alignment };
        var attachment = HudSnapAttachment.FromSnap(candidate, target, target, 1)!;
        var origin = HudContainerAttachment.Origin(attachment, target, new SizeF(smallWidth, smallHeight), 1);
        if (horizontalAxis) Near(target.Left + alignment * .5f * target.Width, origin.X + alignment * .5f * smallWidth);
        else Near(target.Top + alignment * .5f * target.Height, origin.Y + alignment * .5f * smallHeight);
    }

    /// <summary>Native exterior snaps encode opposing attachment and pivot edges for every direction.</summary>
    [Theory]
    [InlineData(0, 0, .5f, 1, .5f, 300, 200, 100, 60, 40, 20)]
    [InlineData(1, 1, .5f, 0, .5f, 300, 200, 100, 60, 40, 20)]
    [InlineData(2, .5f, 0, .5f, 1, 300, 200, 100, 60, 40, 20)]
    [InlineData(3, .5f, 1, .5f, 0, 300, 200, 100, 60, 40, 20)]
    public void ExteriorSnapCapturePreservesAttachment(int side, float anchorX, float anchorY, float pivotX, float pivotY,
        float x, float y, float width, float height, float itemWidth, float itemHeight)
    {
        var target = new RectangleF(x, y, width, height);
        var incoming = new RectangleF(x + anchorX * width - pivotX * itemWidth, y + anchorY * height - pivotY * itemHeight, itemWidth, itemHeight);
        var candidate = new HudSnapCandidate("align:native", null, (HudSnapSide)side, HudOverlayCrossAlignment.Center, incoming.Location, 0, target)
            { Boundary = new("hotbar", target), Exterior = true };
        var attachment = HudSnapAttachment.FromSnap(candidate, incoming, target, 1)!;
        Assert.True(HudContainerAttachment.IsSupported(attachment));
        Assert.Equal(incoming.Location, HudContainerAttachment.Origin(attachment, target, incoming.Size, 1));
        var smaller = new SizeF(itemWidth / 2, itemHeight / 2);
        var origin = HudContainerAttachment.Origin(attachment, target, smaller, 1);
        Near(x + anchorX * width, origin.X + pivotX * smaller.Width);
        Near(y + anchorY * height, origin.Y + pivotY * smaller.Height);
    }

    /// <summary>Measured collapse and growth keep the selected external edge and cross-axis alignment attached.</summary>
    [Theory]
    [InlineData(HudOverlayPoint.LeftMiddle, HudOverlayPoint.RightMiddle, 1, false, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle, 1.5f, true, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.CenterTop, HudOverlayPoint.CenterBottom, 2, false, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.CenterBottom, HudOverlayPoint.CenterTop, 1.5f, true, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.LeftMiddle, HudOverlayPoint.RightMiddle, 2, true, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle, 1, false, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.CenterTop, HudOverlayPoint.CenterBottom, 1.5f, true, 900, 700, 400, 300, 160, 80, 30, 20)]
    [InlineData(HudOverlayPoint.CenterBottom, HudOverlayPoint.CenterTop, 1, false, 900, 700, 400, 300, 160, 80, 30, 20)]
    public void NativeAttachmentFollowsMeasuredContent(HudOverlayPoint anchor, HudOverlayPoint pivot, float scale, bool grouped,
        float viewportWidth, float viewportHeight, float targetX, float targetY, float targetWidth, float targetHeight, float width, float height)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = scale;
            RectangleF? target = new(targetX, targetY, targetWidth, targetHeight);
            var context = new HudOverlayAnchorContext(new Window(viewportWidth, viewportHeight), () => RuntimeEnv.GUIScale, () => target,
                readBoundary: id => id == "hotbar" ? target : null);
            var group = new HudOverlayGroup("test", new("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
            var first = Member("test:a", width, height); var second = Member("test:b", width, height);
            var tree = grouped ? new HudLayoutNode { First = new() { Indicator = first.Key.Id }, Second = new() { Indicator = second.Key.Id } }
                : new HudLayoutNode { Indicator = first.Key.Id };
            var attachment = new HudOverlayPlacement("native:hotbar", anchor, pivot, screenPaddingPixels: 0);
            var checkpoint = new PointF(width, height);
            using var layout = new HudOverlayGroupLayout();
            foreach (bool expanded in new[] { true, false, true })
            {
                context.BeginFrame();
                var measurements = grouped && expanded ? new[] { first, second } : new[] { Member("test:a", expanded ? width * 2 : width, height) };
                layout.Apply(context, group, measurements, checkpoint, tree, 0, attachment);
                AssertAttached(layout, attachment, target.Value, scale);
                Assert.Equal(grouped && expanded ? 2 : 1, layout.Members.Count);
            }
            // A moving native surface and changed scale resolve the same metadata against newly measured content.
            target = new RectangleF(targetX + width, targetY + height, targetWidth, targetHeight);
            RuntimeEnv.GUIScale = scale / 2;
            context.BeginFrame(); layout.Apply(context, group, new[] { first }, checkpoint, tree, 0, attachment);
            AssertAttached(layout, attachment, target.Value, RuntimeEnv.GUIScale);
            // Temporary native unavailability uses the stored origin, without losing the attachment.
            target = null; context.BeginFrame(); layout.Apply(context, group, new[] { first }, checkpoint, tree, 0, attachment);
            Assert.True(layout.Available);
            Near(checkpoint.X * RuntimeEnv.GUIScale, layout.Root.renderX);
            Near(checkpoint.Y * RuntimeEnv.GUIScale, layout.Root.renderY);
            target = new RectangleF(targetX, targetY, targetWidth, targetHeight);
            context.BeginFrame(); layout.Apply(context, group, new[] { first }, checkpoint, tree, 0, attachment);
            AssertAttached(layout, attachment, target.Value, RuntimeEnv.GUIScale);
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Screen attachment respects viewport edges rather than anchoring a group's old top-left after collapse.</summary>
    [Theory]
    [InlineData(HudOverlayPoint.RightTop, 1, 800, 600, 40, 30)]
    [InlineData(HudOverlayPoint.RightBottom, 1.5f, 800, 600, 40, 30)]
    [InlineData(HudOverlayPoint.LeftBottom, 2, 800, 600, 40, 30)]
    [InlineData(HudOverlayPoint.LeftTop, 1.5f, 800, 600, 40, 30)]
    public void ScreenAttachmentTracksViewportAndSize(HudOverlayPoint corner, float scale, float width, float height, float itemWidth, float itemHeight)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = scale;
            var window = new Window(width, height); var context = new HudOverlayAnchorContext(window, () => scale, () => null);
            using var layout = new HudOverlayGroupLayout();
            var group = new HudOverlayGroup("test", new("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
            var attachment = new HudOverlayPlacement("screen", corner, corner, screenPaddingPixels: 0);
            foreach (float factor in new[] { 2f, 1f, 3f })
            {
                window.Resize(width * factor, height * factor); context.BeginFrame();
                layout.Apply(context, group, new[] { Member("test:a", itemWidth * factor, itemHeight) }, new PointF(itemWidth, itemHeight), attachment: attachment);
                AssertAttached(layout, attachment, new RectangleF(0, 0, width * factor, height * factor), scale);
                var member = Assert.Single(layout.Members);
                Assert.Equal(new RectangleF((float)member.Bounds.renderX, (float)member.Bounds.renderY,
                    (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight), member.Clip);
            }
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Both compatible snapshot versions load; a new write carries attachment metadata and is readable after restart.</summary>
    [Theory]
    [InlineData(1, false)] [InlineData(2, true)]
    public void LegacyAndAttachedSnapshotsRoundTrip(int version, bool attached)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a");
        var placement = new HudOverlayPlacement("native:hotbar", HudOverlayPoint.LeftMiddle, HudOverlayPoint.RightMiddle, screenPaddingPixels: 0);
        var container = Record(placement) with { Attachment = attached ? placement : null };
        fixture.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Version = version, Containers = new[] { container } });
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.False(containers.ReadOnly); Assert.Equal(container.Attachment, Assert.Single(containers.Resolved).Attachment);
        Assert.True(containers.Begin(container.Id, null, new PointF(container.X!.Value, container.Y!.Value), new Dictionary<string, PointF>()));
        containers.Move(new PointF(container.X.Value, container.Y.Value), placement);
        Assert.True(containers.Commit(new PointF(container.X.Value, container.Y.Value)));
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.False(restored.ReadOnly); Assert.Equal(placement, Assert.Single(restored.Resolved).Attachment);
        Assert.Equal(2, JsonSerializer.Deserialize<HudLayoutSnapshot>(fixture.Values[HudLayoutStore.SettingKey])!.Version);
    }

    /// <summary>Free dragging clears attachment only on commit; cancellation restores the complete original record.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void FreeDragCommitClearsButCancelRestores(bool grouped, bool cancel)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); if (grouped) fixture.Add("test:b");
        var placement = new HudOverlayPlacement("screen", HudOverlayPoint.RightTop, HudOverlayPoint.RightTop, screenPaddingPixels: 0);
        var record = Record(placement);
        if (grouped) record = record with { Tree = new() { First = record.Tree, Second = new() { Indicator = "test:b" } } };
        fixture.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] { record } });
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); int writes = fixture.Writes;
        Assert.True(containers.Begin(record.Id, null, new PointF(record.X!.Value, record.Y!.Value), new Dictionary<string, PointF>()));
        Assert.Null(Assert.Single(containers.Resolved).Attachment);
        containers.Move(new PointF(record.X.Value * 2, record.Y.Value * 2));
        if (cancel) { containers.Cancel(); Assert.Equal(writes, fixture.Writes); }
        else Assert.True(containers.Commit(new PointF(record.X.Value * 2, record.Y.Value * 2)));
        Assert.Equal(cancel ? placement : null, Assert.Single(containers.Resolved).Attachment);
        Assert.Equal(cancel ? placement : null, Assert.Single(new HudOverlayContainers(fixture.Api.Object, fixture.Registry).Resolved).Attachment);
    }

    /// <summary>Unsupported and malformed attachment metadata remains read-only and is never silently discarded.</summary>
    [Theory]
    [InlineData(1, "native:hotbar")] [InlineData(2, "native:unknown")] [InlineData(3, "native:hotbar")]
    public void InvalidAttachmentSnapshotsStayUntouched(int version, string target)
    {
        using var fixture = new HudLayoutFixture();
        var placement = new HudOverlayPlacement(target, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, screenPaddingPixels: 0);
        string json = JsonSerializer.Serialize(new HudLayoutSnapshot { Version = version, Containers = new[] { Record(placement) } });
        fixture.Values[HudLayoutStore.SettingKey] = json;
        var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object);
        Assert.True(store.ReadOnly); Assert.Equal(json, fixture.Values[HudLayoutStore.SettingKey]); Assert.Equal(0, fixture.Writes);
    }
    /// <summary>Canonical targets and saved native aliases share one frame sample and one supported placement identity.</summary>
    [Theory]
    [InlineData("hotbar", "native:hotbar", "hotbar", 70, 80, 90, 40)]
    [InlineData("saturation", "native:saturationstatbar", "saturationstatbar", 70, 80, 90, 40)]
    [InlineData("health", "native:healthstatbar", "healthstatbar", 70, 80, 90, 40)]
    [InlineData("temporal-stability", "native:temporal-stability", "temporal-stability", 70, 80, 90, 40)]
    public void CanonicalAndLegacyTargetsShareSample(string target, string alias, string boundary, float x, float y, float width, float height)
    {
        int reads = 0;
        var rectangle = new RectangleF(x, y, width, height); bool available = true;
        var context = new HudOverlayAnchorContext(new Window(x + width * 2, y + height * 2), () => 1,
            () => throw new InvalidOperationException("Injected boundary reader must own hotbar availability."),
            readSaturation: () => throw new InvalidOperationException("Injected boundary reader must own saturation availability."),
            readBoundary: id => { Assert.Equal(boundary, id); reads++; return available ? rectangle : null; });
        context.BeginFrame();
        Assert.Equal(rectangle, context.Resolve(alias)); Assert.Equal(rectangle, context.Resolve(target)); Assert.Equal(1, reads);
        Assert.Equal(target, HudOverlayTargets.Canonical(alias)); Assert.Equal(boundary, HudOverlayTargets.Boundary(target));
        Assert.Equal(target, HudOverlayTargets.Target(boundary));
        Assert.True(HudContainerAttachment.IsSupported(new(target, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, screenPaddingPixels: 0)));
        Assert.True(HudContainerAttachment.IsSupported(new(alias, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, screenPaddingPixels: 0)));
        context.BeginFrame(); Assert.Equal(rectangle, context.Resolve(target)); Assert.Equal(2, reads);
        available = false; context.BeginFrame(); Assert.Null(context.Resolve(target)); Assert.Null(context.Resolve(alias)); Assert.Equal(3, reads);
    }
    #endregion

    #region Private
    /// <summary>Uses native rendered bounds to compare the chosen attachment and pivot points.</summary>
    private static void AssertAttached(HudOverlayGroupLayout layout, HudOverlayPlacement placement, RectangleF target, double scale)
    {
        PointF anchor = HudOverlayPoints.Normalized(placement.Attachment), pivot = HudOverlayPoints.Normalized(placement.Pivot);
        Assert.InRange(Math.Abs(target.Left + anchor.X * target.Width + placement.OffsetX * scale - layout.Root.renderX - pivot.X * layout.Root.OuterWidth), 0, scale + .001);
        Assert.InRange(Math.Abs(target.Top + anchor.Y * target.Height + placement.OffsetY * scale - layout.Root.renderY - pivot.Y * layout.Root.OuterHeight), 0, scale + .001);
    }
    /// <summary>Creates a complete independent checkpoint using a supplied attachment contract.</summary>
    private static HudLayoutContainer Record(HudOverlayPlacement placement) => new() { Id = "sample", Key = "vanillaexpanded:hud:custom:sample", X = 30, Y = 40, Movable = true, Attachment = placement, Tree = new() { Indicator = "test:a" } };
    /// <summary>Creates measured input without any presentation activity.</summary>
    private static KeyValuePair<HudOverlayRegistration, SizeF> Member(string id, float width, float height) => new(new(id, Mock.Of<IHudOverlay>(), () => true, "test"), new(width, height));
    /// <summary>Permits only float geometry roundoff.</summary>
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - .001, expected + .001);
    /// <summary>Provides a resizable native viewport without a graphical platform.</summary>
    private sealed class Window : ElementBounds
    {
        private float width, height;
        #region Public API
        /// <summary>Installs the caller-supplied physical viewport.</summary>
        public Window(float width, float height) { IsWindowBounds = true; Resize(width, height); CalcWorldBounds(); }
        /// <summary>Invalidates viewport dimensions like a client resolution change.</summary>
        public void Resize(float width, float height) { this.width = width; this.height = height; requiresrelculation = true; }
        /// <summary>Uses an origin independent of dialog margins.</summary>
        public override double renderX => 0;
        /// <summary>Uses an origin independent of dialog margins.</summary>
        public override double renderY => 0;
        /// <summary>Publishes only native geometry, without platform access.</summary>
        public override void CalcWorldBounds() { absInnerWidth = width; absInnerHeight = height; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}


