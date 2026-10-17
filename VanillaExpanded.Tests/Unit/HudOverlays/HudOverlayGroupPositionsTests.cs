using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies isolated native preferences and effective anchor selection for arbitrary registered groups.</summary>
public sealed class HudOverlayGroupPositionsTests
{
    #region Public API
    /// <summary>Group and indicator namespaces remain distinct even when their registered identities match.</summary>
    [Fact]
    public void KeysAreStableAndDisjoint()
    {
        string[] ids = { "world-status", "vitals", "mod:custom" };
        var keys = ids.SelectMany(id => new[] { HudOverlayPositionKeys.Group(id), HudOverlayPositionKeys.Indicator(id) }).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (string id in ids)
        {
            Assert.Equal("vanillaexpanded:hud:group:" + id, HudOverlayPositionKeys.Group(id));
            Assert.Equal("vanillaexpanded:hud:indicator:" + id, HudOverlayPositionKeys.Indicator(id));
        }
    }

    /// <summary>Reset and release write only the selected group's key and another session restores the committed origin.</summary>
    [Fact]
    public void NativeCommitAndResetRemainIsolatedAcrossGroups()
    {
        var stored = new Dictionary<string, Vec2i>();
        var gui = new Mock<IGuiAPI>();
        gui.Setup(value => value.GetDialogPosition(It.IsAny<string>())).Returns((string key) => stored.GetValueOrDefault(key)!);
        gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()))
            .Callback<string, Vec2i>((key, position) => { if (position == null) stored.Remove(key); else stored[key] = position; });
        var positions = new HudOverlayGroupPositions(gui.Object);
        var first = positions.Get("first"); var second = positions.Get("second");
        first.SetMovable(true); second.SetMovable(true);
        first.Begin(PointF.Empty, PointF.Empty, 1); first.Commit(new PointF(41.7f, 52.3f));
        second.Begin(PointF.Empty, PointF.Empty, 1); second.Commit(new PointF(83.2f, 94.8f));
        first.SetMovable(false);
        Assert.Null(first.Position);
        Assert.Equal(new PointF(83, 94), second.Position);
        Assert.Single(stored);
        var restored = new HudOverlayGroupPositions(gui.Object);
        Assert.Null(restored.Get("first").Position);
        Assert.Equal(second.Position, restored.Get("second").Position);
        Assert.Same(second, positions.Get("second"));
        gui.Verify(value => value.SetDialogPosition(second.Key, It.IsAny<Vec2i>()), Times.Once);
    }

    /// <summary>Saved and preview origins use screen availability, while cancellation and reset recover their former anchor.</summary>
    [Fact]
    public void EffectiveAnchorTracksPreviewSavedPreferenceAndReset()
    {
        var gui = new Mock<IGuiAPI>();
        var positions = new HudOverlayGroupPositions(gui.Object);
        var group = new HudOverlayGroup("custom", new HudOverlayPlacement("missing-native", HudOverlayPoint.RightBottom, HudOverlayPoint.LeftTop));
        var movement = positions.Get(group.Id);
        Assert.Equal(group.Placement.TargetId, positions.TargetId(group));
        movement.SetMovable(true);
        movement.Begin(new PointF(13, 27), PointF.Empty, 1);
        Assert.Equal(HudOverlayAnchorContext.ScreenTargetId, positions.TargetId(group));
        movement.Move(new PointF(4, 8));
        positions.Cancel();
        Assert.Null(movement.Position);
        Assert.Equal(group.Placement.TargetId, positions.TargetId(group));
        movement.Begin(new PointF(13, 27), PointF.Empty, 1); movement.Commit(new PointF(17, 35));
        Assert.Equal(HudOverlayAnchorContext.ScreenTargetId, positions.TargetId(group));
        movement.Begin(movement.Position!.Value, PointF.Empty, 1); movement.Move(new PointF(6, 7));
        positions.Cancel();
        Assert.Equal(new PointF(17, 35), movement.Position);
        movement.SetMovable(false);
        Assert.Equal(group.Placement.TargetId, positions.TargetId(group));
    }
    #endregion
}
