using Moq;
using VanillaExpanded.AutoStashing.Targets;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects adapter resolution and acquisition boundaries without rendering or launching a runtime.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class AutoStashTargetTests
{
    #region Public API
    /// <summary>Reads persisted bag contents without loading a workspace, slots, sessions or owner storage.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttachedResolution_IsReadOnly(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        var before = test.Snapshot();

        var target = Assert.IsType<AttachedBagAutoStashTarget>(AttachedBagAutoStashTarget.Resolve(
            test.Fixture.World, test.Host, test.AttachmentMock.Object, 0));
        Assert.Equal(new[] { 10, 4 }, target.GetContents().Select(stack => stack!.StackSize));

        Assert.False(target.IsPrepared);
        Assert.Null(test.Workspace);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
        test.Verify(before, false, 0);
    }

    /// <summary>Complete no-candidate operations avoid mutable workspace preparation for either bag implementation.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoCandidates_DoNotPrepareAttachedInventory(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Other, 3);
        var before = test.Snapshot();

        Assert.False(test.Run());

        Assert.Null(test.Workspace);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
        test.Verify(before, false, 0);
    }

    /// <summary>Caller-owned sessions remain open; an acquired session is released only once.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InventoryAdapter_ReleasesOnlyItsOwnSession(bool alreadyOpen)
    {
        var test = new TransferCase();
        test.Fixture.InventoryManagerMock.Setup(owner => owner.OpenedInventories)
            .Returns(alreadyOpen ? new List<IInventory> { test.Target } : []);
        var target = new InventoryAutoStashTarget(test.Target);
        target.Acquire(test.Fixture.Player);
        target.Release(test.Fixture.Player);
        target.Release(test.Fixture.Player);
        test.AssertSessions(alreadyOpen ? 0 : 1);
        Assert.Empty(test.ModifiedSlots);
    }
    #endregion
}
