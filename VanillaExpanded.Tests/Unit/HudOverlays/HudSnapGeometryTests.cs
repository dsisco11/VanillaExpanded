using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks physical snap acquisition, stable selection and caller-supplied attachment geometry.</summary>
public sealed class HudSnapGeometryTests
{
    #region Public API
    #region Acquisition and candidate geometry
    /// <summary>A pointer constrained to a viewport edge can attach outside a flush target without requiring an offscreen grab point.</summary>
    [Theory]
    [InlineData(0, 0, 180, -40, 180, 0, 210, 1)]
    [InlineData(1, 920, 180, 960, 180, 1000, 210, 1)]
    [InlineData(2, 900, 0, 900, -40, 940, 0, 1)]
    [InlineData(3, 900, 720, 900, 760, 940, 800, 1)]
    [InlineData(0, 0, 180, -40, 180, 0, 210, 2)]
    [InlineData(1, 920, 180, 960, 180, 1000, 210, 2)]
    [InlineData(2, 900, 0, 900, -40, 940, 0, 2)]
    [InlineData(3, 900, 720, 900, 760, 940, 800, 2)]
    public void ViewportEdgeCanAcquireExteriorGrouping(int side, float targetX, float targetY,
        float movingX, float movingY, float pointerX, float pointerY, float scale)
    {
        var viewport = new RectangleF(0, 0, 1000 * scale, 800 * scale);
        var target = new HudSnapTarget("destination", "leaf",
            new RectangleF(targetX * scale, targetY * scale, 80 * scale, 80 * scale), 5 * scale);
        var moving = new RectangleF(movingX * scale, movingY * scale, 80 * scale, 80 * scale);
        var pointer = new PointF(pointerX * scale, pointerY * scale);
        var candidates = HudSnapGeometry.Candidates(moving, pointer, viewport, new[] { target }, null, false);
        var acquired = Assert.Single(candidates, candidate => candidate.Grouping && candidate.Side == (HudSnapSide)side);
        Assert.Equal(target, acquired.Target);
        Assert.DoesNotContain(HudSnapGeometry.Candidates(moving, pointer, viewport, new[] { target }, acquired, true),
            candidate => candidate.Grouping);
    }

    /// <summary>A distant pointer cannot acquire the finite target edge.</summary>
    [Theory]
    [InlineData(0, 200, -40, 0, 0, 0, 500, 300)]
    [InlineData(200, 0, 160, -40, 400, 0, 80, 80)]
    public void PointerMustBeNearFiniteTargetEdge(float targetX, float targetY,
        float movingX, float movingY, float pointerX, float pointerY, float movingWidth, float movingHeight)
    {
        var target = new HudSnapTarget("destination", "leaf", new RectangleF(targetX, targetY, 80, 80), 5);
        var moving = new RectangleF(movingX, movingY, movingWidth, movingHeight);
        var candidates = HudSnapGeometry.Candidates(moving, new PointF(pointerX, pointerY), new RectangleF(0, 0, 1000, 800),
            new[] { target }, null, false);
        Assert.DoesNotContain(candidates, candidate => candidate.Grouping && candidate.Side == HudSnapSide.Left);
    }

    /// <summary>All four sides expose every cross-axis alignment and retain the supplied normal gap.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EverySideSelectsAlignmentFromPointer(int sideValue)
    {
        var side = (HudSnapSide)sideValue;
        var target = new HudSnapTarget("destination", "leaf", new RectangleF(200, 200, 90, 60), 7);
        var size = new SizeF(30, 20);
        foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
        {
            float factor = alignment == HudOverlayCrossAlignment.Start ? 0 : alignment == HudOverlayCrossAlignment.End ? 1 : .5f;
            bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
            var expected = new PointF(horizontal ? side == HudSnapSide.Left ? target.Bounds.Left - target.Gap - size.Width : target.Bounds.Right + target.Gap : target.Bounds.Left + factor * (target.Bounds.Width - size.Width),
                horizontal ? target.Bounds.Top + factor * (target.Bounds.Height - size.Height) : side == HudSnapSide.Above ? target.Bounds.Top - target.Gap - size.Height : target.Bounds.Bottom + target.Gap);
            var pointer = new PointF(horizontal ? side == HudSnapSide.Left ? target.Bounds.Left : target.Bounds.Right : target.Bounds.Left + factor * target.Bounds.Width, horizontal ? target.Bounds.Top + factor * target.Bounds.Height : side == HudSnapSide.Above ? target.Bounds.Top : target.Bounds.Bottom);
            var candidate = Assert.Single(Candidates(new RectangleF(expected, size), pointer, new[] { target }), value => value.Grouping && value.Side == side);
            Assert.Equal(alignment, candidate.Alignment); Assert.Equal(expected, candidate.Origin); Assert.Equal(target, candidate.Target);
        }
    }

