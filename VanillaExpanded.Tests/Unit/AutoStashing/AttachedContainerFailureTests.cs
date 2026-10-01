using Moq;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects interrupted bag movement through persistence and session cleanup.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AttachedContainerFailureTests
{
    #region Public API
    /// <summary>Checks live movement survives bag and owner reload after transfer or callback interruption.</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void InterruptedMovement_PersistsLiveContentsBeforeOwnedSessionCleanup(bool vanilla, bool alreadyOpen, bool callbackFailure)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        // Persist the initial owner so the failure can be compared with a real reload, not missing setup.
        test.AttachmentMock.Object.storeInv();
        test.ClearObservations();
        var first = new ObservedTransferSlot(test.Fixture.BackpackInventory) { Itemstack = test.Stack(test.Item, 2) };
        var second = new ObservedTransferSlot(test.Fixture.BackpackInventory) { Itemstack = test.Stack(test.Item, 5) };
        test.Fixture.BackpackInventory[0] = first;
        test.Fixture.BackpackInventory[1] = second;
        test.Fixture.HotbarInventory[9].Itemstack = test.Stack(test.Other, 3);
        if (alreadyOpen)
        {
            test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories)
                .Returns(new List<IInventory> { test.LoadWorkspace().WrapperInv });
        }
        var failure = new InvalidOperationException("controlled attached-container interruption");
        InventoryGeneric? live = null;
        InventorySnapshot? beforeLive = null;
        var modified = new List<int>();
        Action<int> observe = index => modified.Add(index);
        first.BeforeMove = destination =>
        {
            live = (InventoryGeneric)destination.Inventory;
            beforeLive = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, live);
            live.SlotModified += observe;
        };
        if (callbackFailure) first.AfterMove = _ => throw failure;
        else second.BeforeMove = _ => throw failure;
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => test.Run()));
        }
        finally
        {
            if (live is not null) live.SlotModified -= observe;
        }

        Assert.NotNull(live);
        Assert.NotNull(beforeLive);
        Assert.True(first.Empty);
        InventorySnapshot.AssertStack(second, test.Item, 5);
        InventorySnapshot.AssertStack(live[0], test.Item, 12);
        Assert.Equal("preserved", live[0].Itemstack!.Attributes.GetString("fixture"));
        beforeLive.AssertUnchangedExcept(first, live[0]);
        beforeLive.AssertConserved();
        Assert.Equal(new[] { 0 }, modified);
        test.AssertPersisted(12, 4);
        test.BagMock.Verify(bag => bag.Store(It.IsAny<ItemStack>(), It.IsAny<ItemSlotBagContent>()),
            Times.Exactly(vanilla ? 3 : 2));
        test.AttachmentMock.Verify(attachment => attachment.storeInv(), Times.Exactly(vanilla ? 2 : 1));
        test.AttachmentsMock.Verify(inventory => inventory.MarkSlotDirty(It.IsAny<int>()), Times.Once());
        int owned = vanilla && !alreadyOpen ? 1 : 0;
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Exactly(owned));
        test.Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Exactly(owned));
        if (vanilla)
        {
            test.Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(live), Times.Exactly(owned));
        }
        test.Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never());

        // Both paths now persist live contents despite interruption.
        // Reload the real owner serialization to ensure the temporary path cannot lose applied progress.
        test.ReloadOwnerBag();
        test.AssertPersisted(12, 4);
        Assert.Equal(callbackFailure ? 0 : 1, second.Attempts.Count);
    }
    #endregion
}
