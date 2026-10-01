using Moq;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Characterizes owned-session cleanup, live opening state and interruption after real mutation.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AutoStashLifecycleTests
{
    #region Public API
    #region Interruptions
    /// <summary>Throws before or after an earlier real move and checks exact partial state and session ownership.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void TransferFailure_CurrentPartialStateAndSessionOwnership(bool crate, bool alreadyOpen, bool earlierMove)
    {
        var test = new ContainerLifecycleCase(crate);
        var transfer = test.Transfer;
        var first = transfer.Source(2);
        test.Seed(first, 2);
        var failing = earlierMove ? transfer.Source(5, 1) : first;
        if (earlierMove) test.Seed(failing, 5);
        var failure = new InvalidOperationException("controlled transfer interruption");
        failing.BeforeMove = _ => throw failure;
        transfer.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories)
            .Returns(alreadyOpen ? new List<IInventory> { transfer.Target } : []);
        var before = new InventorySnapshot(transfer.Fixture.BackpackInventory, transfer.Fixture.HotbarInventory, transfer.Target);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => test.Run()));

        if (earlierMove)
        {
            Assert.True(first.Empty);
            InventorySnapshot.AssertStack(failing, transfer.Item, 5);
            InventorySnapshot.AssertStack(transfer.Target[0], transfer.Item, 12);
            before.AssertUnchangedExcept(first, transfer.Target[0]);
            Assert.Equal(new[] { 0 }, transfer.ModifiedSlots);
            Assert.Equal("preserved", transfer.Target[0].Itemstack!.Attributes.GetString("fixture"));
        }
        else
        {
            before.AssertUnchangedExcept();
            Assert.Empty(transfer.ModifiedSlots);
        }
        before.AssertConserved();
        transfer.AssertSessions(alreadyOpen ? 0 : 1);
        // Current behavior: final block synchronization is bypassed when the service throws.
        test.AssertDirty(0);
        Assert.Single(failing.Attempts);
    }

    /// <summary>A real destination modification callback throws after mutation, before a move can return.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MutationCallbackFailure_CurrentStateIsNotRolledBack(bool crate, bool alreadyOpen)
    {
        var test = new ContainerLifecycleCase(crate);
        var transfer = test.Transfer;
        var source = transfer.Source(7);
        test.Seed(source, 7);
        transfer.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories)
            .Returns(alreadyOpen ? new List<IInventory> { transfer.Target } : []);
        var before = new InventorySnapshot(transfer.Fixture.BackpackInventory, transfer.Fixture.HotbarInventory, transfer.Target);
        var failure = new InvalidOperationException("controlled modification callback failure");
        Action<int> callback = _ => throw failure;
        transfer.Target.SlotModified += callback;
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => test.Run()));
        }
        finally
        {
            transfer.Target.SlotModified -= callback;
        }

        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(transfer.Target[0], transfer.Item, 17);
        Assert.Equal("preserved", transfer.Target[0].Itemstack!.Attributes.GetString("fixture"));
        before.AssertUnchangedExcept(source, transfer.Target[0]);
        before.AssertConserved();
        Assert.Equal(new[] { 0 }, transfer.ModifiedSlots);
        transfer.AssertSessions(alreadyOpen ? 0 : 1);
        test.AssertDirty(0);
    }
    #endregion
    #region Live state and normal completion
    /// <summary>Changes capacity during opening and requires actual movement to use the resulting live destination.</summary>
    [Theory]
    [InlineData(false, 64, 0)]
    [InlineData(true, 64, 0)]
    [InlineData(false, 62, 2)]
    [InlineData(true, 62, 2)]
    public void OpeningCallback_ExecutionUsesChangedCapacity(bool crate, int updatedQuantity, int moved)
    {
        var test = new ContainerLifecycleCase(crate);
        var transfer = test.Transfer;
        var source = transfer.Source(7);
        test.Seed(source, 7);
        InventorySnapshot? afterOpening = null;
        transfer.Fixture.InventoryManagerMock.Setup(manager => manager.OpenInventory(transfer.Target))
            .Callback(() =>
            {
                test.Seed(transfer.Target[0], updatedQuantity);
                afterOpening = new InventorySnapshot(transfer.Fixture.BackpackInventory, transfer.Fixture.HotbarInventory, transfer.Target);
            }).Returns(new object());

        Assert.Equal(moved > 0, test.Run());

        Assert.NotNull(afterOpening);
        InventorySnapshot.AssertStack(source, transfer.Item, 7 - moved);
        InventorySnapshot.AssertStack(transfer.Target[0], transfer.Item, updatedQuantity + moved);
        Assert.Equal("preserved", source.Itemstack!.Attributes.GetString("fixture"));
        Assert.Equal("preserved", transfer.Target[0].Itemstack!.Attributes.GetString("fixture"));
        afterOpening.AssertUnchangedExcept(moved == 0 ? [] : [source, transfer.Target[0]]);
        afterOpening.AssertConserved();
        transfer.AssertSessions(1);
        test.AssertDirty(moved > 0 ? 1 : 0);
        Assert.Equal(moved > 0, transfer.ModifiedSlots.Count > 0);
    }

    /// <summary>Separates full-capacity rejection from partial success and protects final dirty notifications.</summary>
    [Theory]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    [InlineData(false, 61)]
    [InlineData(true, 61)]
    public void NormalCompletion_OnlyActualMovementMarksDirty(bool crate, int initialQuantity)
    {
        var test = new ContainerLifecycleCase(crate);
        var transfer = test.Transfer;
        test.Seed(transfer.Target[0], initialQuantity);
        var source = transfer.Source(7);
        test.Seed(source, 7);
        var before = new InventorySnapshot(transfer.Fixture.BackpackInventory, transfer.Fixture.HotbarInventory, transfer.Target);
        int moved = 64 - initialQuantity;

        Assert.Equal(moved > 0, test.Run());

        InventorySnapshot.AssertStack(source, transfer.Item, 7 - moved);
        InventorySnapshot.AssertStack(transfer.Target[0], transfer.Item, 64);
        Assert.Equal("preserved", source.Itemstack!.Attributes.GetString("fixture"));
        Assert.Equal("preserved", transfer.Target[0].Itemstack!.Attributes.GetString("fixture"));
        before.AssertUnchangedExcept(moved == 0 ? [] : [source, transfer.Target[0]]);
        before.AssertConserved();
        transfer.AssertSessions(moved == 0 ? 0 : 1);
        test.AssertDirty(moved == 0 ? 0 : 1);
        Assert.Equal(moved > 0, transfer.ModifiedSlots.Count > 0);
    }
    #endregion
    #endregion
}
