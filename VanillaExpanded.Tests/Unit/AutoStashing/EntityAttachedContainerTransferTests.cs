using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects complete attached-bag transfers, persistent contents and workspace ownership.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class EntityAttachedContainerTransferTests
{
    #region Public API
    #region Transfers and capacity
    /// <summary>Transfers both player sources into a matching bag while preserving unrelated contents.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MatchingContents_TransfersAndPersistsBothSources(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        test.Fixture.HotbarInventory[0].Itemstack = test.Stack(test.Item, 3);
        test.Fixture.BackpackInventory[1].Itemstack = test.Stack(test.Other, 9);
        var before = test.Snapshot();

        Assert.True(test.Run());

        Assert.True(test.Fixture.BackpackInventory[0].Empty);
        Assert.True(test.Fixture.HotbarInventory[0].Empty);
        test.AssertPersisted(20, 4);
        test.Verify(before, true, vanilla ? 1 : 0,
            test.Fixture.BackpackInventory[0], test.Fixture.HotbarInventory[0]);
    }

    /// <summary>Records actual limited movement and persists the exact remainder and destination quantity.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PartialCapacity_PersistsActualGainAndSourceRemainder(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 61), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        var before = test.Snapshot();

        Assert.True(test.Run());

        InventorySnapshot.AssertStack(test.Fixture.BackpackInventory[0], test.Item, 4);
        Assert.Equal("preserved", test.Fixture.BackpackInventory[0].Itemstack!.Attributes.GetString("fixture"));
        test.AssertPersisted(64, 4);
        test.Verify(before, true, vanilla ? 1 : 0, test.Fixture.BackpackInventory[0]);
    }

    /// <summary>Distinguishes full, nonmatching and empty bags without persisting unchanged operations.</summary>
    [Theory]
    [InlineData(true, "full")]
    [InlineData(false, "full")]
    [InlineData(true, "nonmatching")]
    [InlineData(false, "nonmatching")]
    [InlineData(true, "empty")]
    [InlineData(false, "empty")]
    public void UnavailableContents_DoNotMoveOrPersist(bool vanilla, string contents)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(contents == "empty" ? null : test.Stack(test.Item, contents == "full" ? 64 : 10),
            contents == "empty" ? null : test.Stack(test.Unrelated, 64));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(contents == "nonmatching" ? test.Other : test.Item, 7);
        var before = test.Snapshot();

        Assert.False(test.Run());

        test.Verify(before, false, 0);
    }
    #endregion
    #region Validation and ownership
    /// <summary>Rejects invalid attachment indices before loading or storing any bag inventory.</summary>
    [Theory]
    [InlineData(true, -1)]
    [InlineData(false, -1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public void InvalidIndex_LeavesAllContentsAndLifecycleUntouched(bool vanilla, int index)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        var before = test.Snapshot();

        Assert.False(test.Run(index));

        test.Verify(before, false, 0);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
    }

    /// <summary>Isolates empty, non-bag and zero-capacity targets with otherwise matching player sources.</summary>
    [Theory]
    [InlineData(true, "empty")]
    [InlineData(false, "empty")]
    [InlineData(true, "nonbag")]
    [InlineData(false, "nonbag")]
    [InlineData(true, "zero")]
    [InlineData(false, "zero")]
    public void InvalidAttachment_DoesNotLoadMoveOrPersist(bool vanilla, string attachment)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        if (attachment == "empty") test.Attachments[0].Itemstack = null;
        else if (attachment == "nonbag") test.Attachments[0].Itemstack = test.Stack(test.Other, 1);
        else test.BagMock.Setup(bag => bag.GetQuantitySlots(It.IsAny<ItemStack>())).Returns(0);
        var before = test.Snapshot();

        Assert.False(test.Run());

        test.Verify(before, false, 0);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
    }

    /// <summary>Reaches the real workspace load rejection after the outer positive-capacity prerequisite.</summary>
    [Fact]
    public void WorkspaceLoadFailure_DoesNotMoveOpenOrPersist()
    {
        var test = new AttachedContainerCase(true);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        test.BagMock.SetupSequence(bag => bag.GetQuantitySlots(It.IsAny<ItemStack>())).Returns(2).Returns(0);
        var before = test.Snapshot();

        Assert.False(test.Run());

        test.BagMock.Verify(bag => bag.GetQuantitySlots(It.IsAny<ItemStack>()), Times.Exactly(2));
        Assert.NotNull(test.Workspace);
        Assert.Null(test.Workspace.WrapperInv);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
        test.Verify(before, false, 0);
    }

    /// <summary>Closes only newly acquired workspace sessions and never manages temporary inventories.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void SessionOwnership_PreservesAlreadyOpenInventories(bool vanilla, bool alreadyOpen)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        // A preloaded vanilla workspace is the exact inventory supplied to session management.
        IInventory open = vanilla ? test.LoadWorkspace().WrapperInv : test.Fixture.HotbarInventory;
        var opened = alreadyOpen ? new List<IInventory> { open } : new List<IInventory>();
        test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns(opened);
        var before = test.Snapshot();

        Assert.True(test.Run());

        test.AssertPersisted(17, 4);
        test.Verify(before, true, vanilla && !alreadyOpen ? 1 : 0, test.Fixture.BackpackInventory[0]);
        Assert.Equal(alreadyOpen ? new[] { open } : Array.Empty<IInventory>(), opened);
    }
    #endregion
    #endregion
}
