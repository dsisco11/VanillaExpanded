using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks physical snap acquisition, stable selection and caller-supplied attachment geometry.</summary>
public sealed class HudSnapGeometryTests
{
    #region Public API
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
        string retained = Candidates(moving, pointer, new[] { target }).First(value => value.Grouping).Identity;
        moving.X += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        Assert.DoesNotContain(Candidates(moving, pointer, new[] { target }), value => value.Identity == retained);
        Assert.Equal(retained, Candidates(moving, pointer, new[] { target }, retained).First().Identity);
        Assert.Empty(HudSnapGeometry.Candidates(moving, pointer, new RectangleF(0, 0, 1000, 800), new[] { target }, retained, true));
        moving.X += HudSnapGeometry.Release;
        Assert.DoesNotContain(Candidates(moving, pointer, new[] { target }, retained), value => value.Identity == retained);
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

    #region Private
    /// <summary>Supplies a distant viewport so synthetic local targets determine attachment selection.</summary>
    private static IReadOnlyList<HudSnapCandidate> Candidates(RectangleF moving, PointF pointer, IEnumerable<HudSnapTarget> targets, string? retained = null) =>
        HudSnapGeometry.Candidates(moving, pointer, new RectangleF(0, 0, 1000, 800), targets, retained, false);
    #endregion
}
