using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Exercises registered entity requests against both durable attached-bag paths.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class ServerEntityRequestTests
{
    #region Public API
    /// <summary>Accepts exactly six blocks and rejects just beyond with otherwise eligible bag contents.</summary>
    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 6)]
    [InlineData(true, 6.0001)]
    [InlineData(false, 6.0001)]
    public void DistanceBoundary_UsesCurrentPlayerPosition(bool vanilla, double distance)
    {
        var test = new AttachedContainerCase(vanilla);
        using var request = new ServerRequestCase(test.Fixture);
        InstallAttachment(test);
        request.PlayerEntity.Pos.SetPos(distance, 0, 0);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 1));
        var source = test.Fixture.BackpackInventory[0];
        source.Itemstack = test.Stack(test.Item, 3);
        Assert.True(EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));
        request.Fixture.WorldMock.Setup(world => world.GetEntityById(test.Host.EntityId)).Returns(test.Host);
        var before = test.Snapshot();

        request.EntityRequest(test.Host.EntityId, 0);

        bool moved = distance == 6;
        test.AssertPersisted(moved ? 13 : 10, 1);
        if (moved) Assert.True(source.Empty);
        else InventorySnapshot.AssertStack(source, test.Item, 3);
        test.Verify(before, moved, moved && vanilla ? 1 : 0, moved ? [source] : []);
        request.AssertNoSyntheticTransfer();
    }

    /// <summary>Rejects missing entities, missing attachment behavior, and invalid indices without persistence or sessions.</summary>
    [Theory]
    [InlineData(true, "entity")]
    [InlineData(false, "entity")]
    [InlineData(true, "behavior")]
    [InlineData(false, "behavior")]
    [InlineData(true, "negative")]
    [InlineData(false, "negative")]
    [InlineData(true, "past-end")]
    [InlineData(false, "past-end")]
    public void InvalidRequest_PreservesOtherwiseEligibleContents(bool vanilla, string failure)
    {
        var test = new AttachedContainerCase(vanilla);
        using var request = new ServerRequestCase(test.Fixture);
        InstallAttachment(test, failure != "behavior");
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 1));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 3);
        Assert.True(EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));
        request.Fixture.WorldMock.Setup(world => world.GetEntityById(test.Host.EntityId)).Returns(failure == "entity" ? null! : test.Host);
        var before = test.Snapshot();

        request.EntityRequest(test.Host.EntityId, failure == "negative" ? -1 : failure == "past-end" ? test.Attachments.Count : 0);

        test.AssertPersisted(10, 1);
        test.Verify(before, false, 0);
        request.AssertNoSyntheticTransfer();
    }

    /// <summary>Uses current persisted capacity after a positive client assessment instead of trusting stale eligibility.</summary>
    [Theory]
    [InlineData(true, 62)]
    [InlineData(false, 62)]
    [InlineData(true, 64)]
    [InlineData(false, 64)]
    public void CapacityChangesBeforeReceipt_UsesRemainingCapacity(bool vanilla, int current)
    {
        var test = new AttachedContainerCase(vanilla);
        using var request = new ServerRequestCase(test.Fixture);
        InstallAttachment(test);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 64));
        var source = test.Fixture.BackpackInventory[0];
        source.Itemstack = test.Stack(test.Item, 3);
        Assert.True(EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));
        test.SeedContents(test.Stack(test.Item, current), test.Stack(test.Unrelated, 64));
        request.Fixture.WorldMock.Setup(world => world.GetEntityById(test.Host.EntityId)).Returns(test.Host);
        var before = test.Snapshot();

        request.EntityRequest(test.Host.EntityId, 0);

        bool moved = current < 64;
        test.AssertPersisted(64, 64);
        InventorySnapshot.AssertStack(source, test.Item, moved ? 1 : 3);
        Assert.Equal("preserved", source.Itemstack!.Attributes.GetString("fixture"));
        test.Verify(before, moved, moved && vanilla ? 1 : 0, moved ? [source] : []);
        request.AssertNoSyntheticTransfer();
    }

    /// <summary>Rejects formerly matching sources when the durable bag changes to a different item before receipt.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContentTypeChangesBeforeReceipt_UsesCurrentMatchingPolicy(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        using var request = new ServerRequestCase(test.Fixture);
        InstallAttachment(test);
        test.SeedContents(test.Stack(test.Item, 10), null);
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 3);
        Assert.True(EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));
        test.SeedContents(test.Stack(test.Other, 10), null);
        request.Fixture.WorldMock.Setup(world => world.GetEntityById(test.Host.EntityId)).Returns(test.Host);
        var before = test.Snapshot();

        request.EntityRequest(test.Host.EntityId, 0);

        var persisted = test.ReadPersisted();
        Assert.Same(test.Other, persisted[0]!.Collectible);
        Assert.Equal(10, persisted[0]!.StackSize);
        Assert.Equal("preserved", persisted[0]!.Attributes.GetString("fixture"));
        test.Verify(before, false, 0);
        request.AssertNoSyntheticTransfer();
    }
    #endregion

    #region Private
    /// <summary>Installs server-side behavior ownership so the real entity lookup resolves the attachment.</summary>
    private static void InstallAttachment(AttachedContainerCase test, bool present = true)
    {
        typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(test.Host,
            new EntityProperties { Server = new EntityServerProperties([], new Dictionary<string, JsonObject>()) });
        if (present) test.Host.SidedProperties.Behaviors.Add(test.AttachmentMock.Object);
    }
    #endregion
}
