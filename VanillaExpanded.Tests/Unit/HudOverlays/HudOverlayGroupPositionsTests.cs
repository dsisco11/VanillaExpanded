using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks registration-independent ownership and snapshot precedence over legacy positions.</summary>
public sealed class HudOverlayGroupPositionsTests
{
    #region Public API
    /// <summary>Group and independent native key namespaces remain stable and disjoint.</summary>
    [Fact]
    public void KeysAreStableAndDisjoint()
    {
        string[] ids = { "world-status", "vitals", "mod:custom" };
        var keys = ids.SelectMany(id => new[] { HudOverlayPositionKeys.Group(id), HudOverlayPositionKeys.Indicator(id) }).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }
    /// <summary>Legacy origins migrate once; reset initialization prevents stale mirrors being imported again.</summary>
    [Fact]
    public void LegacyImportAndResetDoNotResurrectCoordinates()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a");
        string key = HudOverlayPositionKeys.Group("test"); fixture.Positions[key] = new Vec2i(41, 52);
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.True(containers.Get("test").Movable); Assert.Equal(new PointF(41, 52), containers.Get("test").Position);
        Assert.True(containers.Reset()); fixture.Positions[key] = new Vec2i(99, 88);
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.False(restored.Get("test").Movable); Assert.Null(restored.Get("test").Position);
    }
    /// <summary>Anchored mode changes retain a screen origin and reset restores anchor eligibility.</summary>
    [Fact]
    public void EffectiveAnchorTracksCapturedOriginAndGlobalReset()
    {
        using var fixture = new HudLayoutFixture(); var registration = fixture.Add("test:a", anchor: "missing-native");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.Equal("missing-native", containers.TargetId(registration));
        containers.SetMovable("test", true, new PointF(13, 27)); Assert.Equal("screen", containers.TargetId(registration));
        containers.SetMovable("test", false, new PointF(13, 27)); Assert.Equal("screen", containers.TargetId(registration));
        containers.Reset(); Assert.Equal("missing-native", containers.TargetId(registration));
    }
    #endregion
}
