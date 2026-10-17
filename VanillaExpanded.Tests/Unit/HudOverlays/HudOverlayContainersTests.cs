using System.Drawing;
using System.Text.Json;
using Moq;
using Vintagestory.API.MathTools;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies flat effective membership and transactional individual detachment.</summary>
public sealed class HudOverlayContainersTests
{
    #region Public API
    /// <summary>Cross-registration trees retain unavailable leaves while new registrations use filtered defaults once.</summary>
    [Fact]
    public void ExplicitCrossRegistrationOwnershipAndNewDefaultsRemainDisjoint()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a", "first"); fixture.Add("test:b", "second");
        var tree = HudLayoutNode.FromMembers(new[] { "test:a", "test:b", "integration:unknown" }, new HudOverlayPacking())!;
        fixture.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(new HudLayoutSnapshot { Containers = new[] { new HudLayoutContainer { Id = "first", Key = HudOverlayPositionKeys.Group("first"), X = 10, Y = 20, Tree = tree } } });
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); fixture.Add("test:new", "first"); containers.Synchronize();
        Assert.Equal("first", containers.Owner("test:a")!.Id); Assert.Equal("first", containers.Owner("test:b")!.Id);
        Assert.Equal("default-remainder:first", containers.Owner("test:new")!.Id);
        Assert.Equal("first", containers.Owner("integration:unknown")!.Id);
        var leaves = containers.Resolved.SelectMany(value => value.Tree.Leaves()).ToArray(); Assert.Equal(leaves.Length, leaves.Distinct().Count());
        Assert.Equal("screen", containers.TargetId(fixture.Add("test:later", "second")));
    }
    /// <summary>Repeatedly materialized remainder containers cannot collide when more indicators later register.</summary>
    [Fact]
    public void NewRegistrationsAllocateDistinctSuccessiveDefaultRemainders()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        containers.SetMovable("test", true, new PointF(10, 20));
        fixture.Add("test:b"); containers.Synchronize(); var firstRemainder = containers.Owner("test:b")!;
        Assert.True(containers.SetMovable(firstRemainder.Id, true, new PointF(40, 50)));
        fixture.Add("test:c"); containers.Synchronize(); var secondRemainder = containers.Owner("test:c")!;
        Assert.NotEqual(firstRemainder.Id, secondRemainder.Id); Assert.NotEqual(firstRemainder.Key, secondRemainder.Key);
        Assert.True(containers.SetMovable(secondRemainder.Id, true, new PointF(70, 80)));
        fixture.Add("test:d"); containers.Synchronize();
        Assert.Equal(4, containers.Resolved.Select(value => value.Id).Distinct().Count());
        Assert.Equal(4, containers.Resolved.Select(value => value.Key).Distinct().Count());
        Assert.Equal(new[] { "test:a", "test:b", "test:c", "test:d" }, containers.Resolved.SelectMany(value => value.Tree.Leaves()).Order());
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.Equal(containers.Owner("test:c")!.Id, restored.Owner("test:c")!.Id);
        Assert.Equal(4, restored.Resolved.SelectMany(value => value.Tree.Leaves()).Distinct().Count());
    }    /// <summary>Invalid recovery cannot resurrect an obsolete legacy import after a snapshot had superseded it.</summary>
    [Fact]
    public void InvalidRecoveryShowsFixedDefaultsInsteadOfStaleLegacyImport()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Positions[HudOverlayPositionKeys.Group("test")] = new Vec2i(41, 52);
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); Assert.True(containers.Get("test").Movable);
        Assert.True(containers.SetMovable("test", true, new PointF(80, 90)));
        bool unreadable = true;
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Throws(new IOException());
        fixture.Strings.Setup(value => value.Get(HudLayoutStore.SettingKey, It.IsAny<string>()))
            .Returns((string key, string fallback) => unreadable ? throw new IOException() : fixture.Values.GetValueOrDefault(key, fallback)!);
        Assert.False(containers.SetMovable("test", false, new PointF(80, 90))); Assert.True(containers.ReadOnly);
        const string invalid = "malformed-layout"; fixture.Values[HudLayoutStore.SettingKey] = invalid; unreadable = false; containers.RepairMirrors();
        Assert.True(containers.ReadOnly); Assert.False(containers.Get("test").Movable); Assert.Null(containers.Get("test").Position);
        Assert.False(Assert.Single(containers.Resolved).Movable); Assert.Equal(invalid, fixture.Values[HudLayoutStore.SettingKey]);
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Callback<string, string>((key, value) => fixture.Values[key] = value);
        Assert.True(containers.Reset()); Assert.False(containers.ReadOnly); Assert.Null(containers.Get("test").Position);
    }    /// <summary>Detachment previews close the source gap, preserve unavailable membership, and cancellation restores everything without writes.</summary>
    [Fact]
    public void DetachCancellationRestoresCompleteTopologyAndOrigin()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b"); fixture.Add("test:c");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); var origin = new PointF(20, 30); containers.SetMovable("test", true, origin);
        int writes = fixture.Writes; var original = Assert.Single(containers.Resolved);
        var members = new Dictionary<string, PointF> { ["test:a"] = origin, ["test:b"] = new(60, 30), ["test:c"] = new(100, 30) };
        Assert.True(containers.Begin("test", "test:b", members["test:b"], members, origin));
        Assert.Equal(2, containers.Resolved.Count); Assert.Equal(new[] { "test:a", "test:c" }, containers.Owner("test:a")!.Tree.Leaves());
        containers.Move(new PointF(170, 90)); Assert.Equal(writes, fixture.Writes);
        containers.Cancel(); Assert.Equal(original, Assert.Single(containers.Resolved)); Assert.Equal(writes, fixture.Writes);
    }
    /// <summary>A committed free drop creates independent keys and singleton normalization captures the remaining displayed origin.</summary>
    [Fact]
    public void FreeDropNormalizesSingletonAndSurvivesRestart()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); containers.SetMovable("test", true, new PointF(20, 30));
        var members = new Dictionary<string, PointF> { ["test:a"] = new(32, 42), ["test:b"] = new(82, 42) };
        Assert.True(containers.Begin("test", "test:a", members["test:a"], members, new PointF(20, 30))); Assert.True(containers.Commit(new PointF(170, 90)));
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.Equal(HudOverlayPositionKeys.Indicator("test:a"), restored.Owner("test:a")!.Key); Assert.Equal(170, restored.Owner("test:a")!.X);
        Assert.Equal(82, restored.Owner("test:b")!.X); Assert.All(restored.Resolved, value => Assert.True(value.Movable));
        Assert.Equal(2, restored.Resolved.Count);
    }
    #endregion
}