    /// <summary>Retention uses a wider band, then releases; bypass immediately removes all candidates.</summary>
    [Fact]
    public void HysteresisRetainsAcquiredIdentityAndShiftBypassesIt()
    {
        var target = new HudSnapTarget("destination", "leaf", new RectangleF(200, 200, 80, 60), 5);
        var moving = new RectangleF(target.Bounds.Right + target.Gap, target.Bounds.Top, 30, 20);
        var pointer = new PointF(target.Bounds.Right, target.Bounds.Top + target.Bounds.Height / 2);
        var retained = Candidates(moving, pointer, new[] { target }).First(value => value.Grouping);
        pointer.X += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        Assert.DoesNotContain(Candidates(moving, pointer, new[] { target }), value => value.Identity == retained.Identity);
        Assert.Equal(retained.Identity, Candidates(moving, pointer, new[] { target }, retained).First().Identity);
        Assert.Empty(HudSnapGeometry.Candidates(moving, pointer, new RectangleF(0, 0, 1000, 800), new[] { target }, retained, true));
        pointer.X += HudSnapGeometry.Release;
        Assert.DoesNotContain(Candidates(moving, pointer, new[] { target }, retained), value => value.Identity == retained.Identity);
    }

    /// <summary>Enumeration order cannot choose a different tied destination, and distant perpendicular spans cannot group.</summary>
    [Fact]
    public void CandidateOrderingIsDeterministicAndLocal()
    {
        var a = new HudSnapTarget("a", "one", new RectangleF(200, 200, 80, 60), 5);
        var b = a with { Container = "b" };
        var moving = new RectangleF(a.Bounds.Right + a.Gap, a.Bounds.Top, 30, 20);
        var forward = Candidates(moving, a.Bounds.Location, new[] { a, b });
        var reverse = Candidates(moving, a.Bounds.Location, new[] { b, a });
        Assert.Equal(forward.Select(value => value.Identity), reverse.Select(value => value.Identity));
        Assert.Equal("a", forward.First(value => value.Grouping).Target!.Container);
        var farPointer = new PointF(a.Bounds.Left, a.Bounds.Bottom + a.Bounds.Height * 4);
        Assert.DoesNotContain(Candidates(moving, farPointer, new[] { a }), value => value.Grouping);
        Assert.DoesNotContain(Candidates(moving, a.Bounds.Location, new[] { a with { Bounds = RectangleF.Empty } }), value => value.Grouping);
    }

    /// <summary>Matching screen edges supply only alignment, never a grouping destination.</summary>
    [Fact]
    public void ScreenAlignmentNeverCreatesMembershipIntent()
    {
        var candidates = Candidates(new RectangleF(2, 100, 30, 20), new PointF(2, 100), Array.Empty<HudSnapTarget>());
        Assert.NotEmpty(candidates); Assert.All(candidates, value => Assert.False(value.Grouping));
        Assert.Contains(candidates, value => value.Origin.X == 0);
    }

