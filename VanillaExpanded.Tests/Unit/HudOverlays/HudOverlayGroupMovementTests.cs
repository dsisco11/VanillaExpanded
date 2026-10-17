using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks native position ownership and independent preview geometry without feature preparations or engine disk writes.</summary>
public sealed class HudOverlayGroupMovementTests
{
    private const string GroupId = "world-status";
    private static readonly string DialogKey = HudOverlayPositionKeys.Group(GroupId);
    #region Public API
    /// <summary>Missing positions retain registered placement and cannot move before the player enables the grip.</summary>
    [Fact]
    public void MissingPositionStartsFixedWithoutWrites()
    {
        var gui = new Mock<IGuiAPI>();
        var movement = new HudOverlayGroupMovement(gui.Object, GroupId);
        movement.Begin(new PointF(10, 20), new PointF(100, 100), 1);
        Assert.False(movement.Movable);
        Assert.False(movement.Dragging);
        Assert.Null(movement.Position);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }

    /// <summary>Total pointer displacement converts pixels once at the captured scale and cancellation restores the saved origin.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PreviewUsesTotalDisplacementAndCancelDoesNotPersist(double scale)
    {
        var gui = new Mock<IGuiAPI>();
        gui.Setup(value => value.GetDialogPosition(DialogKey)).Returns(new Vec2i(20, 30));
        var movement = new HudOverlayGroupMovement(gui.Object, GroupId);
        movement.Begin(new PointF(20, 30), new PointF(100, 100), scale);
        movement.Move(new PointF(160, 190));
        movement.Move(new PointF(130, 160));
        Assert.Equal(new PointF(20 + (float)(30 / scale), 30 + (float)(60 / scale)), movement.Position);
        movement.Cancel();
        Assert.Equal(new PointF(20, 30), movement.Position);
        Assert.False(movement.Dragging);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
    }

    /// <summary>A successful drop stores clamped displayed coordinates, while a new controller restores those native values.</summary>
    [Fact]
    public void CommitStoresDisplayedOriginOnceAndRestoresThroughNativeApi()
    {
        Vec2i? stored = null;
        var gui = new Mock<IGuiAPI>();
        gui.Setup(value => value.GetDialogPosition(DialogKey)).Returns(() => stored!);
        gui.Setup(value => value.SetDialogPosition(DialogKey, It.IsAny<Vec2i>()))
            .Callback<string, Vec2i>((_, position) => stored = position);
        var movement = new HudOverlayGroupMovement(gui.Object, GroupId);
        movement.SetMovable(true);
        movement.Begin(new PointF(20, 30), PointF.Empty, 1);
        movement.Move(new PointF(10000, 10000));
        movement.Commit(new PointF(150.9f, 240.8f));
        movement.Commit(new PointF(1, 1));
        Assert.Equal(new PointF(150, 240), movement.Position);
        Assert.False(movement.Dragging);
        gui.Verify(value => value.SetDialogPosition(DialogKey, It.IsAny<Vec2i>()), Times.Once);
        var restored = new HudOverlayGroupMovement(gui.Object, GroupId);
        Assert.True(restored.Movable);
        Assert.Equal(new PointF(150, 240), restored.Position);
    }

    /// <summary>Fixed mode clears only the known group preference and discards an active preview.</summary>
    [Fact]
    public void FixedModeRestoresDefaultAndClearsOnlyItsKey()
    {
        var gui = new Mock<IGuiAPI>();
        gui.Setup(value => value.GetDialogPosition(DialogKey)).Returns(new Vec2i(20, 30));
        var movement = new HudOverlayGroupMovement(gui.Object, GroupId);
        movement.Begin(new PointF(20, 30), PointF.Empty, 1);
        movement.SetMovable(false);
        Assert.Null(movement.Position);
        Assert.False(movement.Dragging);
        Assert.False(movement.Movable);
        gui.Verify(value => value.SetDialogPosition(DialogKey, null!), Times.Once);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Once);
    }

    /// <summary>Above/below switching never moves the supplied content rectangle and keeps controls reachable at viewport edges.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void HandleRemainsInsideViewportAtEveryEdge(double scale)
    {
        var viewport = new RectangleF(10, 20, 800, 600);
        foreach (var content in new[] { new RectangleF(10, 20, 90, 90), new RectangleF(720, 20, 90, 90),
            new RectangleF(10, 530, 90, 90), new RectangleF(720, 530, 90, 90) })
        {
            var handle = HudOverlayGroupControls.PlaceHandle(content, viewport, scale);
            Assert.True(viewport.Contains(handle));
            Assert.False(handle.IntersectsWith(content));
            if (content.Top == viewport.Top) Assert.True(handle.Top >= content.Bottom);
            else Assert.True(handle.Bottom <= content.Top);
        }
    }
    #endregion
}
