using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies permission-only controls, native recovery and total-displacement previews.</summary>
public sealed class HudOverlayGroupMovementTests
{
    #region Public API
    /// <summary>Defaults cannot move until explicitly enabled; Fixed captures placement without deleting it.</summary>
    [Fact]
    public void FixedPreservesOriginMembershipAndLocksMemberDetachment()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        var movement = containers.Get("test");
        Assert.False(movement.Movable);
        Assert.False(containers.Begin("test", "test:a", PointF.Empty, new Dictionary<string, PointF>()));
        var origin = new PointF(21, 37);
        Assert.True(containers.SetMovable("test", true, origin));
        Assert.True(containers.SetMovable("test", false, origin));
        Assert.Equal(origin, movement.Position);
        Assert.Equal(new[] { "test:a", "test:b" }, Assert.Single(containers.Resolved).Tree.Leaves());
        Assert.False(containers.Begin("test", "test:a", origin, new Dictionary<string, PointF>()));
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.False(restored.Get("test").Movable); Assert.Equal(origin, restored.Get("test").Position);
    }
    /// <summary>Pointer displacement is scaled once, remains unsummed, and cancellation performs no writes.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void PreviewUsesTotalDisplacementAndCancelDoesNotPersist(double scale)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        var origin = new PointF(20, 30); containers.SetMovable("test", true, origin);
        int writes = fixture.Writes; var movement = containers.Get("test");
        movement.Begin(origin, new PointF(100, 100), scale);
        movement.Move(new PointF(160, 190)); movement.Move(new PointF(130, 160));
        Assert.Equal(new PointF(origin.X + (float)(30 / scale), origin.Y + (float)(60 / scale)), movement.Position);
        movement.Cancel(); Assert.Equal(origin, movement.Position); Assert.Equal(writes, fixture.Writes);
    }
    /// <summary>Sub-GUI-unit pointer movement remains continuous until release truncates the native checkpoint.</summary>
    [Theory]
    [InlineData(1.5)] [InlineData(2)]
    public void FractionalPreviewIsNotRoundedBeforeRelease(double scale)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(20, 30);
        containers.SetMovable("test", true, origin); int writes = fixture.Writes; var movement = containers.Get("test"); movement.Begin(origin, PointF.Empty, scale);
        movement.Move(new PointF(1, 1)); var preview = movement.Position!.Value;
        Assert.True(preview.X > origin.X && preview.X < origin.X + 1); Assert.Equal(writes, fixture.Writes);
        Assert.Equal((int)origin.X, Assert.Single(containers.Resolved).X);
        movement.Commit(preview); Assert.Equal(origin, movement.Position); Assert.Equal(writes + 1, fixture.Writes);
    }    /// <summary>Successful release stores displayed integer coordinates once and restores them through the snapshot.</summary>
    [Fact]
    public void CommitStoresDisplayedOriginAndRestartRestoresIt()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        containers.SetMovable("test", true, PointF.Empty); int writes = fixture.Writes;
        var movement = containers.Get("test"); movement.Begin(PointF.Empty, PointF.Empty, 1);
        movement.Move(new PointF(10000, 10000)); movement.Commit(new PointF(150.9f, 240.8f)); movement.Commit(PointF.Empty);
        Assert.Equal(writes + 1, fixture.Writes);
        Assert.Equal(new PointF(150, 240), new HudOverlayContainers(fixture.Api.Object, fixture.Registry).Get("test").Position);
    }
    /// <summary>Handles stay reachable and outside caller-provided content at each viewport edge.</summary>
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void HandleRemainsInsideViewportAtEveryEdge(double scale)
    {
        var viewport = new RectangleF(10, 20, 800, 600);
        foreach (var content in new[] { new RectangleF(10, 20, 90, 90), new RectangleF(720, 20, 90, 90), new RectangleF(10, 530, 90, 90), new RectangleF(720, 530, 90, 90) })
        {
            var handle = HudOverlayGroupControls.PlaceHandle(content, viewport, scale);
            Assert.True(viewport.Contains(handle)); Assert.False(handle.IntersectsWith(content));
        }
    }
    #endregion
}