    #endregion
    /// <summary>Pointer intent selects the same attachment for different incoming dimensions and grab offsets.</summary>
    [Theory]
    [InlineData(0, 0, 30, 20, 200, 230, 0)]
    [InlineData(700, 500, 150, 100, 200, 230, 0)]
    [InlineData(0, 0, 30, 20, 280, 230, 1)]
    [InlineData(700, 500, 150, 100, 280, 230, 1)]
    [InlineData(0, 0, 30, 20, 240, 200, 2)]
    [InlineData(700, 500, 150, 100, 240, 200, 2)]
    [InlineData(0, 0, 30, 20, 240, 260, 3)]
    [InlineData(700, 500, 150, 100, 240, 260, 3)]
    public void PointerIntentIsIndependentOfIncomingGeometry(float x, float y, float width, float height,
        float pointerX, float pointerY, int side)
    {
        var target = new HudSnapTarget("destination", "leaf", new RectangleF(200, 200, 80, 60), 5);
        var selected = Candidates(new RectangleF(x, y, width, height), new PointF(pointerX, pointerY), new[] { target }).First();
        Assert.True(selected.Grouping); Assert.Equal((HudSnapSide)side, selected.Side);
        Assert.Equal(HudOverlayCrossAlignment.Center, selected.Alignment);
        Assert.Equal(target, selected.Target);
    }

    /// <summary>Incoming edges cannot manufacture grouping when the pointer is elsewhere.</summary>
    [Theory]
    [InlineData(165, 200, 600, 500)]
    [InlineData(285, 200, 600, 500)]
    [InlineData(200, 175, 600, 500)]
    [InlineData(200, 265, 600, 500)]
    public void IncomingEdgeDoesNotAcquireDistantPointer(float x, float y, float pointerX, float pointerY)
    {
        var target = new HudSnapTarget("destination", "leaf", new RectangleF(200, 200, 80, 60), 5);
        Assert.DoesNotContain(Candidates(new RectangleF(x, y, 30, 20), new PointF(pointerX, pointerY), new[] { target }), candidate => candidate.Grouping);
    }

    #region Magnetic intent and retention
    /// <summary>Attachments preempt retained screen guides for individual and whole-group targets.</summary>
    [Theory]
    [InlineData("leaf")] [InlineData(null)]
    public void GroupingPreemptsRetainedScreenGuide(string? member)
    {
        var target = new HudSnapTarget("destination", member, new RectangleF(40, 200, 80, 60), 5);
        var moving = new RectangleF(5, 200, 30, 20);
        var screen = Candidates(moving, moving.Location, Array.Empty<HudSnapTarget>()).First(value => value.Axis == HudSnapAxis.X);
        var selected = Candidates(moving, new PointF(target.Bounds.Left, target.Bounds.Top + target.Bounds.Height / 2), new[] { target }, screen).First();
        Assert.True(selected.Grouping); Assert.Equal(target, selected.Target); Assert.Equal(HudSnapSide.Left, selected.Side);
    }

    /// <summary>Pointer jitter retains alignment while deliberate movement crosses the alignment margin.</summary>
    [Theory]
    [InlineData("leaf")] [InlineData(null)]
    public void AlignmentBoundaryRetainsThenSwitches(string? member)
    {
        var target = new HudSnapTarget("destination", member, new RectangleF(200, 200, 90, 90), 5);
        var moving = new RectangleF(target.Bounds.Right + target.Gap, target.Bounds.Top, 30, 20);
        float boundary = target.Bounds.Top + target.Bounds.Height / 3;
        var retained = Candidates(moving, new PointF(moving.X, boundary - 1), new[] { target }).First(value => value.Grouping);
        var jitter = Candidates(moving, new PointF(moving.X, boundary + 1), new[] { target }, retained).First();
        Assert.Equal(HudOverlayCrossAlignment.Start, jitter.Alignment); Assert.Equal(retained.Target, jitter.Target); Assert.Equal(retained.Side, jitter.Side);
        var changed = Candidates(moving, new PointF(moving.X, boundary + 12), new[] { target }, jitter).First();
        Assert.Equal(HudOverlayCrossAlignment.Center, changed.Alignment); Assert.Equal(retained.Target, changed.Target); Assert.Equal(retained.Side, changed.Side);
        moving.X += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        Assert.Contains(Candidates(moving, new PointF(moving.X, boundary + 12), new[] { target }, changed), value => value.Grouping && value.Side == retained.Side);
    }

