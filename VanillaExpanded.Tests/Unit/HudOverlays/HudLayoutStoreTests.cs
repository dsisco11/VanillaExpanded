using System.Text.Json;
using Moq;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises snapshot authority and each recoverable native write boundary.</summary>
public sealed class HudLayoutStoreTests
{
    #region Public API
    /// <summary>Failed assignments before mutation preserve committed state and all native coordinates.</summary>
    [Fact]
    public void FailedSnapshotWriteKeepsOldStateAndMirrors()
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object);
        Assert.True(store.Commit(Snapshot(12))); string before = fixture.Values[HudLayoutStore.SettingKey];
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Throws(new IOException());
        Assert.False(store.Commit(Snapshot(84))); Assert.Equal(before, fixture.Values[HudLayoutStore.SettingKey]);
        Assert.Equal(12, Assert.Single(store.Current.Containers).X); Assert.Equal(12, fixture.Positions[Key].X);
    }
    /// <summary>A throwing setter is accepted only when exact readback proves the candidate was stored.</summary>
    [Fact]
    public void ThrowAfterAssignmentUsesExactReadback()
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object);
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>())
            .Callback<string, string>((key, value) => { fixture.Values[key] = value; throw new IOException(); });
        Assert.True(store.Commit(Snapshot(84))); Assert.Equal(84, fixture.Positions[Key].X);
        Assert.Equal(84, Assert.Single(new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object).Current.Containers).X);
    }
    /// <summary>Unreadable ambiguous writes suspend interaction rather than guessing which topology committed.</summary>
    [Fact]
    public void AmbiguousReadbackSuspendsWrites()
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object);
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Throws(new IOException());
        fixture.Strings.Setup(value => value.Get(HudLayoutStore.SettingKey, It.IsAny<string>())).Throws(new IOException());
        Assert.False(store.Commit(Snapshot(84))); Assert.True(store.ReadOnly); Assert.True(store.Uncertain); Assert.False(store.Reset()); Assert.Empty(fixture.Positions);
    }
    /// <summary>Recovery adopts the exact accepted snapshot after a temporary readback failure ends.</summary>
    [Fact]
    public void UncertainAssignmentRecoversBeforeFurtherInteraction()
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); bool unreadable = true;
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>())
            .Callback<string, string>((key, value) => { fixture.Values[key] = value; throw new IOException(); });
        fixture.Strings.Setup(value => value.Get(HudLayoutStore.SettingKey, It.IsAny<string>()))
            .Returns((string key, string fallback) => unreadable ? throw new IOException() : fixture.Values.GetValueOrDefault(key, fallback)!);
        Assert.False(store.Commit(Snapshot(84))); Assert.True(store.Uncertain);
        unreadable = false; store.Recover(); store.RepairMirrors();
        Assert.False(store.Uncertain); Assert.False(store.ReadOnly); Assert.Equal(84, Assert.Single(store.Current.Containers).X); Assert.Equal(84, fixture.Positions[Key].X);
    }    /// <summary>Invalid metadata discovered after ambiguity exposes only read-only defaults instead of stale custom state.</summary>
    [Theory]
    [InlineData("malformed-json")] [InlineData("{\"Version\":99,\"Initialized\":true,\"Containers\":[]}")]
    public void UncertainRecoveryToInvalidMetadataDiscardsStaleCustomState(string invalid)
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); Assert.True(store.Commit(Snapshot(12)));
        bool unreadable = true;
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Throws(new IOException());
        fixture.Strings.Setup(value => value.Get(HudLayoutStore.SettingKey, It.IsAny<string>()))
            .Returns((string key, string fallback) => unreadable ? throw new IOException() : fixture.Values.GetValueOrDefault(key, fallback)!);
        Assert.False(store.Commit(Snapshot(84))); Assert.True(store.Uncertain);
        fixture.Values[HudLayoutStore.SettingKey] = invalid; unreadable = false; store.Recover();
        Assert.True(store.ReadOnly); Assert.False(store.Uncertain); Assert.False(store.HasSnapshot); Assert.Empty(store.Current.Containers);
        Assert.Equal(invalid, fixture.Values[HudLayoutStore.SettingKey]);
        fixture.Strings.SetupSet(value => value[HudLayoutStore.SettingKey] = It.IsAny<string>()).Callback<string, string>((key, value) => fixture.Values[key] = value);
        Assert.True(store.Reset()); Assert.False(store.ReadOnly); Assert.Empty(store.Current.Containers);
    }    /// <summary>Mirror failure cannot roll back a committed snapshot and is repaired on a safe retry.</summary>
    [Fact]
    public void MirrorFailureRepairsFromSnapshotNeverTheReverse()
    {
        using var fixture = new HudLayoutFixture(); bool fail = true;
        fixture.Gui.Setup(value => value.SetDialogPosition(Key, It.IsAny<Vec2i>())).Callback<string, Vec2i>((key, position) => { if (fail) throw new IOException(); fixture.Positions[key] = position; });
        var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); Assert.True(store.Commit(Snapshot(84))); Assert.Empty(fixture.Positions);
        fail = false; fixture.Positions[Key] = new Vec2i(-1, -1); store.RepairMirrors(); Assert.Equal(84, fixture.Positions[Key].X);
    }
    /// <summary>Malformed and newer snapshots stay untouched until reset, which preserves unrelated keys.</summary>
    [Theory]
    [InlineData("broken-json")] [InlineData("{\"Version\":99}")]
    public void InvalidMetadataIsReadOnlyUntilExplicitReset(string data)
    {
        using var fixture = new HudLayoutFixture(); fixture.Values[HudLayoutStore.SettingKey] = data; fixture.Positions["other:dialog"] = new Vec2i(4, 7);
        var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); Assert.True(store.ReadOnly); Assert.False(store.Commit(Snapshot(84)));
        Assert.Equal(data, fixture.Values[HudLayoutStore.SettingKey]); Assert.True(store.Reset()); Assert.False(store.ReadOnly); Assert.Empty(store.Current.Containers); Assert.True(store.Current.Initialized); Assert.True(fixture.Positions.ContainsKey("other:dialog"));
    }
    /// <summary>Validation rejects duplicate members, bad enums, foreign mirror keys and invalid numeric geometry.</summary>
    [Fact]
    public void InvalidCandidatesNeverReachStorage()
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); var container = Assert.Single(Snapshot(12).Containers);
        var invalid = new[] {
            Snapshot(12) with { Containers = new[] { container, container with { Id = "other", Key = "vanillaexpanded:hud:custom:other" } } },
            Snapshot(12) with { Containers = new[] { container with { Padding = double.NaN } } },
            Snapshot(12) with { Containers = new[] { container with { Tree = container.Tree with { Direction = (HudOverlayDirection)999 } } } },
            Snapshot(12) with { RetiredKeys = new[] { "unrelated:dialog" } },
            Snapshot(12) with { Containers = new[] { container with { Key = "vanillaexpanded:hud:custom:wrong-owner" } } },
            Snapshot(12) with { Containers = new[] { container with { TemplateId = " " } } }
        };
        foreach (var candidate in invalid) Assert.False(store.Commit(candidate)); Assert.Equal(0, fixture.Writes);
    }
    /// <summary>Explicit reset remains available when a valid snapshot has exhausted the revision counter.</summary>
    [Fact]
    public void ResetCanReplaceMaximumRevisionSnapshot()
    {
        using var fixture = new HudLayoutFixture(); fixture.Values[HudLayoutStore.SettingKey] = JsonSerializer.Serialize(Snapshot(12) with { Revision = long.MaxValue });
        var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object);
        Assert.True(store.Reset()); Assert.Empty(store.Current.Containers); Assert.True(store.Current.Initialized);
        Assert.Empty(new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object).Current.Containers);
    }    /// <summary>Reset persists cleanup inventory for unavailable integrations before their mirrors are erased.</summary>
    [Fact]
    public void ResetRetainsOwnedCleanupForUnavailableMembers()
    {
        using var fixture = new HudLayoutFixture(); var store = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); store.Commit(Snapshot(84));
        Assert.True(store.Reset()); Assert.False(fixture.Positions.ContainsKey(Key)); Assert.Contains(Key, store.Current.RetiredKeys);
        fixture.Positions[Key] = new Vec2i(99, 99); var restored = new HudLayoutStore(fixture.Strings.Object, fixture.Gui.Object); Assert.Empty(restored.Current.Containers); Assert.False(fixture.Positions.ContainsKey(Key));
    }
    #endregion
    #region Private
    private const string Key = "vanillaexpanded:hud:custom:sample";
    /// <summary>Creates a well-formed unavailable integration checkpoint without registered defaults.</summary>
    private static HudLayoutSnapshot Snapshot(int x) => new() { Containers = new[] { new HudLayoutContainer { Id = "sample", Key = Key, X = x, Y = 23, Tree = new HudLayoutNode { Indicator = "integration:unknown" } } } };
    #endregion
}
