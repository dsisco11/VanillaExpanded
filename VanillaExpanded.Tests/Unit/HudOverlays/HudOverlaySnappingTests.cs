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
    /// <summary>An empty reserved remainder still validates an integration that returns before commit.</summary>
    [Theory]
    [InlineData(false, 100, 100, 20, 20, 100, 30, 130, 100, 300, 200)]
    [InlineData(true, 100, 100, 20, 20, 100, 30, 130, 100, 300, 200)]
    public void ReturningOnlySourceMemberStillValidatesFinalOverlap(bool bypass, float sourceX, float sourceY, float width, float height,
        float restoredWidth, float restoredHeight, float peerX, float peerY, float dragX, float dragY)
    {
        float previousScale = RuntimeEnv.GUIScale; RuntimeEnv.GUIScale = 1;
        using var storage = new HudLayoutFixture();
        var members = new List<HudOverlayRegistration> { storage.Add("test:a", "source"), storage.Add("test:peer", "peer") };
        storage.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] {
            new HudLayoutContainer { Id = "source", Key = HudOverlayPositionKeys.Group("source"), TemplateId = "source", X = (int)sourceX, Y = (int)sourceY, Movable = true,
                Tree = new() { First = new() { Indicator = "test:a" }, Second = new() { Indicator = "missing:integration" } } },
            new HudLayoutContainer { Id = "peer", Key = HudOverlayPositionKeys.Group("peer"), TemplateId = "peer", X = (int)peerX, Y = (int)peerY, Tree = new() { Indicator = "test:peer" } } } });
        var containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
        var anchors = new HudOverlayAnchorContext(new Window(), () => 1, () => null);
        var layouts = new Dictionary<string, HudOverlayGroupLayout>();
        var movement = containers.Get("source", "test:a");
        try
        {
            Repack();
            movement.SourceGeometry = HudOverlaySourceReservation.Capture(layouts["source"], PointF.Empty, 1);
            movement.ContainerOrigin = new PointF(sourceX, sourceY);
            movement.MemberOrigins = new Dictionary<string, PointF> { ["test:a"] = new(sourceX, sourceY) };
            movement.Begin(new PointF(sourceX, sourceY), PointF.Empty, 1); movement.Move(new PointF(dragX, dragY)); Repack();
            members.Add(storage.Add("missing:integration", "source")); containers.Synchronize(); Repack();
            var remainder = layouts[containers.Owner("missing:integration")!.Id];
            Assert.Empty(remainder.Members); Assert.Single(remainder.Measurements);
            var snapping = new HudOverlaySnapping(containers);
            snapping.Update(movement, new PointF(sourceX + dragX, sourceY + dragY), bypass, 1, new RectangleF(0, 0, 800, 600), layouts, _ => true);
            Assert.Equal(bypass, snapping.DropAllowed); Assert.Null(snapping.Selected);
        }
        finally { movement.Cancel(); foreach (var layout in layouts.Values) layout.Dispose(); RuntimeEnv.GUIScale = previousScale; }

        /// <summary>Publishes current integration measurements while forwarding the source reservation.</summary>
        void Repack()
        {
            anchors.BeginFrame();
            foreach (var container in containers.Resolved)
            {
                if (!layouts.TryGetValue(container.Id, out var layout)) layouts[container.Id] = layout = new HudOverlayGroupLayout();
                layout.Apply(anchors, containers.Definition(container), members.Where(member => container.Tree.Leaves().Contains(member.Id)).Select(member =>
                    new KeyValuePair<HudOverlayRegistration, SizeF>(member, member.Id == "missing:integration" ? new(restoredWidth, restoredHeight) : new(width, height))).ToArray(),
                    containers.Origin(container), container.Tree, container.Padding, container.Attachment, containers.Reservation(container));
            }
        }
    }

    /// <summary>A returning integration gains a measured size immediately but no unreserved source slot until dragging ends.</summary>
    [Theory]
    [InlineData(false, 1.5f, 43, 31, 400, 350)]
    [InlineData(true, 2, 40, 30, 400, 350)]
    public void ReturningIntegrationWaitsForSourceReservationToEnd(bool cancel, float scale, float width, float height, float dragX, float dragY)
    {
        using var fixture = new Fixture(scale, measured: new SizeF(width, height), paired: true, individual: true, sourceThird: true);
        fixture.Begin(); fixture.Movement.Move(new PointF(dragX, dragY)); fixture.Repack();
        var source = fixture.Layouts[fixture.Containers.Owner("test:a2")!.Id];
        var frozen = source.Members.ToDictionary(member => member.Registration.Id, member => member.Clip);
        fixture.RegisterIntegration(); fixture.Repack();
        Assert.Contains(source.Measurements, member => member.Key.Id == "missing:integration");
        Assert.DoesNotContain(source.Members, member => member.Registration.Id == "missing:integration");
        Assert.All(source.Members, member => Assert.Equal(frozen[member.Registration.Id], member.Clip));
        if (cancel) fixture.Movement.Cancel(); else fixture.Movement.Commit(fixture.Movement.Position!.Value);
        fixture.Repack();
        Assert.Contains(fixture.Layouts[fixture.Containers.Owner("missing:integration")!.Id].Members, member => member.Registration.Id == "missing:integration");
    }

    /// <summary>Rejoining a reserved source uses current measured sizes while its drag targets retain their original slots.</summary>
    [Theory]
    [InlineData(1.5f, 40, 30, 76, 48, 400, 350)]
    [InlineData(2, 43, 31, 65, 50, 400, 350)]
    public void RejoiningReservedSourceUsesCurrentMeasuredSize(float scale, float width, float height, float changedWidth, float changedHeight, float dragX, float dragY)
    {
        using var fixture = new Fixture(scale, measured: new SizeF(width, height), paired: true, individual: true);
        fixture.Begin(); fixture.Movement.Move(new PointF(dragX, dragY)); fixture.Repack();
        var sibling = fixture.Layouts[fixture.Containers.Owner("test:a2")!.Id].Members.Single();
        var frozen = sibling.Clip;
        fixture.MeasurementOverrides["test:a2"] = new SizeF(changedWidth, changedHeight); fixture.Repack();
        Assert.Equal(frozen, sibling.Clip);
        var pointer = new PointF(frozen.Right, frozen.Top + frozen.Height / 2);
        fixture.Movement.Move(pointer); fixture.Repack(); fixture.Update(pointer);
        Assert.True(fixture.Snapping.Selected?.Grouping); Assert.Equal("test:a2", fixture.Snapping.Selected!.Target!.Member);
        var insertion = fixture.Snapping.Insertion; var target = fixture.Snapping.ConnectionTarget;
        fixture.Repack();
        var joined = fixture.Containers.Owner("test:a")!;
        var preview = fixture.Layouts[joined.Id];
        Assert.Equal(insertion, preview.Members.Single(member => member.Registration.Id == "test:a").Clip);
        Assert.Equal(target, preview.Members.Single(member => member.Registration.Id == "test:a2").Clip);
        Assert.Equal(changedWidth * scale, target.Width); Assert.Equal(changedHeight * scale, target.Height);
        Assert.False(insertion.IntersectsWith(target));
        fixture.Movement.Commit(fixture.Containers.Origin(joined)!.Value); fixture.Repack();
        Assert.Equal(insertion, fixture.Layouts[joined.Id].Members.Single(member => member.Registration.Id == "test:a").Clip);
        Assert.Equal(target, fixture.Layouts[joined.Id].Members.Single(member => member.Registration.Id == "test:a2").Clip);
    }

    /// <summary>Source siblings retain their pre-drag slots until a detached or joined transaction commits or cancels.</summary>
    [Theory]
    [InlineData(1, false, false, false, 40, 30, 300, 200)]
    [InlineData(1.5f, true, false, false, 43, 31, 300, 200)]
    [InlineData(2, false, true, false, 40, 30, 300, 200)]
    [InlineData(1.5f, true, true, true, 43, 31, 300, 200)]
    public void SourceFootprintIsReservedUntilTransactionEnds(float scale, bool third, bool join, bool cancel, float width, float height, float targetX, float targetY)
    {
        using var fixture = new Fixture(scale, measured: new SizeF(width, height), paired: true, individual: true, sourceThird: third, targetOrigin: new PointF(targetX, targetY));
        var original = fixture.Layouts["source"];
        var bounds = new RectangleF((float)original.Root.renderX, (float)original.Root.renderY, (float)original.Root.OuterWidth, (float)original.Root.OuterHeight);
        var siblings = original.Members.Where(member => member.Registration.Id != "test:a").ToDictionary(member => member.Registration.Id, member => member.Clip);
        fixture.Begin(); fixture.Movement.Move(new PointF(400, 350)); fixture.Repack();
        AssertReserved();
        if (join) { fixture.MoveBesideTarget(); fixture.Update(); fixture.Repack(); Assert.True(fixture.Snapping.Selected?.Grouping); AssertReserved(); }
        if (cancel) fixture.Movement.Cancel(); else fixture.Movement.Commit(fixture.Movement.Position!.Value);
        fixture.Repack();
        var remainder = fixture.Containers.Owner("test:a2")!;
        Assert.Null(fixture.Containers.Reservation(remainder));
        var final = fixture.Layouts[remainder.Id];
        if (cancel) { Assert.Equal(bounds, ReadRectangle(final.Root)); Assert.Contains(final.Members, member => member.Registration.Id == "test:a"); }
        else { Assert.True(final.Root.OuterWidth < bounds.Width || final.Root.OuterHeight < bounds.Height); Assert.DoesNotContain(final.Members, member => member.Registration.Id == "test:a"); }
        Assert.Equal(cancel ? "source" : fixture.Containers.Owner("test:a")!.Id, fixture.Restart().Owner("test:a")!.Id);

        // The reservation is a footprint only; the source has no duplicate live member.
        void AssertReserved()
        {
            var source = fixture.Layouts[fixture.Containers.Owner("test:a2")!.Id];
            Assert.Equal(bounds, ReadRectangle(source.Root));
            Assert.DoesNotContain(source.Members, member => member.Registration.Id == "test:a");
            Assert.All(source.Members, member => Assert.Equal(siblings[member.Registration.Id], member.Clip));
        }
    }

    /// <summary>Shift removes the active external snap preview so a later free commit cannot retain stale docking.</summary>
    [Theory]
    [InlineData(1, 500, 280, 150, 30)] [InlineData(1.5f, 500, 280, 150, 30)]
    public void ShiftBypassClearsExternalAttachment(float scale, float x, float y, float width, float height)
    {
        using var fixture = new Fixture(scale); fixture.Begin();
        var boundary = new HudSnapBoundary("hotbar", new RectangleF(x, y, width, height));
        fixture.Boundaries[boundary.Id] = boundary.Bounds;
        var source = fixture.Layouts["source"].Root;
        var pointer = new PointF(boundary.Bounds.Left + 12, boundary.Bounds.Top - (float)source.OuterHeight - 2);
        fixture.Movement.Move(pointer); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, pointer, false, scale, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true, new[] { boundary });
        Assert.NotNull(fixture.Snapping.Selected); Assert.NotNull(fixture.Containers.Owner("test:a")!.Attachment);
        fixture.Containers.ClearSnap(fixture.Movement.UnsnappedOrigin); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, pointer, true, scale, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true, new[] { boundary });
        Assert.Null(fixture.Snapping.Selected); Assert.True(fixture.Snapping.DropAllowed);
        fixture.Movement.Commit(fixture.Movement.Position!.Value);
        Assert.Null(fixture.Restart().Owner("test:a")!.Attachment);
    }

    /// <summary>A docked destination retains its external pivot and shows the same fractional-scale merge geometry it later commits.</summary>
    [Theory]
    [InlineData(1, 41, 31, 40, 30, 600, 260, 100, 40)]
    [InlineData(1.5f, 41, 31, 40, 30, 600, 260, 100, 40)]
    public void DockedMergePreviewMatchesMeasuredCommit(float scale, float sourceWidth, float sourceHeight, float targetWidth, float targetHeight,
        float nativeX, float nativeY, float nativeWidth, float nativeHeight)
    {
        using var fixture = new Fixture(scale, measured: new SizeF(targetWidth, targetHeight), sourceSize: new SizeF(sourceWidth, sourceHeight));
        var native = new RectangleF(nativeX, nativeY, nativeWidth, nativeHeight);
        fixture.Boundaries["hotbar"] = native;
        var attachment = new HudOverlayPlacement("native:hotbar", HudOverlayPoint.LeftMiddle, HudOverlayPoint.RightMiddle, screenPaddingPixels: 0);
        fixture.DockTarget(attachment);
        fixture.Begin(); fixture.MoveBesideTarget(HudSnapSide.Above); fixture.Update();
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected);
        Assert.True(selected.Grouping); Assert.True(fixture.Snapping.DropAllowed);
        var insertion = fixture.Snapping.Insertion; var destination = fixture.Snapping.Destination;
        fixture.Repack();
        var joined = Assert.Single(fixture.Containers.Resolved);
        Assert.Equal(attachment, joined.Attachment);
        var actual = fixture.Layouts[joined.Id];
        var source = actual.Members.Single(member => member.Registration.Id == "test:a");
        var target = actual.Members.Single(member => member.Registration.Id == "test:b");
        Assert.Equal(insertion, source.Clip); Assert.Equal(destination, target.Clip);
        fixture.Movement.Commit(fixture.Containers.Origin(joined)!.Value); fixture.Repack();
        Assert.Equal(insertion, fixture.Layouts[joined.Id].Members.Single(member => member.Registration.Id == "test:a").Clip);
        Assert.Equal(attachment, Assert.Single(fixture.Restart().Resolved).Attachment);
    }

    /// <summary>An edge-constrained member can move from the bottom to the top of its own stack and persist the reordered layout.</summary>
    [Theory]
    [InlineData(1, 800, 600, 80, 80, false)]
    [InlineData(1, 800, 600, 80, 80, true)]
    [InlineData(1.5f, 800, 600, 80, 80, false)]
    [InlineData(2, 800, 600, 80, 80, false)]
    public void BottomMemberCanReorderAboveViewportFlushStack(float scale, float width, float height, float itemWidth, float itemHeight, bool cancel)
    {
        float previousScale = RuntimeEnv.GUIScale; RuntimeEnv.GUIScale = scale;
        using var storage = new HudLayoutFixture();
        var members = new[] { storage.Add("test:a", "stack"), storage.Add("test:b", "stack"), storage.Add("test:c", "stack") };
        var original = new HudLayoutNode { First = new() { Indicator = "test:a" }, Second = new() {
            First = new() { Indicator = "test:b" }, Second = new() { Indicator = "test:c" }, Direction = HudOverlayDirection.Vertical },
            Direction = HudOverlayDirection.Vertical };
        var record = new HudLayoutContainer { Id = "stack", Key = HudOverlayPositionKeys.Group("stack"), TemplateId = "stack",
            X = (int)(width / scale - itemWidth), Y = 0, Movable = true, Tree = original };
        storage.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] { record } });
        var containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
        var anchors = new HudOverlayAnchorContext(new Window(new RectangleF(0, 0, width, height)), () => scale, () => null);
        var layouts = new Dictionary<string, HudOverlayGroupLayout>();
        // Recalculate the detached and joined trees using the production native layout boundary.
        Action repack = () => {
            anchors.BeginFrame();
            foreach (var item in containers.Resolved)
            {
                if (!layouts.TryGetValue(item.Id, out var layout)) layouts[item.Id] = layout = new HudOverlayGroupLayout();
                layout.Apply(anchors, containers.Definition(item), members.Where(member => item.Tree.Leaves().Contains(member.Id))
                    .Select(member => new KeyValuePair<HudOverlayRegistration, SizeF>(member, new SizeF(itemWidth, itemHeight))).ToArray(),
                    containers.Origin(item), item.Tree, item.Padding);
            }
        };
        try
        {
            repack(); var movement = containers.Get("stack", "test:c");
            movement.ContainerOrigin = new PointF(record.X!.Value, record.Y!.Value);
            movement.MemberOrigins = layouts["stack"].Members.ToDictionary(member => member.Registration.Id,
                member => new PointF((float)member.Bounds.renderX / scale, (float)member.Bounds.renderY / scale));
            var incoming = layouts["stack"].Members.Single(member => member.Registration.Id == "test:c").Bounds;
            var grab = new PointF((float)(incoming.renderX + incoming.OuterWidth / 2), (float)(incoming.renderY + incoming.OuterHeight / 2));
            movement.Begin(movement.MemberOrigins["test:c"], grab, scale);
            var pointer = new PointF(grab.X, 0); movement.Move(pointer); repack();
            var snapping = new HudOverlaySnapping(containers);
            snapping.Update(movement, pointer, false, scale, new RectangleF(0, 0, width, height), layouts, _ => true);
            Assert.True(snapping.Selected?.Grouping); Assert.Equal(HudSnapSide.Above, snapping.Selected!.Side);
            Assert.True(snapping.DropAllowed);
            if (cancel) { movement.Cancel(); Assert.Equal(original, Assert.Single(containers.Resolved).Tree); return; }
            var joined = Assert.Single(containers.Resolved); movement.Commit(containers.Origin(joined)!.Value); repack();
            Assert.Equal(new[] { "test:c", "test:a", "test:b" }, joined.Tree.Leaves());
            var clips = layouts[joined.Id].Members.Select(member => member.Clip).ToArray();
            Assert.All(clips, clip => Assert.True(new RectangleF(0, 0, width, height).Contains(clip)));
            for (int i = 0; i < clips.Length; i++) for (int j = i + 1; j < clips.Length; j++) Assert.False(clips[i].IntersectsWith(clips[j]));
            Assert.Equal(joined.Tree, Assert.Single(new HudOverlayContainers(storage.Api.Object, storage.Registry).Resolved).Tree);
        }
        finally { foreach (var layout in layouts.Values) layout.Dispose(); RuntimeEnv.GUIScale = previousScale; }
    }

    /// <summary>Fixed HUD exterior alignment remains separate membership and rounds away from native pixel overlap.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5f)] [InlineData(1.75f)]
    public void NativeBoundaryExteriorMatchesCommittedGeometry(float scale)
    {
        using var fixture = new Fixture(scale, targetOrigin: new PointF(350, 250));
        fixture.Begin();
        var boundary = new HudSnapBoundary("healthstatbar", new RectangleF(230.25f, 255.25f, 130, 22));
        fixture.Boundaries[boundary.Id] = boundary.Bounds;
        var source = fixture.Layouts["source"].Root;
        var pointer = new PointF(boundary.Bounds.Left + 12, boundary.Bounds.Top - (float)source.OuterHeight - 2);
        fixture.Movement.Move(pointer); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, pointer, false, scale, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true, new[] { boundary });
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected);
        Assert.False(selected.Grouping); Assert.Equal(boundary, selected.Boundary); Assert.True(selected.Exterior);
        Assert.True(fixture.Snapping.DropAllowed);
        fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack();
        var actual = fixture.Layouts["source"].Root;
        Assert.InRange(boundary.Bounds.Top - (float)(actual.renderY + actual.OuterHeight), 0, scale + .001f);
        Assert.Equal(selected.Origin.Y, (float)actual.renderY, 3);
        Assert.Equal(2, fixture.Containers.Resolved.Count);
        Assert.Equal(2, fixture.Restart().Resolved.Count);
        Assert.Equal(HudOverlayTargets.Target(boundary.Id), fixture.Restart().Owner("test:a")!.Attachment!.TargetId);
    }

    /// <summary>A matched horizontal guide cannot preserve a vertical docking gap through update, commit, or reload.</summary>
    [Theory]
    [InlineData(1, 6, 300, 400, 240, 20)]
    [InlineData(1.5f, 6, 300, 400, 240, 20)]
    [InlineData(2, 6, 300, 400, 240, 20)]
    public void NativeDockingClosesGapAndPersistsBothAxes(float scale, float gap,
        float x, float y, float width, float height)
    {
        using var fixture = new Fixture(scale);
        var boundary = new HudSnapBoundary("saturationstatbar", new RectangleF(x, y, width, height));
        fixture.Boundaries[boundary.Id] = boundary.Bounds;
        fixture.Begin();
        var source = fixture.Layouts["source"].Root;
        var pointer = new PointF(boundary.Bounds.Left, boundary.Bounds.Top - (float)source.OuterHeight - gap);
        fixture.Movement.Move(pointer); fixture.Repack(); fixture.Update(pointer);
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected);
        Assert.True(selected.Exterior);
        Assert.Equal(HudSnapSide.Above, selected.Side);
        Assert.True(fixture.Snapping.DropAllowed);
        fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack();
        var actual = fixture.Layouts["source"].Root;
        Assert.Equal(boundary.Bounds.Left, (float)actual.renderX, 3);
        Assert.InRange(boundary.Bounds.Top - (float)(actual.renderY + actual.OuterHeight), 0, scale);
        var attachment = fixture.Containers.Owner("test:a")!.Attachment!;
        Assert.Equal(0, attachment.OffsetX);
        Assert.InRange(Math.Abs(attachment.OffsetY), 0, 1);
        Assert.Equal(attachment, fixture.Restart().Owner("test:a")!.Attachment);
    }

    /// <summary>Native HUD surfaces reject overlapping free drops while Shift explicitly bypasses both magnets and obstacles.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeBoundaryOverlapRequiresShift(bool bypass)
    {
        using var fixture = new Fixture(1); fixture.Begin();
        var boundary = new HudSnapBoundary("native:hotbar", new RectangleF(100, 300, 180, 80));
        var pointer = new PointF(157, 327);
        fixture.Movement.Move(pointer); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, pointer, bypass, 1, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true, new[] { boundary });
        Assert.Equal(bypass, fixture.Snapping.DropAllowed); Assert.Null(fixture.Snapping.Selected);
        fixture.Containers.ClearSnap(fixture.Movement.UnsnappedOrigin); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, pointer, false, 1, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true, Array.Empty<HudSnapBoundary>());
        Assert.True(fixture.Snapping.DropAllowed);
    }

    /// <summary>A valid overlay grouping magnet is rejected when its combined placement would cover a native HUD surface.</summary>
    [Fact]
    public void GroupingCannotGrowOverNativeHud()
    {
        using var fixture = new Fixture(1); fixture.Begin(); fixture.MoveBesideTarget();
        var incoming = fixture.Layouts["source"].Root;
        var native = new HudSnapBoundary("native:health", new RectangleF((float)incoming.renderX, (float)incoming.renderY,
            (float)incoming.OuterWidth, (float)incoming.OuterHeight));
        fixture.Snapping.Update(fixture.Movement, new PointF((float)(fixture.Layouts["target"].Root.renderX + fixture.Layouts["target"].Root.OuterWidth), (float)(fixture.Layouts["target"].Root.renderY + fixture.Layouts["target"].Root.OuterHeight / 2)), false, 1, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true, new[] { native });
        Assert.False(fixture.Snapping.Selected?.Grouping == true);
        Assert.Equal(2, fixture.Containers.Resolved.Count);
    }
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
            var pointer = new PointF((float)(root.renderX + root.OuterWidth * (side == HudSnapSide.Left ? 0 : side == HudSnapSide.Right ? 1 : fraction)), (float)(root.renderY + root.OuterHeight * (side == HudSnapSide.Above ? 0 : side == HudSnapSide.Below ? 1 : fraction)));
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
            var selectedMember = fixture.Layouts[joined.Id].Members.Single(member =>
                member.Registration.Id == fixture.Snapping.Selected.Target.Member).Bounds;
            Assert.Equal(new RectangleF((float)selectedMember.renderX, (float)selectedMember.renderY,
                (float)selectedMember.OuterWidth, (float)selectedMember.OuterHeight), fixture.Snapping.ConnectionTarget);
            double expected = side switch { HudSnapSide.Left => before.Right, HudSnapSide.Above => before.Bottom,
                HudSnapSide.Right => before.Left, _ => before.Top };
            double actual = side switch { HudSnapSide.Left => after.renderX + after.OuterWidth,
                HudSnapSide.Above => after.renderY + after.OuterHeight, HudSnapSide.Right => after.renderX, _ => after.renderY };
            Assert.Equal(expected, actual, 3);
            fixture.Snapping.Clear();
            Assert.Equal(RectangleF.Empty, fixture.Snapping.ConnectionTarget);
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
                    .Select(member => new KeyValuePair<HudOverlayRegistration, SizeF>(member, sizes[member.Id])).ToArray(), containers.Origin(item), item.Tree, item.Padding, item.Attachment, containers.Reservation(item));
            }
        };
        try
        {
            repack(); var original = layouts["source"].Members;
            var peerClip = Assert.Single(layouts["peer"].Members).Clip;
            Assert.All(original, member => Assert.False(member.Clip.IntersectsWith(peerClip)));
            var movement = containers.Get("source", "test:a"); movement.ContainerOrigin = new PointF(100, 100);
            movement.SourceGeometry = HudOverlaySourceReservation.Capture(layouts["source"], PointF.Empty, 1);
            movement.MemberOrigins = original.ToDictionary(member => member.Registration.Id,
                member => new PointF((float)member.Bounds.renderX, (float)member.Bounds.renderY));
            movement.Begin(movement.MemberOrigins["test:a"], PointF.Empty, 1); movement.Move(new PointF(300, 200)); repack();
            Assert.All(layouts["source"].Members, member => Assert.False(member.Clip.IntersectsWith(peerClip)));
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
        Assert.Equal((selected.Axis == HudSnapAxis.X) ? selected.Origin.X : selected.Origin.Y, (float)((selected.Axis == HudSnapAxis.X) ? committed.renderX : committed.renderY), 3);
    }

    /// <summary>Edge alignment cannot advertise a snapped placement over another visible indicator.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)]
    public void OverlappingAlignmentIsRejected(float scale)
    {
        using var fixture = new Fixture(scale); fixture.Begin();
        var target = fixture.Layouts["target"].Root;
        fixture.Movement.Move(new PointF((float)target.renderX + 2, (float)target.renderY + 2)); fixture.Repack(); fixture.Update(new PointF((float)(target.renderX + target.OuterWidth + HudSnapGeometry.Release * 2), (float)(target.renderY + target.OuterHeight + HudSnapGeometry.Release * 2)));
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
        fixture.Snapping.Update(fixture.Movement, new PointF((float)(target.renderX + target.OuterWidth + HudSnapGeometry.Release * 2), (float)(target.renderY + target.OuterHeight + HudSnapGeometry.Release * 2)), bypass, 1, new RectangleF(0, 0, 800, 600), fixture.Layouts, _ => true);
        Assert.Null(fixture.Snapping.Selected);
        Assert.Equal(bypass, fixture.Snapping.DropAllowed);
    }

    /// <summary>A fractional physical center retains alignment and advertises the resolved native position.</summary>
    [Theory]
    [InlineData(1.25f)] [InlineData(1.5f)] [InlineData(1.75f)]
    public void FractionalCenterKeepsResolvedAlignmentGuide(float scale)
    {
        var viewport = new RectangleF(0, 0, 799, 600);
        using var fixture = new Fixture(scale, viewport: viewport); fixture.Begin();
        var source = fixture.Layouts["source"].Root;
        float center = (viewport.Width - (float)source.OuterWidth) / 2;
        fixture.Movement.Move(new PointF(center + 2, 50)); fixture.Repack();
        fixture.Snapping.Update(fixture.Movement, new PointF(center + 2, 50), false, scale, viewport, fixture.Layouts, _ => true);
        var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected); Assert.False(selected.Grouping); Assert.Equal(HudSnapAxis.X, selected.Axis);
        fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack();
        var actual = fixture.Layouts["source"].Root;
        Assert.Equal(selected.Origin.X, (float)actual.renderX, 3);
        Assert.InRange(Math.Abs((float)actual.renderX - center), 0, scale);
        Assert.Equal((float)(actual.renderX + actual.OuterWidth / 2), selected.Guide.X, 3);
        Assert.Equal(2, fixture.Containers.Resolved.Count);
    }

    /// <summary>Every screen edge is reachable by preview, commit, and restored native placement without an inset.</summary>
    [Theory]
    [InlineData(1, false)] [InlineData(1.5, false)] [InlineData(2, false)]
    [InlineData(1, true)] [InlineData(1.5, true)] [InlineData(2, true)]
    public void ScreenEdgeGuideMatchesNativeClampedOrigin(float scale, bool offsetWindow)
    {
        var viewport = new RectangleF(offsetWindow ? 31 : 0, offsetWindow ? 47 : 0, 800, 600);
        foreach (var side in Enum.GetValues<HudSnapSide>())
        {
            using var fixture = new Fixture(scale, viewport: viewport); fixture.Begin();
            var source = fixture.Layouts["source"].Root;
            var desired = side switch {
                HudSnapSide.Left => new PointF(viewport.Left, viewport.Top + 100),
                HudSnapSide.Right => new PointF(viewport.Right - (float)source.OuterWidth, viewport.Top + 100),
                HudSnapSide.Above => new PointF(viewport.Left + 100, viewport.Top),
                _ => new PointF(viewport.Left + 100, viewport.Bottom - (float)source.OuterHeight) };
            // The raw pointer remains slightly inside the acquisition zone on either axis.
            var pointer = desired + new SizeF(2, 2);
            fixture.Movement.Move(pointer); fixture.Repack();
            fixture.Snapping.Update(fixture.Movement, pointer, false, scale, viewport, fixture.Layouts, _ => true);
            var selected = Assert.IsType<HudSnapCandidate>(fixture.Snapping.Selected);
            Assert.False(selected.Grouping); Assert.True(fixture.Snapping.DropAllowed);
            fixture.Repack();
            var preview = fixture.Layouts["source"].Root;
            if (selected.Axis == HudSnapAxis.X) Assert.InRange(Math.Abs(desired.X - (float)preview.renderX), 0, scale);
            else Assert.InRange(Math.Abs(desired.Y - (float)preview.renderY), 0, scale);
            fixture.Movement.Commit(fixture.Movement.Position!.Value); fixture.Repack();
            Assert.Equal(selected.Origin.X, (float)fixture.Layouts["source"].Root.renderX, 3);
            Assert.Equal(selected.Origin.Y, (float)fixture.Layouts["source"].Root.renderY, 3);
            var restored = fixture.Restart();
            using var restoredLayout = new HudOverlayGroupLayout();
            var container = restored.Owner("test:a")!;
            restoredLayout.Apply(fixture.Anchors, restored.Definition(container), fixture.Layouts["source"].Members.Select(member =>
                new KeyValuePair<HudOverlayRegistration, SizeF>(member.Registration, new SizeF((float)member.Bounds.fixedWidth, (float)member.Bounds.fixedHeight))).ToArray(),
                restored.Origin(container), container.Tree, container.Padding);
            Assert.Equal(selected.Origin.X, (float)restoredLayout.Root.renderX, 3);
            Assert.Equal(selected.Origin.Y, (float)restoredLayout.Root.renderY, 3);
            Assert.All(restoredLayout.Members, member => Assert.Equal(new RectangleF((float)member.Bounds.renderX,
                (float)member.Bounds.renderY, (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight), member.Clip));
        }
    }
    #endregion

    #region Private
    /// <summary>Reads physical native root geometry independently of presentation dimensions.</summary>
    private static RectangleF ReadRectangle(ElementBounds bounds) => new((float)bounds.renderX, (float)bounds.renderY, (float)bounds.OuterWidth, (float)bounds.OuterHeight);

    /// <summary>Owns synthetic registrations and native bounds while borrowing no feature rendering resources.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly float previousScale = RuntimeEnv.GUIScale;
        private readonly HudLayoutFixture storage = new();
        private readonly Window window;
        private readonly HudOverlayAnchorContext anchors;
        private readonly List<HudOverlayRegistration> members = new();
        private readonly SizeF size;
        private readonly SizeF sourceSize;
        private PointF? attachmentPointer;
        public readonly HudOverlayContainers Containers;
        public readonly HudOverlayGroupMovement Movement;
        public readonly HudOverlaySnapping Snapping;
        public readonly Dictionary<string, HudOverlayGroupLayout> Layouts = new();
        public readonly Dictionary<string, RectangleF> Boundaries = new();
        public readonly Dictionary<string, SizeF> MeasurementOverrides = new();
        public HudOverlayAnchorContext Anchors => anchors;
        #region Public API
        /// <summary>Builds caller-defined instrument geometry at a selected native scale.</summary>
        public Fixture(float scale, SizeF? measured = null, PointF? targetOrigin = null, bool paired = false, SizeF? sourceSize = null, bool individual = false, RectangleF? viewport = null, bool sourceThird = false)
        {
            RuntimeEnv.GUIScale = scale; size = measured ?? new SizeF(40, 30);
            window = new Window(viewport);
            this.sourceSize = sourceSize ?? size;
            members.Add(storage.Add("test:a", "source")); members.Add(storage.Add("test:b", "target"));
            if (paired) { members.Add(storage.Add("test:a2", "source")); members.Add(storage.Add("test:b2", "target")); }
            if (sourceThird) members.Add(storage.Add("test:a3", "source"));
            // These geometry fixtures start with explicit free positions rather than registered default anchors.
            storage.Positions[HudOverlayPositionKeys.Group("source")] = new(30, 40);
            var targetPosition = targetOrigin ?? new PointF(200, 120);
            storage.Positions[HudOverlayPositionKeys.Group("target")] = new((int)targetPosition.X, (int)targetPosition.Y);
            if (sourceThird)
            {
                // A temporarily unavailable integration remains structurally present without measured artwork.
                storage.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] {
                    new HudLayoutContainer { Id = "source", Key = HudOverlayPositionKeys.Group("source"), TemplateId = "source", X = 30, Y = 40, Movable = true,
                        Tree = new() { First = new() { Indicator = "test:a" }, Second = new() { First = new() { Indicator = "test:a2" },
                            Second = new() { First = new() { Indicator = "test:a3" }, Second = new() { Indicator = "missing:integration" } } } } },
                    new HudLayoutContainer { Id = "target", Key = HudOverlayPositionKeys.Group("target"), TemplateId = "target", X = (int)targetPosition.X, Y = (int)targetPosition.Y,
                        Tree = new() { First = new() { Indicator = "test:b" }, Second = new() { Indicator = "test:b2" } } } } });
            }
            Containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
            Containers.SetMovable("source", true, new PointF(30, 40)); Containers.SetMovable("target", false, targetOrigin ?? new PointF(200, 120));
            anchors = new HudOverlayAnchorContext(window, () => RuntimeEnv.GUIScale, () => Boundaries.TryGetValue(HudOverlayTargets.Hotbar, out var hotbar) ? hotbar : null,
                readSaturation: () => Boundaries.TryGetValue(HudOverlayTargets.SaturationElement, out var saturation) ? saturation : null, readBoundary: id => Boundaries.TryGetValue(id, out var rectangle) ? rectangle : null);
            Movement = Containers.Get("source", individual ? "test:a" : null); Snapping = new HudOverlaySnapping(Containers); Repack();
        }
        /// <summary>Captures source movement without native titlebar hit testing.</summary>
        public void Begin()
        {
            var layout = Layouts["source"];
            Movement.SourceGeometry = HudOverlaySourceReservation.Capture(layout, new PointF((float)window.renderX, (float)window.renderY), RuntimeEnv.GUIScale);
            Movement.ContainerOrigin = Movement.SourceGeometry.Origin;
            Movement.MemberOrigins = layout.Members.ToDictionary(member => member.Registration.Id, member => new PointF(
                (float)((member.Bounds.renderX - window.renderX) / RuntimeEnv.GUIScale), (float)((member.Bounds.renderY - window.renderY) / RuntimeEnv.GUIScale)));
            Movement.Begin(new PointF(30, 40), new PointF((float)window.renderX + 30 * RuntimeEnv.GUIScale, (float)window.renderY + 40 * RuntimeEnv.GUIScale), RuntimeEnv.GUIScale);
        }
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
            attachmentPointer = new PointF((float)(root.renderX + (side == HudSnapSide.Right ? root.OuterWidth : side == HudSnapSide.Left ? 0 : root.OuterWidth / 2)), (float)(root.renderY + (side == HudSnapSide.Below ? root.OuterHeight : side == HudSnapSide.Above ? 0 : root.OuterHeight / 2)));
            Movement.Move(position); Repack();
        }
        /// <summary>Reloads the authoritative committed snapshot without changing its native mirrors.</summary>
        public HudOverlayContainers Restart() => new(storage.Api.Object, storage.Registry);
        /// <summary>Simulates registration of an optional integration retained in the authoritative tree.</summary>
        public void RegisterIntegration()
        {
            members.Add(storage.Add("missing:integration", "source"));
            Containers.Synchronize();
        }
        /// <summary>Calculates feedback against native roots and drawable instrument measurements.</summary>
        public void Update(PointF? pointer = null) => Snapping.Update(Movement, pointer ?? attachmentPointer ?? Movement.UnsnappedOrigin, false,
            RuntimeEnv.GUIScale, new RectangleF(0, 0, 800, 600), Layouts, _ => true,
            Boundaries.Select(pair => new HudSnapBoundary(pair.Key, pair.Value)).ToArray());
        /// <summary>Persists a target attachment through the same transaction contract used by a native snap.</summary>
        public void DockTarget(HudOverlayPlacement attachment)
        {
            var origin = Containers.Origin(Containers.Owner("test:b")!)!.Value;
            Assert.True(Containers.SetMovable("target", true, origin));
            Assert.True(Containers.Begin("target", null, origin, new Dictionary<string, PointF>()));
            Containers.Move(origin, attachment); Assert.True(Containers.Commit(origin));
            Assert.True(Containers.SetMovable("target", false, origin)); Repack();
        }
        /// <summary>Adds a separate measured obstacle in the proposed incoming footprint.</summary>
        public void AddObstacle()
        {
            var target = Layouts["target"].Root; members.Add(storage.Add("test:obstacle", "obstacle"));
            Containers.Synchronize();
            var position = new PointF((float)(target.renderX + target.OuterWidth + 6), (float)target.renderY);
            Containers.SetMovable("obstacle", true, position);
            Assert.True(Containers.Begin("obstacle", null, position, new Dictionary<string, PointF>()));
            Assert.True(Containers.Commit(position));
            Containers.SetMovable("obstacle", false, position); Repack();
        }
        /// <summary>Applies effective container trees using the same native layout boundary as the runtime.</summary>
        public void Repack()
        {
            anchors.BeginFrame();
            foreach (string id in Layouts.Keys.Where(id => Containers.Resolved.All(value => value.Id != id)).ToArray()) { Layouts[id].Dispose(); Layouts.Remove(id); }
            foreach (var container in Containers.Resolved)
            {
                if (!Layouts.TryGetValue(container.Id, out var layout)) Layouts[container.Id] = layout = new HudOverlayGroupLayout();
                layout.Apply(anchors, Containers.Definition(container), members.Where(value => container.Tree.Leaves().Contains(value.Id)).Select(value => new KeyValuePair<HudOverlayRegistration, SizeF>(value, MeasurementOverrides.TryGetValue(value.Id, out var changed) ? changed : value.Id.StartsWith("test:a", StringComparison.Ordinal) ? sourceSize : size)).ToArray(), Containers.Origin(container), container.Tree, container.Padding, container.Attachment, Containers.Reservation(container));
            }

        }
        /// <summary>Releases native layout roots and restores global GUI scaling.</summary>
        public void Dispose() { Movement.Cancel(); foreach (var layout in Layouts.Values) layout.Dispose(); storage.Dispose(); RuntimeEnv.GUIScale = previousScale; }
        #endregion
    }
    /// <summary>Provides a deterministic native framebuffer parent.</summary>
    private sealed class Window : ElementBounds
    {
        private readonly RectangleF viewport;
        #region Public API
        /// <summary>Marks the geometry root as a viewport.</summary>
        public Window(RectangleF? viewport = null) { this.viewport = viewport ?? new RectangleF(0, 0, 800, 600); IsWindowBounds = true; CalcWorldBounds(); }
        /// <summary>Provides the framebuffer origin.</summary>
        public override double renderX => viewport.X;
        /// <summary>Provides the framebuffer origin.</summary>
        public override double renderY => viewport.Y;
        /// <summary>Supplies caller-independent screen extents without platform access.</summary>
        public override void CalcWorldBounds() { absInnerWidth = viewport.Width; absInnerHeight = viewport.Height; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}
