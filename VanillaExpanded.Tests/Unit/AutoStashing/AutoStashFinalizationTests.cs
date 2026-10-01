using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects target finalization order, persistence failures and original exception preservation.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AutoStashFinalizationTests
{
    #region Public API
    /// <summary>Attempts both finalization and cleanup while preserving the first failure and reporting secondary failures.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleFailures_PreserveOriginalExceptionAndStillClose(bool transferFailure)
    {
        var test = new TransferCase();
        test.Target[0].Itemstack = new ItemStack(test.Item, 10);
        var source = test.Source(2);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        var original = new InvalidOperationException("transfer failed after mutation");
        var persistence = new InvalidOperationException("finalization failed");
        var cleanup = new InvalidOperationException("cleanup failed");
        if (transferFailure) source.AfterMove = _ => throw original;
        var order = new List<string>();
        test.Fixture.InventoryManagerMock.Setup(manager => manager.CloseInventoryAndSync(test.Target))
            .Callback(() => { order.Add("close"); throw cleanup; });

        var caught = Assert.Throws<InvalidOperationException>(() => AutoStashTransferService.AutoStashToInventory(
            test.Fixture.World, test.Fixture.Player, "test", test.Target, new BlockPos(0), "test", _ => true,
            finalizeChanges: () => { order.Add("finalize"); throw persistence; }));

        Assert.Same(transferFailure ? original : persistence, caught);
        Assert.Equal(new[] { "finalize", "close" }, order);
        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 12);
        before.AssertUnchangedExcept(source, test.Target[0]);
        before.AssertConserved();
        test.AssertSessions(1);
        AssertReported(test.Fixture.LoggerMock, cleanup);
        if (transferFailure) AssertReported(test.Fixture.LoggerMock, persistence);
    }

    /// <summary>Suppresses finalization for a normally returned zero move while finalizing an uncertain throwing attempt.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoProgress_FinalizesOnlyWhenMutationIsUncertain(bool throws)
    {
        var test = new TransferCase();
        test.Target[0].Itemstack = new ItemStack(test.Item, 10);
        var source = test.Source(2);
        source.Reject = true;
        var failure = new InvalidOperationException("uncertain move");
        if (throws) source.BeforeMove = _ => throw failure;
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        int finalized = 0;
        Func<int> run = () => AutoStashTransferService.AutoStashToInventory(test.Fixture.World, test.Fixture.Player,
            "test", test.Target, new BlockPos(0), "test", _ => true, finalizeChanges: () => finalized++);

        if (throws) Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => run()));
        else Assert.Equal(0, run());

        Assert.Equal(throws ? 1 : 0, finalized);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        test.AssertSessions(1);
    }

    /// <summary>Reports failed owner persistence and guarantees session cleanup occurs after all persistence attempts.</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    public void OwnerPersistenceFailure_PreservesTransferFailureAndSessionOwnership(bool vanilla, bool alreadyOpen, bool transferFailure)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.AttachmentMock.Object.storeInv();
        test.ClearObservations();
        if (alreadyOpen)
            test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories)
                .Returns(new List<IInventory> { test.LoadWorkspace().WrapperInv });
        var source = new ObservedTransferSlot(test.Fixture.BackpackInventory) { Itemstack = test.Stack(test.Item, 2) };
        test.Fixture.BackpackInventory[0] = source;
        var original = new InvalidOperationException("transfer interrupted");
        var persistence = new InvalidOperationException("owner persistence failed");
        if (transferFailure) source.AfterMove = _ => throw original;
        int saveAttempts = 0;
        int failureAttempt = vanilla ? 2 : 1;
        // Retain the real vanilla slot-callback save; fail only the final owner-persistence attempt.
        test.AttachmentMock.Setup(attachment => attachment.storeInv()).Callback(() =>
        {
            if (++saveAttempts == failureAttempt) throw persistence;
        }).CallBase();
        bool closedAfterPersistence = false;
        test.Fixture.InventoryManagerMock.Setup(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()))
            .Callback(() =>
            {
                Assert.Equal(failureAttempt, saveAttempts);
                test.AssertPersisted(12, 4);
                closedAfterPersistence = true;
            });

        Assert.Same(transferFailure ? original : persistence,
            Assert.Throws<InvalidOperationException>(() => test.Run()));

        Assert.True(source.Empty);
        test.AssertPersisted(12, 4);
        Assert.Equal(failureAttempt, saveAttempts);
        test.AttachmentsMock.Verify(inventory => inventory.MarkSlotDirty(0), Times.Once());
        int owned = vanilla && !alreadyOpen ? 1 : 0;
        Assert.Equal(owned == 1, closedAfterPersistence);
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Exactly(owned));
        test.Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Exactly(owned));
        if (transferFailure) AssertReported(test.Fixture.LoggerMock, persistence);
    }
    #endregion

    #region Private
    /// <summary>Checks that the exact secondary exception is included in an error log.</summary>
    private static void AssertReported(Mock<ILogger> logger, Exception exception)
    {
        Assert.Contains(logger.Invocations, invocation => invocation.Method.Name == nameof(ILogger.Error)
            && invocation.Arguments.OfType<object[]>().Any(arguments => arguments.Contains(exception)));
    }
    #endregion
}
