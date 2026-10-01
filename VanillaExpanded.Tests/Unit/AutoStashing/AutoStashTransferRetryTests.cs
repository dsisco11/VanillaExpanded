using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Exercises deferred merge ordering and bounded observation of rejected retries.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class AutoStashTransferRetryTests
{
    #region Public API
    #region Deferred retries
    /// <summary>Proves ordinary destinations are exhausted before a deferred direct merge succeeds.</summary>
    [Fact]
    public void DeferredMerge_RunsAfterAutomaticAlternatives()
    {
        var test = new TransferCase();
        var item = new DeferredMergeItem(test.Fixture.Api);
        var source = test.Source(5, item: item);
        test.Target[0].Itemstack = new ItemStack(item, 1);
        test.Target[1].MaxSlotStackSize = 2;
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(5, test.Run());
        Assert.Equal(new[] { test.Target[0], test.Target[1], test.Target[0] }, source.Attempts.Select(a => a.Destination));
        Assert.Equal(new[] { EnumMergePriority.AutoMerge, EnumMergePriority.AutoMerge, EnumMergePriority.DirectMerge }, source.Attempts.Select(a => a.Priority));
        Assert.Equal(new[] { 5, 5, 3 }, source.Attempts.Select(a => a.Quantity));
        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], item, 4);
        InventorySnapshot.AssertStack(test.Target[1], item, 2);
        snapshot.AssertUnchangedExcept(source, test.Target[0], test.Target[1]);
        snapshot.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        test.AssertSessions(1);
    }

    /// <summary>Proves a direct rejection without a renewed priority request terminates without mutation.</summary>
    [Fact]
    public void DeferredMerge_RejectedDirectAttemptTerminates()
    {
        var test = new TransferCase(1);
        var item = new DeferredMergeItem(test.Fixture.Api) { RejectDirect = true };
        var source = test.Source(3, item: item);
        test.Target[0].Itemstack = new ItemStack(item, 1);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(0, test.Run());
        Assert.Equal(new[] { EnumMergePriority.AutoMerge, EnumMergePriority.DirectMerge }, source.Attempts.Select(a => a.Priority));
        snapshot.AssertUnchangedExcept();
        snapshot.AssertConserved();
        Assert.Empty(test.ModifiedSlots);
        test.AssertSessions(1);
    }

    /// <summary>Requires normal bounded termination even when a collectible repeatedly renews its direct request.</summary>
    [Fact]
    public void DeferredMerge_RepeatedDirectRequestMustTerminate()
    {
        var test = new TransferCase(1);
        var item = new DeferredMergeItem(test.Fixture.Api) { RejectDirect = true, RepeatRequiredPriority = true };
        var source = test.Source(3, item: item);
        var errors = new TransferErrorObservation(test);
        source.AttemptLimit = 4;
        test.Target[0].Itemstack = new ItemStack(item, 1);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        // The observer bounds a broken retry loop; throwing is not accepted as successful termination.
        try
        {
            Assert.Equal(0, test.Run());
            Assert.InRange(source.Attempts.Count, 1, 2);
            errors.AssertErrors(1);
        }
        finally
        {
            snapshot.AssertUnchangedExcept();
            snapshot.AssertConserved();
            Assert.Empty(test.ModifiedSlots);
            test.AssertSessions(1);
        }
    }

    #endregion
    #region Direct destinations
    /// <summary>Tries later populated and empty slots after the first direct candidate rejects, without sending failure feedback.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAlternatives_FirstRejectsLaterDestinationSucceeds(bool emptyAlternative)
    {
        var test = new TransferCase();
        var item = new DeferredMergeItem(test.Fixture.Api);
        var source = test.Source(3, item: item);
        var errors = new TransferErrorObservation(test);
        test.Target[0].Itemstack = new ItemStack(item, 1);
        if (!emptyAlternative) test.Target[1].Itemstack = new ItemStack(item, 2);
        // Force automatic rejection at the boundary; successful direct mutation remains engine-owned.
        source.BeforeMove = slot => source.Reject = source.Attempts.Last().Priority == EnumMergePriority.AutoMerge
            || ReferenceEquals(slot, test.Target[0]);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(3, test.Run());
        Assert.Equal(new[] { test.Target[0], test.Target[1], test.Target[0], test.Target[1] }, source.Attempts.Select(attempt => attempt.Destination));
        Assert.Equal(new[] { EnumMergePriority.AutoMerge, EnumMergePriority.AutoMerge, EnumMergePriority.DirectMerge, EnumMergePriority.DirectMerge }, source.Attempts.Select(attempt => attempt.Priority));
        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[1], item, emptyAlternative ? 3 : 5);
        snapshot.AssertUnchangedExcept(source, test.Target[1]);
        snapshot.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        errors.AssertErrors(0);
        test.AssertSessions(1);
    }

    /// <summary>Exhausts every empty or populated direct destination for multiple sources and reports one operation-wide error.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAlternatives_AllCandidatesRejectOnceAndNotifyOwnerOnce(bool populated)
    {
        var test = new TransferCase();
        var first = test.Source(3);
        var second = test.Source(2, hotbar: true);
        first.Reject = second.Reject = true;
        if (populated)
        {
            test.Target[0].Itemstack = new ItemStack(test.Item, 1);
            test.Target[1].Itemstack = new ItemStack(test.Item, 2);
        }
        var errors = new TransferErrorObservation(test);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(0, test.Run());
        foreach (var source in new[] { first, second })
        {
            Assert.Equal(new[] { test.Target[0], test.Target[1], test.Target[0], test.Target[1] }, source.Attempts.Select(attempt => attempt.Destination));
            Assert.Equal(new[] { EnumMergePriority.AutoMerge, EnumMergePriority.AutoMerge, EnumMergePriority.DirectMerge, EnumMergePriority.DirectMerge }, source.Attempts.Select(attempt => attempt.Priority));
        }
        snapshot.AssertUnchangedExcept();
        snapshot.AssertConserved();
        Assert.Empty(test.ModifiedSlots);
        errors.AssertErrors(1);
        test.AssertSessions(1);
    }

    /// <summary>Reevaluates a rejected direct destination after another destination makes finite positive progress.</summary>
    [Fact]
    public void DirectAlternatives_ProgressMakesEarlierRejectedSlotViable()
    {
        var test = new TransferCase();
        var item = new DeferredMergeItem(test.Fixture.Api);
        var source = test.Source(5, item: item);
        var errors = new TransferErrorObservation(test);
        test.Target[0].Itemstack = new ItemStack(item, 1);
        test.Target[1].Itemstack = new ItemStack(item, 2);
        test.Target[1].MaxSlotStackSize = 4;
        source.BeforeMove = slot =>
        {
            source.Reject = source.Attempts.Last().Priority == EnumMergePriority.AutoMerge
                || (ReferenceEquals(slot, test.Target[0]) && source.StackSize > 3);
            source.QuantityLimit = ReferenceEquals(slot, test.Target[1]) ? 2 : null;
        };
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(5, test.Run());
        Assert.Equal(new[] { test.Target[0], test.Target[1], test.Target[0], test.Target[1], test.Target[0] }, source.Attempts.Select(attempt => attempt.Destination));
        Assert.Equal(new[] { EnumMergePriority.AutoMerge, EnumMergePriority.AutoMerge, EnumMergePriority.DirectMerge, EnumMergePriority.DirectMerge, EnumMergePriority.DirectMerge }, source.Attempts.Select(attempt => attempt.Priority));
        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], item, 4);
        InventorySnapshot.AssertStack(test.Target[1], item, 4);
        snapshot.AssertUnchangedExcept(source, test.Target[0], test.Target[1]);
        snapshot.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        errors.AssertErrors(0);
        test.AssertSessions(1);
    }

    /// <summary>Discovers slots excluded by automatic eligibility, including when preflight has only direct capacity.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAlternatives_AutomaticallyExcludedSlotIsStillAttempted(bool rejectedAlternative)
    {
        var test = new TransferCase(rejectedAlternative ? 2 : 1);
        var source = test.Source(3);
        var errors = new TransferErrorObservation(test);
        int index = rejectedAlternative ? 1 : 0;
        var directSlot = new PriorityRestrictedSlot(test.Target);
        test.Target[index] = directSlot;
        source.BeforeMove = slot =>
        {
            source.Reject = !ReferenceEquals(slot, directSlot);
        };
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        // Engine TryPutInto rechecks default automatic eligibility and rejects this direct-only slot.
        Assert.Equal(0, test.Run());
        var selected = source.Attempts.Last();
        Assert.Same(directSlot, selected.Destination);
        Assert.Equal(EnumMergePriority.DirectMerge, selected.Priority);
        Assert.Equal(rejectedAlternative ? 3 : 1, source.Attempts.Count);
        InventorySnapshot.AssertStack(source, test.Item, 3);
        Assert.True(directSlot.Empty);
        snapshot.AssertUnchangedExcept();
        snapshot.AssertConserved();
        Assert.Empty(test.ModifiedSlots);
        errors.AssertErrors(1);
        test.AssertSessions(1);
    }

    #endregion
    #region Failure feedback
    /// <summary>Preserves actual partial progress and its remainder when a subsequent direct retry rejects.</summary>
    [Fact]
    public void DirectAlternatives_PartialProgressThenRejectionPreservesQuantityAndReportsError()
    {
        var test = new TransferCase(1);
        var item = new DeferredMergeItem(test.Fixture.Api);
        var source = test.Source(5, item: item);
        var errors = new TransferErrorObservation(test);
        source.QuantityLimit = 2;
        source.AfterMove = _ =>
        {
            if (source.Attempts.Last().Priority == EnumMergePriority.DirectMerge) source.Reject = true;
        };
        test.Target[0].Itemstack = new ItemStack(item, 1);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(2, test.Run());
        Assert.Equal(new[] { EnumMergePriority.AutoMerge, EnumMergePriority.DirectMerge, EnumMergePriority.DirectMerge }, source.Attempts.Select(attempt => attempt.Priority));
        Assert.Equal(new[] { 5, 5, 3 }, source.Attempts.Select(attempt => attempt.Quantity));
        InventorySnapshot.AssertStack(source, item, 3);
        InventorySnapshot.AssertStack(test.Target[0], item, 3);
        snapshot.AssertUnchangedExcept(source, test.Target[0]);
        snapshot.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        errors.AssertErrors(1);
        test.AssertSessions(1);
    }

    /// <summary>A fully occupied ordinary inventory returns without reporting a direct-merge error.</summary>
    [Fact]
    public void DirectAlternatives_OrdinaryFullTargetDoesNotReportError()
    {
        var test = new TransferCase(1);
        test.Source(3);
        var errors = new TransferErrorObservation(test);
        test.Target[0].Itemstack = new ItemStack(test.Item, 64);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(0, test.Run());
        snapshot.AssertUnchangedExcept();
        snapshot.AssertConserved();
        Assert.Empty(test.ModifiedSlots);
        errors.AssertErrors(0);
        test.AssertSessions(0);
    }
    #endregion
    #endregion
}
