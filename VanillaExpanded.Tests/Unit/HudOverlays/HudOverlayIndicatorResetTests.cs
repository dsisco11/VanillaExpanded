using System.Drawing;
using System.Text.Json;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies selected-indicator reset without discarding unrelated custom layout ownership.</summary>
public sealed class HudOverlayIndicatorResetTests
{
    #region Public API
    /// <summary>A detached indicator rejoins untouched registered defaults and retains that placement after restart.</summary>
    [Fact]
    public void DetachedIndicatorReturnsToDerivedDefaultsAcrossRestart()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        containers.SetMovable("test", true, new PointF(30, 40));
        Assert.True(containers.Begin("test", "test:a", new PointF(30, 40), new Dictionary<string, PointF> { ["test:a"] = new(30, 40), ["test:b"] = new(60, 40) }, new PointF(30, 40)));
        Assert.True(containers.Commit(new PointF(200, 250)));
        var sibling = containers.Owner("test:b")!;
        Assert.True(containers.ResetIndicator("test:a"));
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        var reset = restored.Owner("test:a")!;
        Assert.Null(reset.X); Assert.Null(reset.Y); Assert.Null(reset.Attachment); Assert.False(reset.Movable);
        Assert.Equal("test", reset.TemplateId); Assert.Equal(sibling, restored.Owner("test:b"));
    }

    /// <summary>Reset removes only the selected leaf from a mixed custom tree; other groups retain complete records.</summary>
    [Fact]
    public void CustomizedDefaultGroupAndUnrelatedRecordsRemainUnchanged()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b"); fixture.Add("test:other", "other");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.True(containers.SetMovable("test", true, new PointF(30, 40)));
        Assert.True(containers.SetMovable("other", true, new PointF(220, 140)));
        var before = JsonSerializer.Deserialize<HudLayoutSnapshot>(fixture.Values[HudLayoutStore.SettingKey])!;
        var unrelated = before.Containers.Single(value => value.Id == "other");
        var oldDefault = before.Containers.Single(value => value.Id == "test");
        Assert.True(containers.ResetIndicator("test:a"));
        var after = JsonSerializer.Deserialize<HudLayoutSnapshot>(fixture.Values[HudLayoutStore.SettingKey])!;
        Assert.Equal(unrelated, after.Containers.Single(value => value.Id == "other"));
        Assert.Equal(oldDefault with { Tree = oldDefault.Tree.Remove("test:a")! }, after.Containers.Single(value => value.Id == "test"));
        var reset = containers.Owner("test:a")!;
        Assert.NotEqual("test", reset.Id); Assert.Equal("test", reset.TemplateId); Assert.Null(reset.X); Assert.False(reset.Movable);
        Assert.Equal(new[] { "test:a", "test:b", "test:other" }, containers.Resolved.SelectMany(value => value.Tree.Leaves()).Order());
    }

    /// <summary>A failed authoritative write leaves both membership and coordinate mirrors unchanged.</summary>
    [Fact]
    public void FailedResetDoesNotPublishPartialLayout()
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); fixture.Add("test:b");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        containers.SetMovable("test", true, new PointF(30, 40));
        var saved = fixture.Values[HudLayoutStore.SettingKey]; var original = containers.Resolved.ToArray();
        var mirrors = fixture.Positions.ToDictionary(value => value.Key, value => value.Value);
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Throws(new IOException());
        Assert.False(containers.ResetIndicator("test:a"));
        Assert.Equal(saved, fixture.Values[HudLayoutStore.SettingKey]); Assert.Equal(original, containers.Resolved);
        Assert.Equal(mirrors.OrderBy(value => value.Key), fixture.Positions.OrderBy(value => value.Key));
    }

    /// <summary>Unknown targets and unreadable layouts cannot silently replace unrelated saved state.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidResetIsReadOnly(bool unreadable)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a");
        if (unreadable) fixture.Values[HudLayoutStore.SettingKey] = "unreadable-layout";
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry); int writes = fixture.Writes;
        Assert.False(containers.ResetIndicator(unreadable ? "test:a" : "unknown")); Assert.Equal(writes, fixture.Writes);
        if (unreadable) Assert.Equal("unreadable-layout", fixture.Values[HudLayoutStore.SettingKey]);
    }
    #endregion
}
