using System.Drawing;
using System.Text.Json;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies snapping against actual native measured layouts and committed integer origins.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlaySnappingTests
{
    #region Public API
    /// <summary>Insertion grows toward the incoming instrument while the existing destination stays in place.</summary>
    [Theory]
    [InlineData(1, false)] [InlineData(1.5, false)] [InlineData(2, false)]
    [InlineData(1, true)] [InlineData(1.5, true)]
    public void GroupingExpandsOutwardAndPreservesDestination(float scale, bool wholeGroup)
    {
        foreach (var side in Enum.GetValues<HudSnapSide>())
        foreach (bool cancel in new[] { false, true })
        {
            using var fixture = new Fixture(scale, targetOrigin: new PointF(220, 180), paired: wholeGroup);
            var original = fixture.Layouts["target"].Members.ToDictionary(member => member.Registration.Id,
                member => new PointF((float)member.Bounds.renderX, (float)member.Bounds.renderY));
            fixture.Begin(); fixture.MoveBesideTarget(side); fixture.Update();
            Assert.True(fixture.Snapping.Selected?.Grouping); Assert.Equal(side, fixture.Snapping.Selected!.Side);
            Assert.False(Assert.Single(fixture.Containers.Resolved).Movable);
            var insertion = fixture.Snapping.Insertion;
            fixture.Repack(); var preview = Assert.Single(fixture.Containers.Resolved);
            foreach (var member in fixture.Layouts[preview.Id].Members.Where(member => original.ContainsKey(member.Registration.Id)))
            {
                var expected = original[member.Registration.Id];
                Assert.Equal(expected.X, (float)member.Bounds.renderX, 3);
                Assert.Equal(expected.Y, (float)member.Bounds.renderY, 3);
            }
            if (cancel)
            {
                fixture.Movement.Cancel(); fixture.Repack();
                Assert.Equal(2, fixture.Containers.Resolved.Count);
                Assert.Equal(original["test:b"].X, (float)fixture.Layouts["target"].Members.First().Bounds.renderX, 3);
            }
            else
            {
                var origin = fixture.Containers.Origin(preview)!.Value;
                fixture.Movement.Commit(origin); fixture.Repack();
                var restored = fixture.Restart(); Assert.Equal(preview, Assert.Single(restored.Resolved));
                var incoming = fixture.Layouts[preview.Id].Members.Where(member => !original.ContainsKey(member.Registration.Id))
                    .Select(member => new RectangleF((float)member.Bounds.renderX, (float)member.Bounds.renderY,
                        (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight)).Aggregate(RectangleF.Union);
                Assert.Equal(insertion, incoming);
            }
        }
    }

    /// <summary>A larger incoming instrument preserves the destination at each cross-axis attachment alignment.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void LargerIncomingPreservesDestinationForEveryAlignment(float scale)
    {
        foreach (var side in Enum.GetValues<HudSnapSide>())
        foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
        {
            using var fixture = new Fixture(scale, targetOrigin: new PointF(180, 140), sourceSize: new SizeF(75, 65));
            var original = Assert.Single(fixture.Layouts["target"].Members).Bounds;
            var expected = new PointF((float)original.renderX, (float)original.renderY);
            var root = fixture.Layouts["target"].Root;
            float fraction = alignment switch { HudOverlayCrossAlignment.Center => .5f, HudOverlayCrossAlignment.End => .9f, _ => .1f };
            var pointer = new PointF((float)(root.renderX + root.OuterWidth * fraction), (float)(root.renderY + root.OuterHeight * fraction));
            fixture.Begin(); fixture.MoveBesideTarget(side); fixture.Update(pointer);
            Assert.True(fixture.Snapping.Selected?.Grouping); Assert.Equal(side, fixture.Snapping.Selected!.Side);
            Assert.Equal(alignment, fixture.Snapping.Selected.Alignment);
            fixture.Repack(); var joined = Assert.Single(fixture.Containers.Resolved);
            var target = fixture.Layouts[joined.Id].Members.Single(member => member.Registration.Id == "test:b").Bounds;
            // Center alignment can require a half-GUI-unit origin, while native persistence stores integers.
            Assert.InRange(Math.Abs(expected.X - (float)target.renderX), 0, scale / 2 + .001f);
            Assert.InRange(Math.Abs(expected.Y - (float)target.renderY), 0, scale / 2 + .001f);
            fixture.Movement.Commit(fixture.Containers.Origin(joined)!.Value);
            Assert.Equal(joined, Assert.Single(fixture.Restart().Resolved));
        }
    }

    /// <summary>Insertion beside one nested member grows the destination outer boundary without recentering its root.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void NestedInsertionRetainsOppositeOuterEdge(float scale)
    {
        foreach (var side in Enum.GetValues<HudSnapSide>())
        {
            using var fixture = new Fixture(scale, targetOrigin: new PointF(180, 140), paired: true, individual: true);
            var root = fixture.Layouts["target"].Root;
            var before = new RectangleF((float)root.renderX, (float)root.renderY, (float)root.OuterWidth, (float)root.OuterHeight);
            fixture.Begin(); fixture.Repack(); fixture.MoveBesideTarget(side); fixture.Update();
            Assert.True(fixture.Snapping.Selected?.Grouping); Assert.Equal(side, fixture.Snapping.Selected!.Side);
            Assert.NotNull(fixture.Snapping.Selected.Target!.Member);
            fixture.Repack(); var joined = fixture.Containers.Owner("test:b")!;
            var after = fixture.Layouts[joined.Id].Root;
            double expected = side switch { HudSnapSide.Left => before.Right, HudSnapSide.Above => before.Bottom,
                HudSnapSide.Right => before.Left, _ => before.Top };
            double actual = side switch { HudSnapSide.Left => after.renderX + after.OuterWidth,
                HudSnapSide.Above => after.renderY + after.OuterHeight, HudSnapSide.Right => after.renderX, _ => after.renderY };
            Assert.Equal(expected, actual, 3);
        }
    }

    /// <summary>Detachment validates the reflowed source remainder as well as the freely moved member.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReflowedSourceRemainderCannotOverlapPeerWithoutShift(bool bypass)
    {
        float previousScale = RuntimeEnv.GUIScale; RuntimeEnv.GUIScale = 1;
        using var storage = new HudLayoutFixture();
        var members = new[] { storage.Add("test:a", "source"), storage.Add("test:b", "source"),
            storage.Add("test:c", "source"), storage.Add("test:peer", "peer") };
        var right = new HudLayoutNode { First = new() { Indicator = "test:b" }, Second = new() { Indicator = "test:c" },
            Direction = HudOverlayDirection.Vertical, Alignment = HudOverlayCrossAlignment.Start };
        var source = new HudLayoutContainer { Id = "source", Key = HudOverlayPositionKeys.Group("source"), TemplateId = "source", X = 100, Y = 100,
            Movable = true, Tree = new() { First = new() { Indicator = "test:a" }, Second = right,
                Direction = HudOverlayDirection.Horizontal, Alignment = HudOverlayCrossAlignment.Center } };
        var peer = new HudLayoutContainer { Id = "peer", Key = HudOverlayPositionKeys.Group("peer"), TemplateId = "peer", X = 150, Y = 100,
            Tree = new() { Indicator = "test:peer" } };
        storage.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] { source, peer } });
        var containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
        var anchors = new HudOverlayAnchorContext(new Window(), () => RuntimeEnv.GUIScale, () => null);
        var layouts = new Dictionary<string, HudOverlayGroupLayout>();
        var sizes = new Dictionary<string, SizeF> { ["test:a"] = new(20, 100), ["test:b"] = new(100, 20),
            ["test:c"] = new(100, 20), ["test:peer"] = new(20, 20) };
        // Resolve both the original and detached topology through the runtime's native layout boundary.
        Action repack = () => {
            anchors.BeginFrame();
            foreach (var item in containers.Resolved)
            {
                if (!layouts.TryGetValue(item.Id, out var layout)) layouts[item.Id] = layout = new HudOverlayGroupLayout();
                layout.Apply(anchors, containers.Definition(item), members.Where(member => item.Tree.Leaves().Contains(member.Id))
                    .Select(member => new KeyValuePair<HudOverlayRegistration, SizeF>(member, sizes[member.Id])).ToArray(), containers.Origin(item), item.Tree, item.Padding);
            }
        };
        try
        {
            repack(); var original = layouts["source"].Members;
            var peerClip = Assert.Single(layouts["peer"].Members).Clip;
            Assert.All(original, member => Assert.False(member.Clip.IntersectsWith(peerClip)));
            var movement = containers.Get("source", "test:a"); movement.ContainerOrigin = new PointF(100, 100);
            movement.MemberOrigins = original.ToDictionary(member => member.Registration.Id,
                member => new PointF((float)member.Bounds.renderX, (float)member.Bounds.renderY));
            movement.Begin(movement.MemberOrigins["test:a"], PointF.Empty, 1); movement.Move(new PointF(300, 200)); repack();
            Assert.Contains(layouts["source"].Members, member => member.Clip.IntersectsWith(peerClip));
            var snapping = new HudOverlaySnapping(containers);
            snapping.Update(movement, new PointF(400, 300), bypass, 1, new RectangleF(0, 0, 800, 600), layouts, _ => true);
            Assert.Equal(bypass, snapping.DropAllowed); Assert.Null(snapping.Selected); Assert.Equal(0, storage.Writes);
            movement.Cancel(); Assert.Equal(source.Tree, containers.Owner("test:b")!.Tree);
        }
        finally { foreach (var layout in layouts.Values) layout.Dispose(); RuntimeEnv.GUIScale = previousScale; }
    }

    /// <summary>Physical acquisition and integer preview placement use the same relationships at several GUI scales.</summary>
    [Theory]
    [InlineData(1, false)] [InlineData(1.5, false)] [InlineData(2, false)] [InlineData(1, true)] [InlineData(1.5, true)]
    public void GroupingUsesMeasuredGeometryAcrossScales(float scale, bool edge)
    {
        using var fixture = new Fixture(scale, targetOrigin: edge ? PointF.Empty : null); fixture.Begin(); fixture.MoveBesideTarget();
        fixture.Update(); Assert.True(fixture.Snapping.Selected?.Grouping); Assert.True(fixture.Snapping.DropAllowed);
        var preview = fixture.Snapping.Insertion; Assert.False(preview.IsEmpty); Assert.False(preview.IntersectsWith(fixture.Snapping.Destination));
        var joined = Assert.Single(fixture.Containers.Resolved); fixture.Movement.Commit(fixture.Containers.Origin(joined)!.Value);
        fixture.Repack(); var actual = fixture.Layouts[joined.Id].Members.Single(value => value.Registration.Id == "test:a");
        Assert.Equal(preview.X, (float)actual.Bounds.renderX, 3); Assert.Equal(preview.Y, (float)actual.Bounds.renderY, 3);
        Assert.Equal(preview.Width, (float)actual.Bounds.OuterWidth, 3); Assert.Equal(preview.Height, (float)actual.Bounds.OuterHeight, 3);
    }

    /// <summary>A neighboring third item rejects a grouping layout that would overlap it.</summary>
    [Fact]
    public void OverlappingThirdPartyRejectsGrouping()
    {
        using var fixture = new Fixture(1); fixture.AddObstacle(); fixture.Begin(); fixture.MoveBesideTarget(); fixture.Update();
        Assert.False(fixture.Snapping.Selected?.Grouping == true); Assert.Equal(3, fixture.Containers.Resolved.Count);
    }

    /// <summary>An oversized combined arrangement cannot commit a grouping preview that clips the viewport.</summary>
    [Fact]
    public void ClippedCombinedGeometryRejectsGrouping()
    {
        using var fixture = new Fixture(1, new SizeF(450, 350)); fixture.Begin(); fixture.MoveBesideTarget(); fixture.Update();
        Assert.False(fixture.Snapping.Selected?.Grouping == true); Assert.Equal(2, fixture.Containers.Resolved.Count);
    }

    /// <summary>Matching edges remain alignment only after native rounding.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)]
    public void AlignmentOnlyDropRetainsSeparateMembership(float scale)
    {
        using var fixture = new Fixture(scale); fixture.Begin();
        var target = fixture.Layouts["target"].Root;
        fixture.Movement.Move(new PointF((float)target.renderX + 2, (float)(target.renderY + target.OuterHeight + HudSnapGeometry.Release - .1))); fixture.Repack(); fixture.Update();
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected); Assert.False(selected.Grouping);
        Assert.True(fixture.Snapping.DropAllowed);
        fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack(); Assert.Equal(2, fixture.Containers.Resolved.Count);
        var committed = fixture.Layouts["source"].Root;
        Assert.Equal(selected.AlignsX ? selected.Origin.X : selected.Origin.Y, (float)(selected.AlignsX ? committed.renderX : committed.renderY), 3);
    }

    /// <summary>Edge alignment cannot advertise a snapped placement over another visible indicator.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)]
    public void OverlappingAlignmentIsRejected(float scale)
    {
        using var fixture = new Fixture(scale); fixture.Begin();
        var target = fixture.Layouts["target"].Root;
        fixture.Movement.Move(new PointF((float)target.renderX + 2, (float)target.renderY + 2)); fixture.Repack(); fixture.Update();
        Assert.Null(fixture.Snapping.Selected);
        Assert.Equal(2, fixture.Containers.Resolved.Count);
    }

    /// <summary>Free placement cannot accept an overlapping drop unless the user explicitly holds the bypass modifier.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RawOverlappingPlacementRequiresShift(bool bypass)
    {
        using var fixture = new Fixture(1); fixture.Begin();
        var target = fixture.Layouts["target"].Root;
        var pointer = new PointF((float)target.renderX + 13, (float)target.renderY + 13);
        fixture.Movement.Move(pointer); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, pointer, bypass, 1, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true);
        Assert.Null(fixture.Snapping.Selected);
        Assert.Equal(bypass, fixture.Snapping.DropAllowed);
    }

    /// <summary>A fractional physical center retains alignment and advertises the resolved native position.</summary>
    [Theory]
    [InlineData(1.25f)] [InlineData(1.5f)] [InlineData(1.75f)]
    public void FractionalCenterKeepsResolvedAlignmentGuide(float scale)
    {
        using var fixture = new Fixture(scale); fixture.Begin();
        var viewport = new RectangleF(0, 0, 799, 600); var source = fixture.Layouts["source"].Root;
        float center = (viewport.Width - (float)source.OuterWidth) / 2;
        fixture.Movement.Move(new PointF(center + 2, 50)); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, new PointF(center + 2, 50), false, scale, viewport, fixture.Layouts, _ => true);
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected); Assert.False(selected.Grouping); Assert.True(selected.AlignsX);
        fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack();
        var actual = fixture.Layouts["source"].Root;
        Assert.Equal(selected.Origin.X, (float)actual.renderX, 3);
        Assert.InRange(Math.Abs((float)actual.renderX - center), 0, scale);
        Assert.Equal((float)(actual.renderX + actual.OuterWidth / 2), selected.Guide.X, 3);
        Assert.Equal(2, fixture.Containers.Resolved.Count);
    }

    /// <summary>Screen-edge guides honor the same reachable boundary as native free-placement layout.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)]
    public void ScreenEdgeGuideMatchesNativeClampedOrigin(float scale)
    {
        using var fixture = new Fixture(scale, targetOrigin: PointF.Empty); fixture.Begin();
        float nativeEdge = (float)fixture.Layouts["target"].Root.renderX;
        fixture.Movement.Move(new PointF(nativeEdge + 2, 100 * scale)); fixture.Repack(); fixture.Update();
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected); Assert.False(selected.Grouping); Assert.True(selected.AlignsX);
        fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack();
        Assert.Equal(nativeEdge, (float)fixture.Layouts["source"].Root.renderX, 3);
        Assert.Equal(selected.Origin.X, (float)fixture.Layouts["source"].Root.renderX, 3);
    }
    #endregion

    #region Private
    /// <summary>Owns synthetic registrations and native bounds while borrowing no feature rendering resources.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly float previousScale = RuntimeEnv.GUIScale;
        private readonly HudLayoutFixture storage = new();
        private readonly Window window = new();
        private readonly HudOverlayAnchorContext anchors;
        private readonly List<HudOverlayRegistration> members = new();
        private readonly SizeF size;
        private readonly SizeF sourceSize;
        public readonly HudOverlayContainers Containers;
        public readonly HudOverlayGroupMovement Movement;
        public readonly HudOverlaySnapping Snapping;
        public readonly Dictionary<string, HudOverlayGroupLayout> Layouts = new();
        #region Public API
        /// <summary>Builds caller-defined instrument geometry at a selected native scale.</summary>
        public Fixture(float scale, SizeF? measured = null, PointF? targetOrigin = null, bool paired = false, SizeF? sourceSize = null, bool individual = false)
        {
            RuntimeEnv.GUIScale = scale; size = measured ?? new SizeF(40, 30);
            this.sourceSize = sourceSize ?? size;
            members.Add(storage.Add("test:a", "source")); members.Add(storage.Add("test:b", "target"));
            if (paired) { members.Add(storage.Add("test:a2", "source")); members.Add(storage.Add("test:b2", "target")); }
            Containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
            Containers.SetMovable("source", true, new PointF(30, 40)); Containers.SetMovable("target", false, targetOrigin ?? new PointF(200, 120));
            anchors = new HudOverlayAnchorContext(window, () => RuntimeEnv.GUIScale, () => null);
            Movement = Containers.Get("source", individual ? "test:a" : null); Snapping = new HudOverlaySnapping(Containers); Repack();
        }
        /// <summary>Captures source movement without native titlebar hit testing.</summary>
        public void Begin() => Movement.Begin(new PointF(30, 40), new PointF(30 * RuntimeEnv.GUIScale, 40 * RuntimeEnv.GUIScale), RuntimeEnv.GUIScale);
        /// <summary>Places raw source bounds immediately beside the measured target.</summary>
        public void MoveBesideTarget(HudSnapSide side = HudSnapSide.Right)
        {
            var root = Movement.IndicatorId == null ? Layouts["target"].Root :
                Layouts["target"].Members.Single(member => member.Registration.Id == "test:b").Bounds;
            float scale = RuntimeEnv.GUIScale;
            var source = Layouts[Movement.Id].Root;
            float gap = (float)(Containers.Definition(Containers.Owner("test:b")!).Packing.Gap * scale);
            var position = side switch {
                HudSnapSide.Left => new PointF((float)(root.renderX - source.OuterWidth) - gap, (float)root.renderY),
                HudSnapSide.Above => new PointF((float)root.renderX, (float)(root.renderY - source.OuterHeight) - gap),
                HudSnapSide.Below => new PointF((float)root.renderX, (float)(root.renderY + root.OuterHeight) + gap),
                _ => new PointF((float)(root.renderX + root.OuterWidth) + gap, (float)root.renderY) };
            Movement.Move(position); Repack();
        }
        /// <summary>Reloads the authoritative committed snapshot without changing its native mirrors.</summary>
        public HudOverlayContainers Restart() => new(storage.Api.Object, storage.Registry);
        /// <summary>Calculates feedback against native roots and drawable instrument measurements.</summary>
        public void Update(PointF? pointer = null) => Snapping.Update(Movement, pointer ?? new PointF((float)Layouts["target"].Root.renderX, (float)Layouts["target"].Root.renderY), false,
            RuntimeEnv.GUIScale, new RectangleF(0, 0, 800, 600), Layouts, _ => true);
        /// <summary>Adds a separate measured obstacle in the proposed incoming footprint.</summary>
        public void AddObstacle()
        {
            var target = Layouts["target"].Root; members.Add(storage.Add("test:obstacle", "obstacle")); Containers.Synchronize();
            Containers.SetMovable("obstacle", false, new PointF((float)(target.renderX + target.OuterWidth + 6), (float)target.renderY)); Repack();
        }
        /// <summary>Applies effective container trees using the same native layout boundary as the runtime.</summary>
        public void Repack()
        {
            anchors.BeginFrame();
            foreach (string id in Layouts.Keys.Where(id => Containers.Resolved.All(value => value.Id != id)).ToArray()) { Layouts[id].Dispose(); Layouts.Remove(id); }
            foreach (var container in Containers.Resolved)
            {
                if (!Layouts.TryGetValue(container.Id, out var layout)) Layouts[container.Id] = layout = new HudOverlayGroupLayout();
                layout.Apply(anchors, Containers.Definition(container), members.Where(value => container.Tree.Leaves().Contains(value.Id)).Select(value => new KeyValuePair<HudOverlayRegistration, SizeF>(value, value.Id.StartsWith("test:a", StringComparison.Ordinal) ? sourceSize : size)).ToArray(), Containers.Origin(container), container.Tree, container.Padding);
            }

        }
        /// <summary>Releases native layout roots and restores global GUI scaling.</summary>
        public void Dispose() { Movement.Cancel(); foreach (var layout in Layouts.Values) layout.Dispose(); storage.Dispose(); RuntimeEnv.GUIScale = previousScale; }
        #endregion
    }
    /// <summary>Provides a deterministic native framebuffer parent.</summary>
    private sealed class Window : ElementBounds
    {
        #region Public API
        /// <summary>Marks the geometry root as a viewport.</summary>
        public Window() { IsWindowBounds = true; CalcWorldBounds(); }
        /// <summary>Provides the framebuffer origin.</summary>
        public override double renderX => 0;
        /// <summary>Provides the framebuffer origin.</summary>
        public override double renderY => 0;
        /// <summary>Supplies caller-independent screen extents without platform access.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 800; absInnerHeight = 600; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}
