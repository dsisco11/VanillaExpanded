using Moq;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects bloomery synchronization when execution stops after a real ore deposit.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class BloomeryFailureTests
{
    #region Public API
    /// <summary>Preserves actual deposited ore and synchronizes it when a later move or callback fails.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedDeposit_SynchronizesActualPartialState(bool callbackFailure)
    {
        var test = new BloomeryCase();
        var first = test.Source(test.Ore, 2);
        var second = test.Source(test.Ore, 5, 1);
        test.Source(test.Invalid, 3, 9, hotbar: true);
        var before = test.Snapshot();
        var failure = new InvalidOperationException("controlled bloomery interruption");
        if (callbackFailure) first.AfterMove = _ => throw failure;
        else second.BeforeMove = _ => throw failure;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => test.Run()));

        Assert.True(first.Empty);
        test.AssertSlot(second, test.Ore, 5);
        test.AssertSlot(test.Target.TestInventory[1], test.Ore, 2);
        before.AssertUnchangedExcept(first, test.Target.TestInventory[1]);
        before.AssertConserved();
        Assert.Equal(new[] { 1 }, test.ModifiedSlots);
        test.TargetMock.Verify(target => target.MarkDirty(true, null!), Times.Once());
        test.TargetMock.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Once());
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never());
        test.Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never());
        test.Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never());
        Assert.Equal(callbackFailure ? 0 : 1, second.Attempts.Count);
    }
    #endregion
}
