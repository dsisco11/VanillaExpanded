using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects attached-bag eligibility and execution when persisted contents are absent or empty.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AttachedBagEmptyContentsTests
{
    #region Public API
    /// <summary>Empty bag representations produce no candidates or transfers without initializing or persisting the bag.</summary>
    [Theory]
    [InlineData("uninitialized")]
    [InlineData("initialized")]
    [InlineData("custom-null")]
    public void EmptyContents_LeaveAssessmentAndExecutionUntouched(string state)
    {
        var test = new AttachedContainerCase(state != "custom-null");
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 3);
        // Exercise the stock engine's null return as well as initialized empty slots and a custom bag's null return.
        if (state == "uninitialized") test.BagStack.Attributes.RemoveAttribute("backpack");
        if (state == "custom-null")
            test.BagMock.Setup(bag => bag.GetContents(It.IsAny<ItemStack>(), It.IsAny<IWorldAccessor>()))
                .Returns((ItemStack[])null!);
        if (state != "initialized") Assert.Null(test.Bag.GetContents(test.BagStack, test.Fixture.World));
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Attachments);
        var dirtySlots = test.Attachments.DirtySlots.OrderBy(index => index).ToArray();
        var ownerAttributes = test.Host.WatchedAttributes.ToJsonToken().ToString();

        var assessment = AutoStashService.AssessAttached(test.Fixture.World, test.Fixture.Player, test.Attachments[0]);
        Assert.False(EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));
        var result = AutoStashService.StashAttached(test.Fixture.World, test.Player, test.Host, test.AttachmentMock.Object, 0);

        Assert.False(assessment.HasCandidates);
        Assert.Empty(assessment.CandidateItemIds);
        Assert.Empty(assessment.AvailableItemIds);
        Assert.Empty(assessment.DisplayStacks);
        Assert.Equal(AutoStashCapacity.Unknown, assessment.Capacity);
        Assert.Equal(AutoStashOutcome.NoCandidates, result.Outcome);
        Assert.Equal(0, result.MovedQuantity);
        Assert.Null(test.Workspace);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        Assert.Equal(dirtySlots, test.Attachments.DirtySlots.OrderBy(index => index).ToArray());
        Assert.Equal(ownerAttributes, test.Host.WatchedAttributes.ToJsonToken().ToString());
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
        test.BagMock.Verify(bag => bag.Store(It.IsAny<ItemStack>(), It.IsAny<ItemSlotBagContent>()), Times.Never);
        test.AttachmentMock.Verify(attachment => attachment.storeInv(), Times.Never);
        test.AttachmentsMock.Verify(inventory => inventory.MarkSlotDirty(It.IsAny<int>()), Times.Never);
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        test.Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
    }
    #endregion
}