    /// <summary>Nearby corners acquire across a perpendicular gap within the magnetic band.</summary>
    [Fact]
    public void NearbyCornerAcquiresWithinBand()
    {
        var target = new HudSnapTarget("destination", "leaf", new RectangleF(200, 200, 80, 60), 5);
        var moving = new RectangleF(target.Bounds.Right + target.Gap, target.Bounds.Bottom + HudSnapGeometry.Acquisition / 2, 30, 20);
        var pointer = new PointF(target.Bounds.Right, moving.Y);
        Assert.Contains(Candidates(moving, pointer, new[] { target }), value => value.Grouping && value.Side == HudSnapSide.Right);
        pointer.Y += HudSnapGeometry.Release * 2;
        Assert.DoesNotContain(Candidates(moving, pointer, new[] { target }), value => value.Grouping);
    }

    /// <summary>Retention stabilizes competing destinations until the acquired attachment leaves its release band.</summary>
    [Fact]
    public void CompetingTargetRetainsUntilReleased()
    {
        var target = new HudSnapTarget("a", "leaf", new RectangleF(200, 200, 80, 60), 5);
        var other = target with { Container = "b", Bounds = new RectangleF(210, 200, 80, 60) };
        var moving = new RectangleF(target.Bounds.Right + target.Gap, target.Bounds.Top, 30, 20);
        var pointer = new PointF(target.Bounds.Right, target.Bounds.Top + target.Bounds.Height / 2);
        var retained = Candidates(moving, pointer, new[] { target }).First(value => value.Grouping);
        pointer.X = other.Bounds.Right;
        Assert.Equal(target, Candidates(moving, pointer, new[] { other, target }, retained).First().Target);
        pointer.X = target.Bounds.Right + HudSnapGeometry.Release + 1;
        other = other with { Bounds = new RectangleF(pointer.X - other.Bounds.Width, 200, 80, 60) };
        Assert.Equal(other, Candidates(moving, pointer, new[] { target, other }, retained).First().Target);
    }

    /// <summary>Both alignment boundaries resist jitter in both directions along either attachment axis.</summary>
    [Theory]
    [InlineData(true, 1, 1)] [InlineData(true, 1, -1)] [InlineData(true, 2, 1)] [InlineData(true, 2, -1)]
    [InlineData(false, 1, 1)] [InlineData(false, 1, -1)] [InlineData(false, 2, 1)] [InlineData(false, 2, -1)]
    public void EveryBoundaryRetainsBothDirections(bool horizontal, int boundaryIndex, int direction)
    {
        var target = new HudSnapTarget("destination", null, new RectangleF(200, 200, 90, 90), 5);
        var moving = new RectangleF(horizontal ? target.Bounds.Right + target.Gap : target.Bounds.Left,
            horizontal ? target.Bounds.Top : target.Bounds.Bottom + target.Gap, 30, 20);
        float boundary = 200 + target.Bounds.Width * boundaryIndex / 3;
        /// <summary>Moves the pointer along the cross axis while retaining its normal coordinate.</summary>
        PointF Pointer(float coordinate) => horizontal ? new PointF(moving.X, coordinate) : new PointF(coordinate, moving.Y);
        var retained = Candidates(moving, Pointer(boundary - direction), new[] { target }).First(value => value.Grouping);
        var jitter = Candidates(moving, Pointer(boundary + direction), new[] { target }, retained).First();
        Assert.Equal(retained.Alignment, jitter.Alignment);
        var switched = Candidates(moving, Pointer(boundary + direction * 12), new[] { target }, jitter).First();
        Assert.NotEqual(retained.Alignment, switched.Alignment); Assert.Equal(retained.Side, switched.Side);
        if (horizontal) moving.X += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        else moving.Y += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        var held = Candidates(moving, Pointer(boundary + direction * 12), new[] { target }, switched).First();
        Assert.Equal(switched.Alignment, held.Alignment); Assert.Equal(switched.Target, held.Target); Assert.Equal(switched.Side, held.Side);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Supplies a distant viewport so synthetic local targets determine attachment selection.</summary>
    private static IReadOnlyList<HudSnapCandidate> Candidates(RectangleF moving, PointF pointer, IEnumerable<HudSnapTarget> targets, HudSnapCandidate? retained = null) =>
        HudSnapGeometry.Candidates(moving, pointer, new RectangleF(0, 0, 1000, 800), targets, retained, false);
    #endregion
}
