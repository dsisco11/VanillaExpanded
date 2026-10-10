using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Serializes native GUI globals used by headless bounds integration.</summary>
[CollectionDefinition("HudOverlayGeometry", DisableParallelization = true)]
public sealed class HudOverlayGeometryCollection { }

/// <summary>Exercises installed native bounds rather than a parallel layout model.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayLayoutTests
{
    #region Public API
    #region Attachment
    /// <summary>Covers nine native screen alignments, nine pivots, offsets, pixel margins, and scaling exactly once.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ScreenAlignmentAndPivotsUseNativeMargins(double scale)
    {
        float old = RuntimeEnv.GUIScale;
        int left = GuiStyle.LeftDialogMargin, right = GuiStyle.RightDialogMargin;
        try
        {
            RuntimeEnv.GUIScale = (float)scale;
            GuiStyle.LeftDialogMargin = 17;
            GuiStyle.RightDialogMargin = 23;
            var window = new Window(2000, 1600);
            var context = new HudOverlayAnchorContext(window, () => scale, () => null);
            context.BeginFrame();
            foreach (HudOverlayPoint anchor in Enum.GetValues<HudOverlayPoint>())
            foreach (HudOverlayPoint pivot in Enum.GetValues<HudOverlayPoint>())
            {
                using var layout = new HudOverlayGroupLayout();
                var group = Group(new("screen", anchor, pivot, 3, 4));
                var member = Member("mod:a", 80, 40);
                layout.Apply(context, group, new[] { member });
                PointF a = new((int)anchor % 3 * .5f, (int)anchor / 3 * .5f), p = new((int)pivot % 3 * .5f, (int)pivot / 3 * .5f);
                double width = 88 * scale, height = 48 * scale;
                double nativeX = a.X == 0 ? 17 : a.X == 1 ? 2000 - width - 23 : (2000 - width) / 2;
                double nativeY = a.Y * (1600 - height);
                double x = nativeX + scale * (3 + (1 - 2 * a.X) * 12) + (a.X - p.X) * width;
                double y = nativeY + scale * (4 + (1 - 2 * a.Y) * 12) + (a.Y - p.Y) * height;
                Near(Math.Clamp(x, context.SafeRectangle.Left, context.SafeRectangle.Right - width), layout.Root.renderX);
                Near(Math.Clamp(y, context.SafeRectangle.Top, context.SafeRectangle.Bottom - height), layout.Root.renderY);
                EnumDialogArea[] expected = { EnumDialogArea.LeftTop, EnumDialogArea.CenterTop, EnumDialogArea.RightTop, EnumDialogArea.LeftMiddle, EnumDialogArea.CenterMiddle, EnumDialogArea.RightMiddle, EnumDialogArea.LeftBottom, EnumDialogArea.CenterBottom, EnumDialogArea.RightBottom };
                Assert.Equal(expected[(int)anchor], layout.Root.Alignment);
                Near(80 * scale, layout.Members[0].Bounds.OuterWidth);
                Near(layout.Root.renderX + 4 * scale, layout.Members[0].Bounds.renderX);
                Assert.Equal(0, layout.Root.fixedPaddingX);
            }
        }
        finally { RuntimeEnv.GUIScale = old; GuiStyle.LeftDialogMargin = left; GuiStyle.RightDialogMargin = right; }
    }

    /// <summary>Named pixel attachment respects all pivots, positive offsets and parent render offsets.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NamedAttachmentSubtractsParentRenderOrigin(double scale)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = (float)scale;
            var window = new Window(2000, 1600) { renderOffsetX = 7, renderOffsetY = 11 };
            var target = new RectangleF(600, 500, 120, 60);
            var context = new HudOverlayAnchorContext(window, () => scale, () => target);
            context.BeginFrame();
            foreach (HudOverlayPoint anchor in Enum.GetValues<HudOverlayPoint>())
            foreach (HudOverlayPoint pivot in Enum.GetValues<HudOverlayPoint>())
            {
                using var layout = new HudOverlayGroupLayout();
                layout.Apply(context, Group(new("hotbar", anchor, pivot, 3, 4)), new[] { Member("mod:a", 80, 40) });
                PointF a = HudOverlayPoints.Normalized(anchor), p = HudOverlayPoints.Normalized(pivot);
                Near(target.X + a.X * target.Width - p.X * 88 * scale + 3 * scale, layout.Root.renderX);
                Near(target.Y + a.Y * target.Height - p.Y * 48 * scale + 4 * scale, layout.Root.renderY);
                Assert.Equal(EnumDialogArea.None, layout.Root.Alignment);
            }
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    #endregion
    #region Packing and fit
    /// <summary>Stable packing closes gaps after hiding and returns overflow members after viewport growth.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void OrderedPrefixClampingAndRecoveryPreserveSpacing(int directionValue)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            var direction = (HudOverlayDirection)directionValue;
            var window = new Window(130, 130);
            var context = new HudOverlayAnchorContext(window, () => 1, () => new RectangleF(125, 125, 5, 5));
            var group = Group(new("hotbar", HudOverlayPoint.RightBottom, HudOverlayPoint.LeftTop), new(direction));
            var a = Member("mod:a", 40, 40); var b = Member("mod:b", 40, 40); var c = Member("mod:c", 40, 40);
            using var layout = new HudOverlayGroupLayout();
            context.BeginFrame();
            layout.Apply(context, group, new[] { c, a, b });
            Assert.Equal(new[] { "mod:a", "mod:b" }, layout.Members.Select(m => m.Registration.Id));
            Near(direction == HudOverlayDirection.Horizontal ? 94 : 48, layout.Root.OuterWidth);
            Near(direction == HudOverlayDirection.Vertical ? 94 : 48, layout.Root.OuterHeight);
            var first = layout.Members[0].Bounds; var second = layout.Members[1].Bounds;
            Near(46, direction == HudOverlayDirection.Horizontal ? second.renderX - first.renderX : second.renderY - first.renderY);
            Assert.False(layout.Apply(context, group, new[] { a, b, c }));
            layout.Apply(context, group, new[] { a, c });
            Assert.Equal(new[] { "mod:a", "mod:c" }, layout.Members.Select(m => m.Registration.Id));
            window.Resize(250, 250);
            context.BeginFrame();
            layout.Apply(context, group, new[] { a, b, c });
            Assert.Equal(3, layout.Members.Count);
            Assert.All(layout.Members, m => Assert.True(context.SafeRectangle.Contains(m.Clip)));
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Oversized first content remains full size with a bounded clip; later content stays omitted.</summary>
    [Fact]
    public void OversizedFirstMemberIsClippedWithoutScaling()
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            var context = new HudOverlayAnchorContext(new Window(100, 100), () => 1, () => null);
            context.BeginFrame();
            using var layout = new HudOverlayGroupLayout();
            layout.Apply(context, Group(new("screen", HudOverlayPoint.CenterMiddle, HudOverlayPoint.CenterMiddle)),
                new[] { Member("mod:a", 200, 200), Member("mod:b", 1, 1) });
            var member = Assert.Single(layout.Members);
            Near(200, member.Bounds.OuterWidth);
            Near(12, layout.Root.renderX);
            Assert.True(context.SafeRectangle.Contains(member.Clip));
            Assert.Equal(new RectangleF(16, 16, 72, 72), member.Clip);
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    #endregion
    #region Invalidation and validation
    /// <summary>Native margins, target changes, hiding, measured sizes and session parents invalidate without preparing resources.</summary>
    [Fact]
    public void InvalidationAndSessionReplacementReuseNativeHierarchy()
    {
        float old = RuntimeEnv.GUIScale;
        int oldMargin = GuiStyle.LeftDialogMargin;
        try
        {
            RuntimeEnv.GUIScale = 1;
            RectangleF? rectangle = new(200, 200, 50, 50);
            var window = new Window(800, 600);
            var context = new HudOverlayAnchorContext(window, () => 1, () => rectangle);
            var group = Group(new("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle));
            var member = Member("mod:a", 30, 20);
            using var layout = new HudOverlayGroupLayout();
            context.BeginFrame(); layout.Apply(context, group, new[] { member });
            ElementBounds child = layout.Members[0].Bounds;
            context.BeginFrame(); Assert.False(layout.Apply(context, group, new[] { member }));
            rectangle = new(210, 200, 50, 50);
            context.BeginFrame(); Assert.True(layout.Apply(context, group, new[] { member }));
            Assert.Same(child, layout.Members[0].Bounds);
            rectangle = null;
            context.BeginFrame(); layout.Apply(context, group, new[] { member });
            Assert.False(layout.Available); Assert.Empty(layout.Members);
            rectangle = new(210, 200, 50, 50);
            context.BeginFrame(); layout.Apply(context, group, new[] { member });
            Assert.True(layout.Available); Assert.Same(child, layout.Members[0].Bounds);
            GuiStyle.LeftDialogMargin += 3;
            context.BeginFrame(); Assert.False(layout.Apply(context, group, new[] { member }));
            var screen = Group(new("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
            layout.Apply(context, screen, new[] { member });
            double x = layout.Root.renderX;
            GuiStyle.LeftDialogMargin += 3;
            context.BeginFrame(); Assert.True(layout.Apply(context, screen, new[] { member }));
            Near(x + 3, layout.Root.renderX);
            var replacement = new Window(900, 700);
            var next = new HudOverlayAnchorContext(replacement, () => 1, () => null);
            next.BeginFrame(); layout.Apply(next, screen, new[] { member });
            Assert.Same(replacement, layout.Root.ParentBounds);
            Assert.DoesNotContain(layout.Root, window.ChildBounds);
            Assert.Contains(layout.Root, replacement.ChildBounds);
            layout.Dispose(); layout.Dispose();
            Assert.DoesNotContain(layout.Root, replacement.ChildBounds);
        }
        finally { RuntimeEnv.GUIScale = old; GuiStyle.LeftDialogMargin = oldMargin; }
    }

    /// <summary>Shared frame reads are coalesced while hidden frames avoid named-target queries entirely.</summary>
    [Fact]
    public void AnchorFramesShareReadsAndDoNotReadHiddenTargets()
    {
        int reads = 0;
        var context = new HudOverlayAnchorContext(new Window(400, 300), () => 1, () => { reads++; return new RectangleF(10, 20, 30, 40); });
        Assert.Throws<InvalidOperationException>(() => context.Resolve("hotbar"));
        context.BeginFrame();
        Assert.Equal(context.Resolve("hotbar"), context.Resolve("hotbar")); Assert.Equal(1, reads);
        long revision = context.Revision;
        context.BeginFrame(); context.Resolve("hotbar"); Assert.Equal(2, reads); Assert.Equal(revision, context.Revision);
        context.BeginFrame(false); Assert.Null(context.Resolve("hotbar")); Assert.Equal(2, reads);
        context.BeginFrame(); Assert.NotNull(context.Resolve("hotbar")); Assert.Equal(3, reads);
        Assert.Null(context.Resolve("unknown"));
    }

    /// <summary>Cross-axis alignment and deliberate group overlap preserve independent native hierarchies.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public void CrossAlignmentAndIndependentGroups(int directionValue, int alignmentValue)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            var context = new HudOverlayAnchorContext(new Window(800, 600), () => 1, () => null);
            context.BeginFrame();
            var direction = (HudOverlayDirection)directionValue;
            var group = Group(new("screen", HudOverlayPoint.CenterMiddle, HudOverlayPoint.CenterMiddle),
                new(direction, crossAlignment: (HudOverlayCrossAlignment)alignmentValue));
            var first = Member("mod:a", 60, 60); var second = Member("mod:b", 20, 20);
            using var a = new HudOverlayGroupLayout(); using var b = new HudOverlayGroupLayout();
            a.Apply(context, group, new[] { first, second }); b.Apply(context, group, new[] { first, second });
            Near(4 + 20 * alignmentValue, direction == HudOverlayDirection.Horizontal
                ? a.Members[1].Bounds.fixedY : a.Members[1].Bounds.fixedX);
            Assert.NotSame(a.Root, b.Root); Assert.NotSame(a.Members[0].Bounds, b.Members[0].Bounds);
            Near(a.Root.renderX, b.Root.renderX); Near(a.Root.renderY, b.Root.renderY);
            Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<HudOverlayMemberLayout>>(a.Members);
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Existing roots react to scaling, measurements, ordering and screen-to-named transitions without stale native margins.</summary>
    [Fact]
    public void LiveScaleMeasurementOrderAndAlignmentChanges()
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            double scale = 1;
            var context = new HudOverlayAnchorContext(new Window(2000, 1600), () => scale, () => new RectangleF(700, 600, 100, 50));
            using var layout = new HudOverlayGroupLayout();
            var a = Member("mod:a", 20, 10); var b = Member("mod:b", 30, 10);
            var screen = Group(new("screen", HudOverlayPoint.RightBottom, HudOverlayPoint.RightBottom));
            context.BeginFrame(); layout.Apply(context, screen, new[] { a, b });
            ElementBounds child = layout.Members[0].Bounds;
            RuntimeEnv.GUIScale = 2; scale = 2;
            context.BeginFrame(); Assert.True(layout.Apply(context, screen, new[] { a, b }));
            Assert.Same(child, layout.Members[0].Bounds); Near(40, child.OuterWidth);
            var changed = new KeyValuePair<HudOverlayRegistration, SizeF>(a.Key, new SizeF(45, 15));
            Assert.True(layout.Apply(context, screen, new[] { changed, b })); Near(90, child.OuterWidth);
            var reordered = new KeyValuePair<HudOverlayRegistration, SizeF>(new HudOverlayRegistration("mod:b", b.Key.Overlay,
                () => true, "group", order: -1), b.Value);
            Assert.True(layout.Apply(context, screen, new[] { changed, reordered }));
            Assert.Equal(new[] { "mod:b", "mod:a" }, layout.Members.Select(member => member.Registration.Id));
            var named = Group(new("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle, 12, 0));
            layout.Apply(context, named, new[] { changed });
            Near(824, layout.Root.renderX);
            Near(625 - 23, layout.Root.renderY);
            Assert.Equal(0, layout.Root.absMarginX); Assert.Equal(0, layout.Root.absMarginY);
            child.renderOffsetX = 5; child.renderOffsetY = 7;
            Near(layout.Root.renderX + 8 + 5, child.renderX); Near(layout.Root.renderY + 8 + 7, child.renderY);
            Assert.True(layout.Apply(context, named, new[] { changed }));
            Assert.Equal(RectangleF.Intersect(context.SafeRectangle, new RectangleF((float)child.renderX, (float)child.renderY,
                (float)child.OuterWidth, (float)child.OuterHeight)), layout.Members[0].Clip);
            Assert.False(layout.Apply(context, named, new[] { changed }));
            layout.Root.renderOffsetX = 4000;
            Assert.True(layout.Apply(context, named, new[] { changed }));
            Near(context.SafeRectangle.Right - layout.Root.OuterWidth, layout.Root.renderX);
            Assert.Equal(RectangleF.Intersect(context.SafeRectangle, new RectangleF((float)child.renderX, (float)child.renderY,
                (float)child.OuterWidth, (float)child.OuterHeight)), layout.Members[0].Clip);
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Removing content while its target is unavailable releases native child registration references.</summary>
    [Fact]
    public void RemovalWhileAnchorUnavailableDetachesChildren()
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = 1;
            RectangleF? target = new(100, 100, 50, 50);
            var context = new HudOverlayAnchorContext(new Window(800, 600), () => 1, () => target);
            var group = Group(new("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle));
            using var layout = new HudOverlayGroupLayout();
            context.BeginFrame(); layout.Apply(context, group, new[] { Member("mod:a", 20, 20) });
            Assert.Single(layout.Root.ChildBounds);
            target = null; context.BeginFrame();
            layout.Apply(context, group, new[] { Member("mod:a", 25, 25) });
            Assert.Empty(layout.Root.ChildBounds); Assert.Empty(layout.Members);
            target = new(100, 100, 50, 50); context.BeginFrame();
            layout.Apply(context, group, new[] { Member("mod:a", 25, 25) });
            Assert.Single(layout.Root.ChildBounds);
            target = null; context.BeginFrame();
            layout.Apply(context, group, Array.Empty<KeyValuePair<HudOverlayRegistration, SizeF>>());
            Assert.Empty(layout.Root.ChildBounds); Assert.Empty(layout.Members);
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Aligns visible arrow content with the meter left edge and leaves a scaled four-unit gap above it.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SaturationPlacementAlignsContentAboveMeter(double scale)
    {
        float old = RuntimeEnv.GUIScale;
        try
        {
            RuntimeEnv.GUIScale = (float)scale;
            var target = new RectangleF(800, 700, 200, 10);
            int reads = 0;
            var context = new HudOverlayAnchorContext(new Window(1800, 1000), () => scale, () => null,
                readSaturation: () => { reads++; return target; });
            context.BeginFrame();
            var group = Group(new(HudOverlayAnchorContext.SaturationTargetId, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftBottom, -4, 0));
            using var layout = new HudOverlayGroupLayout();
            layout.Apply(context, group, new[] { Member("mod:arrow", 40, 20) });
            var member = Assert.Single(layout.Members);
            Near(target.Left, member.Bounds.renderX);
            Near(target.Top - 4 * scale, member.Bounds.renderY + member.Bounds.OuterHeight);
            context.Resolve(HudOverlayAnchorContext.SaturationTargetId);
            Assert.Equal(1, reads);
        }
        finally { RuntimeEnv.GUIScale = old; }
    }

    /// <summary>Invalid native measurements fail before creating child geometry.</summary>
    [Theory]
    [InlineData(float.NaN, 1)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void InvalidMeasurementsAreRejected(float width, float height)
    {
        var context = new HudOverlayAnchorContext(new Window(400, 300), () => 1, () => null);
        context.BeginFrame();
        using var layout = new HudOverlayGroupLayout();
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Apply(context,
            Group(new("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)), new[] { Member("mod:a", width, height) }));
        Assert.Empty(layout.Root.ChildBounds);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates fixed group metadata without engine or feature side effects.</summary>
    private static HudOverlayGroup Group(HudOverlayPlacement placement, HudOverlayPacking? packing = null) => new("group", placement, packing);
    /// <summary>Creates a measured registration whose strict overlay must never be sampled or prepared by layout.</summary>
    private static KeyValuePair<HudOverlayRegistration, SizeF> Member(string id, float width, float height) =>
        new(new HudOverlayRegistration(id, new Mock<IHudOverlay>(MockBehavior.Strict).Object, () => true, "group"), new SizeF(width, height));
    /// <summary>Compares native double positions with a small float-input tolerance.</summary>
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - .001, expected + .001);

    /// <summary>A native window parent with configurable pixel dimensions and real screen-margin semantics.</summary>
    private sealed class Window : ElementBounds
    {
        private double width, height;
        #region Public API
        /// <summary>Initializes a headless native window boundary without accessing a graphics platform.</summary>
        public Window(double width, double height) { IsWindowBounds = true; Resize(width, height); }
        /// <summary>Marks native dimensions dirty, mirroring installed window resize behavior.</summary>
        public void Resize(double width, double height) { this.width = width; this.height = height; requiresrelculation = true; }
        /// <summary>Returns the window render origin including native pixel offsets.</summary>
        public override double renderX => renderOffsetX;
        /// <summary>Returns the window render origin including native pixel offsets.</summary>
        public override double renderY => renderOffsetY;
        /// <summary>Refreshes pixel dimensions and leaves dependent groups to recalculate through their owning layout.</summary>
        public override void CalcWorldBounds() { absInnerWidth = width; absInnerHeight = height; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}
