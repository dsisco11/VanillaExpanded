using Moq;
using VanillaExpanded.AutoStashing.Transfers;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects the concrete transfer boundary while full-operation regressions cover target behavior.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class InventoryTransferExecutorTests
{
    #region Public API
    #region Instruction validity
    /// <summary>Rejects nonpositive requests without making an engine attempt.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Instruction_RequiresPositiveQuantity(int quantity)
    {
        var test = new TransferCase();
        var source = test.Source(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryTransfer(source, test.Target[0], quantity,
            EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge));
        Assert.Empty(source.Attempts);
    }

    /// <summary>Rejects missing concrete slots before an instruction can be executed.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Instruction_RequiresBothSlots(bool missingSource)
    {
        var test = new TransferCase();
        var source = test.Source(3);
        Assert.Throws<ArgumentNullException>(() => new InventoryTransfer(missingSource ? null! : source,
            missingSource ? test.Target[0] : null!, 3,
            EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge));
        Assert.Empty(source.Attempts);
    }

    /// <summary>Rejects missing execution context or instruction without touching inventories.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Executor_RequiresContextAndInstruction(bool missingWorld)
    {
        var test = new TransferCase();
        var source = test.Source(3);
        var transfer = new InventoryTransfer(source, test.Target[0], 3,
            EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge);
        Assert.Throws<ArgumentNullException>(() => InventoryTransferExecutor.Execute(
            missingWorld ? null! : test.Fixture.World, missingWorld ? transfer : null!));
        Assert.Empty(source.Attempts);
    }
    #endregion

    #region Engine outcomes
    /// <summary>Retains a convenience-overload override instead of bypassing it with an equivalent-looking operation.</summary>
    [Fact]
    public void Executor_PreservesConvenienceVirtualDispatch()
    {
        var test = new TransferCase();
        var source = new Mock<ItemSlot>(test.Fixture.BackpackInventory);
        source.Setup(slot => slot.TryPutInto(test.Fixture.World, test.Target[0], 7)).Returns(2);
        source.Setup(slot => slot.TryPutInto(test.Target[0], ref It.Ref<ItemStackMoveOperation>.IsAny)).Returns(5);

        var transfer = new InventoryTransfer(source.Object, test.Target[0], 7);
        var result = InventoryTransferExecutor.Execute(test.Fixture.World, transfer);

        Assert.Equal(InventoryTransferInvocation.EngineDefaults, transfer.Invocation);
        Assert.Equal(7, result.RequestedQuantity);
        Assert.Equal(2, result.MovedQuantity);
        Assert.Null(result.RequiredPriority);
        source.Verify(slot => slot.TryPutInto(test.Fixture.World, test.Target[0], 7), Times.Once);
        source.Verify(slot => slot.TryPutInto(test.Target[0], ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
    }

    /// <summary>Checks actual convenience defaults at the inner virtual engine boundary.</summary>
    [Fact]
    public void Executor_ConvenienceRetainsEngineDefaultSettings()
    {
        var test = new TransferCase();
        var source = new Mock<ItemSlot>(test.Fixture.BackpackInventory) { CallBase = true };
        source.Setup(slot => slot.TryPutInto(test.Target[0], ref It.Ref<ItemStackMoveOperation>.IsAny))
            .Callback(new MoveCallback((ItemSlot destination, ref ItemStackMoveOperation operation) =>
            {
                Assert.Same(test.Fixture.World, operation.World);
                Assert.Equal(7, operation.RequestedQuantity);
                Assert.Equal(EnumMouseButton.Left, operation.MouseButton);
                Assert.Equal((EnumModifierKey)0, operation.Modifiers);
                Assert.Equal(EnumMergePriority.AutoMerge, operation.CurrentPriority);
                Assert.Null(operation.RequiredPriority);
            }))
            .Returns(0);

        var result = InventoryTransferExecutor.Execute(test.Fixture.World,
            new InventoryTransfer(source.Object, test.Target[0], 7));

        Assert.Equal(0, result.MovedQuantity);
        source.Verify(slot => slot.TryPutInto(test.Target[0], ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Once);
    }

    /// <summary>Passes concrete settings and preserves both the actual return value and updated required priority.</summary>
    [Fact]
    public void Executor_PreservesSettingsAndActualResult()
    {
        var test = new TransferCase();
        var source = new Mock<ItemSlot>(test.Fixture.BackpackInventory);
        source.Setup(slot => slot.TryPutInto(test.Target[0], ref It.Ref<ItemStackMoveOperation>.IsAny))
            .Callback(new MoveCallback((ItemSlot destination, ref ItemStackMoveOperation operation) =>
            {
                Assert.Same(test.Fixture.World, operation.World);
                Assert.Same(test.Target[0], destination);
                Assert.Equal(7, operation.RequestedQuantity);
                Assert.Equal(EnumMouseButton.Right, operation.MouseButton);
                Assert.Equal(EnumModifierKey.CTRL, operation.Modifiers);
                Assert.Equal(EnumMergePriority.DirectMerge, operation.CurrentPriority);
                Assert.Null(operation.RequiredPriority);
                operation.RequiredPriority = EnumMergePriority.AutoMerge;
                operation.RequestedQuantity = 2;
            }))
            .Returns(2);

        var result = InventoryTransferExecutor.Execute(test.Fixture.World,
            new InventoryTransfer(source.Object, test.Target[0], 7,
                EnumMouseButton.Right, EnumModifierKey.CTRL, EnumMergePriority.DirectMerge));

        Assert.Equal(7, result.RequestedQuantity);
        Assert.Equal(2, result.MovedQuantity);
        Assert.Equal(EnumMergePriority.AutoMerge, result.RequiredPriority);
        source.Verify(slot => slot.TryPutInto(test.Target[0], ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Once);
        source.Verify(slot => slot.TryPutInto(It.IsAny<IWorldAccessor>(), It.IsAny<ItemSlot>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>Reports a zero engine result and its direct-priority request without counting requested items as moved.</summary>
    [Fact]
    public void Executor_ReportsDeferredMergeWithoutMovement()
    {
        var test = new TransferCase(1);
        var item = new DeferredMergeItem(test.Fixture.Api);
        var source = test.Source(3, item: item);
        test.Target[0].Itemstack = new ItemStack(item, 1);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = InventoryTransferExecutor.Execute(test.Fixture.World,
            new InventoryTransfer(source, test.Target[0], 3,
                EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge));

        Assert.Equal(3, result.RequestedQuantity);
        Assert.Equal(0, result.MovedQuantity);
        Assert.Equal(EnumMergePriority.DirectMerge, result.RequiredPriority);
        snapshot.AssertUnchangedExcept();
        Assert.Empty(test.ModifiedSlots);
    }

    /// <summary>Delegates partial mutation to the engine and reports its exact return with no invented priority.</summary>
    [Fact]
    public void Executor_ReportsPartialEngineMovement()
    {
        var test = new TransferCase(1);
        var source = test.Source(5);
        test.Target[0].MaxSlotStackSize = 2;
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = InventoryTransferExecutor.Execute(test.Fixture.World,
            new InventoryTransfer(source, test.Target[0], 5,
                EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge));

        Assert.Equal(5, result.RequestedQuantity);
        Assert.Equal(2, result.MovedQuantity);
        Assert.Null(result.RequiredPriority);
        InventorySnapshot.AssertStack(source, test.Item, 3);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 2);
        snapshot.AssertConserved();
    }

    /// <summary>Retains the original callback failure even when the engine already moved items.</summary>
    [Fact]
    public void Executor_PropagatesOriginalFailureAfterMutation()
    {
        var test = new TransferCase(1);
        var source = test.Source(3);
        var failure = new InvalidOperationException("callback failed");
        source.AfterMove = _ => throw failure;
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => InventoryTransferExecutor.Execute(
            test.Fixture.World, new InventoryTransfer(source, test.Target[0], 3,
                EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge))));

        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 3);
        snapshot.AssertConserved();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Allows Moq to observe the engine operation passed by reference.</summary>
    private delegate void MoveCallback(ItemSlot destination, ref ItemStackMoveOperation operation);
    #endregion
}
