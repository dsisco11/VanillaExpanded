using System.Drawing;
using System.Text.Json;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks grouping previews and authoritative commits without involving feature ownership.</summary>
public sealed class HudGroupingTransactionTests
{
    #region Public API
    /// <summary>Every attachment and alignment survives the transaction boundary for individuals and whole groups.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AllAttachmentPoliciesPersistWithoutDuplicateMembers(bool wholeGroup)
    {
        foreach (var side in Enum.GetValues<HudSnapSide>())
        foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
        {
            using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "source"); fixture.Add("test:b", "source"); fixture.Add("test:c", "target"); fixture.Add("test:d", "target");
            var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(40, 50); containers.SetMovable("source", true, origin);
            var members = new Dictionary<string, PointF> { ["test:a"] = origin, ["test:b"] = new PointF(90, 50) };
            Assert.True(containers.Begin("source", wholeGroup ? null : "test:a", origin, members, origin));
            var candidate = Candidate("target", wholeGroup ? null : "test:c") with { Side = side, Alignment = alignment };
            Assert.True(containers.PreviewJoin(candidate, new PointF(200, 150))); Assert.True(containers.Commit(new PointF(200, 150)));
            var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var target = restored.Owner("test:c")!;
            var pair = wholeGroup ? target.Tree : target.Tree.First!;
            Assert.Equal(alignment, pair.Alignment); Assert.Equal(side is HudSnapSide.Left or HudSnapSide.Right ? HudOverlayDirection.Horizontal : HudOverlayDirection.Vertical, pair.Direction);
            Assert.Equal(4, restored.Resolved.SelectMany(value => value.Tree.Leaves()).Count()); Assert.Equal(4, restored.Resolved.SelectMany(value => value.Tree.Leaves()).Distinct().Count());
        }
    }

    /// <summary>Reinserting into the original source remainder remains one flat group, and cancellation restores the old arrangement.</summary>
    [Fact]
    public void SameOriginalContainerInsertionIsTransactional()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b"); fixture.Add("test:c");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(40, 50); containers.SetMovable("test", true, origin);
        var original = Assert.Single(containers.Resolved); int writes = fixture.Writes;
        Assert.True(containers.Begin("test", "test:a", origin, new Dictionary<string, PointF>(), origin));
        Assert.True(containers.PreviewJoin(Candidate("test", "test:c"), origin));
        Assert.Equal(3, Assert.Single(containers.Resolved).Tree.Leaves().Distinct().Count()); Assert.Equal(writes, fixture.Writes);
        containers.Cancel(); Assert.Equal(original, Assert.Single(containers.Resolved)); Assert.Equal(writes, fixture.Writes);
    }

    /// <summary>Independent items form one fresh group; preview writes nothing and restart restores its mode and origin.</summary>
    [Fact]
    public void CreateGroupCommitsMembershipAndFixedDestinationTogether()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "source"); fixture.Add("test:b", "target");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        var sourceOrigin = new PointF(40, 50); var targetOrigin = new PointF(200, 150);
        containers.SetMovable("source", true, sourceOrigin); int writes = fixture.Writes;
        Assert.True(containers.Begin("source", null, sourceOrigin, new Dictionary<string, PointF>()));
        Assert.True(containers.PreviewJoin(Candidate("target", "test:b"), targetOrigin));
        var joined = Assert.Single(containers.Resolved); Assert.NotEqual("source", joined.Id); Assert.NotEqual("target", joined.Id);
        Assert.Equal(new[] { "test:a", "test:b" }, joined.Tree.Leaves().Order()); Assert.False(joined.Movable);
        Assert.Equal(writes, fixture.Writes); Assert.True(containers.Commit(targetOrigin)); Assert.Equal(writes + 1, fixture.Writes);
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var saved = Assert.Single(restored.Resolved);
        Assert.Equal(joined.Id, saved.Id); Assert.False(saved.Movable); Assert.Equal((int)targetOrigin.X, saved.X); Assert.Equal((int)targetOrigin.Y, saved.Y);
        Assert.False(restored.Begin(saved.Id, "test:a", targetOrigin, new Dictionary<string, PointF>()));
    }

    /// <summary>Whole-group merging preserves both trees and a Fixed target while cancellation restores every source record.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MergePreservesArrangementsAndCanCancel(bool cancel)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "source"); fixture.Add("test:b", "source"); fixture.Add("test:c", "target"); fixture.Add("test:d", "target");
        var source = Record("source", Pair("test:a", "test:b", HudOverlayDirection.Vertical), true, 40, 50);
        var target = Record("target", Pair("test:c", "test:d", HudOverlayDirection.Horizontal), false, 200, 150);
        fixture.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] { source, target } });
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); int writes = fixture.Writes;
        Assert.True(containers.Begin(source.Id, null, new PointF(source.X!.Value, source.Y!.Value), new Dictionary<string, PointF>()));
        Assert.True(containers.PreviewJoin(Candidate(target.Id, null), new PointF(target.X!.Value, target.Y!.Value)));
        var merged = Assert.Single(containers.Resolved); Assert.Equal(target.Id, merged.Id); Assert.False(merged.Movable);
        Assert.Equal(source.Tree, merged.Tree.Second); Assert.Equal(target.Tree, merged.Tree.First); Assert.Equal(writes, fixture.Writes);
        if (cancel) containers.Cancel(); else Assert.True(containers.Commit(new PointF(target.X.Value, target.Y.Value)));
        if (cancel) Assert.Equal(new[] { source, target }, containers.Resolved);
        else
        {
            var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); Assert.Equal(merged, Assert.Single(restored.Resolved));
            Assert.Equal(4, restored.Resolved.SelectMany(value => value.Tree.Leaves()).Distinct().Count());
        }
        Assert.Equal(writes + (cancel ? 0 : 1), fixture.Writes);
    }

    /// <summary>Changing snap selection rebuilds from the original drag transaction rather than accumulating insertions.</summary>
    [Fact]
    public void RepeatedPreviewAndBypassKeepOneCopyAndNoWrites()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "source"); fixture.Add("test:b", "target"); fixture.Add("test:c", "target");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(40, 50);
        containers.SetMovable("source", true, origin); int writes = fixture.Writes;
        Assert.True(containers.Begin("source", null, origin, new Dictionary<string, PointF>()));
        for (int index = 0; index < 3; index++)
        {
            containers.ClearSnap(new PointF(origin.X + index, origin.Y));
            Assert.True(containers.PreviewJoin(Candidate("target", index % 2 == 0 ? "test:b" : "test:c"), new PointF(200, 150)));
            Assert.Equal(3, containers.Resolved.SelectMany(value => value.Tree.Leaves()).Distinct().Count());
            Assert.Equal(3, containers.Resolved.SelectMany(value => value.Tree.Leaves()).Count());
        }
        containers.ClearSnap(new PointF(90, 100)); Assert.Equal(2, containers.Resolved.Count); Assert.Equal("source", containers.Owner("test:a")!.Id);
        Assert.Equal(writes, fixture.Writes); Assert.True(containers.Commit(new PointF(90, 100))); Assert.Equal(2, containers.Resolved.Count);
    }

    /// <summary>A failed snapshot write restores both participants and never repairs mirrors from an unaccepted merge.</summary>
    [Fact]
    public void FailedCommitRestoresOriginalContainers()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "source"); fixture.Add("test:b", "target");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(40, 50); containers.SetMovable("source", true, origin);
        var originals = containers.Resolved.ToArray(); var oldSnapshot = fixture.Values[HudLayoutStore.SettingKey];
        Assert.True(containers.Begin("source", null, origin, new Dictionary<string, PointF>())); Assert.True(containers.PreviewJoin(Candidate("target", "test:b"), new PointF(200, 150)));
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Throws(new IOException()); fixture.Gui.Invocations.Clear();
        Assert.False(containers.Commit(new PointF(200, 150))); Assert.Equal(originals, containers.Resolved);
        Assert.Equal(oldSnapshot, fixture.Values[HudLayoutStore.SettingKey]); fixture.Gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vintagestory.API.MathTools.Vec2i>()), Times.Never);
    }

    /// <summary>Readback recognizes an accepted merge when native settings throw after publishing its snapshot.</summary>
    [Fact]
    public void ThrowAfterAssignmentRecoversAcceptedMerge()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "source"); fixture.Add("test:b", "target");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(40, 50); containers.SetMovable("source", true, origin);
        Assert.True(containers.Begin("source", null, origin, new Dictionary<string, PointF>())); Assert.True(containers.PreviewJoin(Candidate("target", "test:b"), new PointF(200, 150)));
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Callback<string, string>((key, value) => { fixture.Values[key] = value; throw new IOException(); });
        Assert.True(containers.Commit(new PointF(200, 150))); var accepted = Assert.Single(containers.Resolved);
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); Assert.Equal(accepted, Assert.Single(restored.Resolved));
    }
    #endregion

    #region Private
    /// <summary>Supplies an already selected adjacency; geometry selection is tested independently.</summary>
    private static HudSnapCandidate Candidate(string target, string? member) => new("selected", new HudSnapTarget(target, member, new RectangleF(200, 150, 80, 60), 5), HudSnapSide.Right,
        HudOverlayCrossAlignment.Center, new PointF(285, 170), 0, RectangleF.Empty);
    /// <summary>Builds a native-key-compatible authoritative container.</summary>
    private static HudLayoutContainer Record(string id, HudLayoutNode tree, bool movable, int x, int y) => new()
    { Id = id, Key = HudOverlayPositionKeys.Group(id), Tree = tree, Movable = movable, X = x, Y = y };
    /// <summary>Supplies distinct authored internal arrangements for merge preservation.</summary>
    private static HudLayoutNode Pair(string a, string b, HudOverlayDirection direction) => new()
    { First = new HudLayoutNode { Indicator = a }, Second = new HudLayoutNode { Indicator = b }, Direction = direction, Alignment = HudOverlayCrossAlignment.End, Gap = 3 };
    #endregion
}
