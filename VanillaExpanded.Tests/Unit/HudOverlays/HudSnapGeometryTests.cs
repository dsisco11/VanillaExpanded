using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks physical snap acquisition, stable selection and caller-supplied attachment geometry.</summary>
public sealed class HudSnapGeometryTests
{
    #region Public API
    #region Acquisition and candidate geometry
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
            var pointer = new PointF(target.Bounds.Left + factor * target.Bounds.Width, target.Bounds.Top + factor * target.Bounds.Height);
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
        var pointer = target.Bounds.Location;
        var retained = Candidates(moving, pointer, new[] { target }).First(value => value.Grouping);
        moving.X += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        Assert.DoesNotContain(Candidates(moving, pointer, new[] { target }), value => value.Identity == retained.Identity);
        Assert.Equal(retained.Identity, Candidates(moving, pointer, new[] { target }, retained).First().Identity);
        Assert.Empty(HudSnapGeometry.Candidates(moving, pointer, new RectangleF(0, 0, 1000, 800), new[] { target }, retained, true));
        moving.X += HudSnapGeometry.Release;
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
        moving.Y += a.Bounds.Height * 4;
        Assert.DoesNotContain(Candidates(moving, a.Bounds.Location, new[] { a }), value => value.Grouping);
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
    #region Magnetic intent and retention
    /// <summary>Attachments preempt retained screen guides for individual and whole-group targets.</summary>
    [Theory]
    [InlineData("leaf")] [InlineData(null)]
    public void GroupingPreemptsRetainedScreenGuide(string? member)
    {
        var target = new HudSnapTarget("destination", member, new RectangleF(40, 200, 80, 60), 5);
        var moving = new RectangleF(5, 200, 30, 20);
        var screen = Candidates(moving, moving.Location, Array.Empty<HudSnapTarget>()).First(value => value.AlignsX);
        var selected = Candidates(moving, target.Bounds.Location, new[] { target }, screen).First();
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
        Assert.Contains(Candidates(moving, target.Bounds.Location, new[] { target }), value => value.Grouping && value.Side == HudSnapSide.Right);
        moving.Y += HudSnapGeometry.Release * 2;
        Assert.DoesNotContain(Candidates(moving, target.Bounds.Location, new[] { target }), value => value.Grouping);
    }

    /// <summary>Retention stabilizes competing destinations until the acquired attachment leaves its release band.</summary>
    [Fact]
    public void CompetingTargetRetainsUntilReleased()
    {
        var target = new HudSnapTarget("a", "leaf", new RectangleF(200, 200, 80, 60), 5);
        var other = target with { Container = "b", Bounds = new RectangleF(210, 200, 80, 60) };
        var moving = new RectangleF(target.Bounds.Right + target.Gap, target.Bounds.Top, 30, 20);
        var retained = Candidates(moving, target.Bounds.Location, new[] { target }).First(value => value.Grouping);
        moving.X = other.Bounds.Right + other.Gap;
        Assert.Equal(target, Candidates(moving, target.Bounds.Location, new[] { other, target }, retained).First().Target);
        moving.X = target.Bounds.Right + target.Gap + HudSnapGeometry.Release + 1;
        other = other with { Bounds = new RectangleF(moving.X - other.Gap - other.Bounds.Width, 200, 80, 60) };
        Assert.Equal(other, Candidates(moving, target.Bounds.Location, new[] { target, other }, retained).First().Target);
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
