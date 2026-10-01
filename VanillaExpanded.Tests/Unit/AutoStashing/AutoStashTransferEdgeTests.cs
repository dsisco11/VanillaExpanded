using Moq;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects shared-transfer quantities, progress, restrictions, ranking, and source ordering.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AutoStashTransferEdgeTests
{
    #region Public API
    #region Capacity and source order
    /// <summary>Checks zero, exact-fit, and partial capacity with exact moved counts and source remainders.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(3)]
    public void Capacity_ReturnsActualQuantityAndPreservesRemainder(int space)
    {
        var test = new TransferCase(1);
        var source = test.Source(5);
        test.Target[0].Itemstack = new ItemStack(test.Item, 64 - space);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(space, test.Run());
        Assert.Equal(5 - space, source.StackSize);
        if (space < 5) InventorySnapshot.AssertStack(source, test.Item, 5 - space);
        else Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 64);
        if (space == 0) before.AssertUnchangedExcept();
        else before.AssertUnchangedExcept(source, test.Target[0]);
        before.AssertConserved();
        test.AssertSessions(space == 0 ? 0 : 1);
        Assert.Equal(space > 0, test.ModifiedSlots.Count > 0);
    }

    /// <summary>Uses limited capacity to distinguish source-slot order and backpack priority over hotbar.</summary>
    [Fact]
    public void CompetingSources_BackpackAndEarlierSlotsConsumeCapacityFirst()
    {
        var test = new TransferCase(1);
        var first = test.Source(2);
        var second = test.Source(4, 1);
        var hotbar = test.Source(3, hotbar: true);
        test.Target[0].Itemstack = new ItemStack(test.Item, 60);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(4, test.Run());
        Assert.True(first.Empty);
        InventorySnapshot.AssertStack(second, test.Item, 2);
        InventorySnapshot.AssertStack(hotbar, test.Item, 3);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 64);
        before.AssertUnchangedExcept(first, second, test.Target[0]);
        before.AssertConserved();
        test.AssertSessions(1);
        Assert.NotEmpty(test.ModifiedSlots);
    }

    /// <summary>Handles absent source inventories without conflating absence with empty engine inventories.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MissingSources_TransfersFromAvailableInventory(bool noBackpack, bool noHotbar)
    {
        var test = new TransferCase(1);
        var backpack = test.Source(2);
        var hotbar = test.Source(3, hotbar: true);
        if (noBackpack) test.Fixture.InventoryManagerMock.Setup(manager => manager.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns((IInventory)null!);
        if (noHotbar) test.Fixture.InventoryManagerMock.Setup(manager => manager.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns((IInventory)null!);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        int expected = (noBackpack ? 0 : 2) + (noHotbar ? 0 : 3);
        Assert.Equal(expected, test.Run());
        Assert.Equal(noBackpack ? 2 : 0, backpack.StackSize);
        Assert.Equal(noHotbar ? 3 : 0, hotbar.StackSize);
        Assert.Equal(expected, test.Target[0].StackSize);
        if (expected > 0) InventorySnapshot.AssertStack(test.Target[0], test.Item, expected);
        else Assert.True(test.Target[0].Empty);
        before.AssertUnchangedExcept(noBackpack ? test.Target[0] : backpack, noHotbar ? test.Target[0] : hotbar, test.Target[0]);
        before.AssertConserved();
        test.AssertSessions(expected == 0 ? 0 : 1);
        Assert.Equal(expected > 0, test.ModifiedSlots.Count > 0);
    }

    /// <summary>Repeating after source or capacity exhaustion performs no further movement or session acquisition.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void RepeatedExecution_AfterExhaustionIsUnchanged(int quantity)
    {
        var test = new TransferCase(1);
        var source = test.Source(quantity);
        test.Target[0].Itemstack = new ItemStack(test.Item, 60);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(Math.Min(quantity, 4), test.Run());
        Assert.Equal(Math.Max(0, quantity - 4), source.StackSize);
        if (quantity > 4) InventorySnapshot.AssertStack(source, test.Item, quantity - 4);
        else Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 60 + Math.Min(quantity, 4));
        before.AssertUnchangedExcept(source, test.Target[0]);
        before.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        var afterFirst = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        int notifications = test.ModifiedSlots.Count;
        Assert.Equal(0, test.Run());
        afterFirst.AssertUnchangedExcept();
        afterFirst.AssertConserved();
        Assert.Equal(notifications, test.ModifiedSlots.Count);
        test.AssertSessions(1);
    }
    #endregion

    #region Rejection and restrictions
    /// <summary>Continues after a selected slot fails containment, or terminates when every selected slot rejects.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedDestinations_ZeroMovesAdvanceToNextCandidate(bool rejectAll)
    {
        var test = new TransferCase(createTarget: _ => new SelectiveInventory(2));
        var target = (SelectiveInventory)test.Target;
        target.Accept = slot => !rejectAll && ReferenceEquals(slot, target[1]);
        var source = test.Source(3);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(rejectAll ? 0 : 3, test.Run());
        Assert.Equal(rejectAll ? new[] { target[0], target[1], target[0], target[1] } : new[] { target[0], target[1] }, source.Attempts.Select(attempt => attempt.Destination));
        Assert.Equal(rejectAll ? 3 : 0, source.StackSize);
        Assert.True(target[0].Empty);
        if (rejectAll) before.AssertUnchangedExcept();
        else
        {
            InventorySnapshot.AssertStack(target[1], test.Item, 3);
            before.AssertUnchangedExcept(source, target[1]);
        }
        before.AssertConserved();
        Assert.Equal(!rejectAll, test.ModifiedSlots.Count > 0);
        test.AssertSessions(1);
    }

    /// <summary>Independently checks source lock, target lock, storage flags, and inventory containment.</summary>
    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("storage")]
    [InlineData("containment")]
    public void Restrictions_PreventMovementWithoutSideEffects(string restriction)
    {
        var test = new TransferCase(createTarget: _ => new SelectiveInventory(1));
        var source = test.Source(3);
        switch (restriction)
        {
            case "source": test.Fixture.BackpackInventory.TakeLocked = true; break;
            case "target": test.Target.PutLocked = true; break;
            case "storage": test.Target[0].StorageType = 0; break;
            case "containment": ((SelectiveInventory)test.Target).Accept = _ => false; break;
        }
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(0, test.Run());
        before.AssertUnchangedExcept();
        before.AssertConserved();
        Assert.Empty(test.ModifiedSlots);
        test.AssertSessions(restriction is "target" or "storage" ? 0 : 1);
        Assert.Equal(restriction is "target" or "storage" ? 0 : 2, source.Attempts.Count);
    }

    /// <summary>Preserves incompatible stack attributes and uses an empty alternative only when available.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleAttributes_DoNotOverwriteExistingStack(bool emptyAlternative)
    {
        var test = new TransferCase(emptyAlternative ? 2 : 1);
        var source = test.Source(3);
        source.Itemstack!.Attributes.SetString("grade", "source");
        test.Target[0].Itemstack = new ItemStack(test.Item, 2);
        test.Target[0].Itemstack!.Attributes.SetString("grade", "target");
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(emptyAlternative ? 3 : 0, test.Run());
        if (emptyAlternative)
        {
            Assert.True(source.Empty);
            InventorySnapshot.AssertStack(test.Target[1], test.Item, 3);
            Assert.Equal("source", test.Target[1].Itemstack!.Attributes.GetString("grade"));
            before.AssertUnchangedExcept(source, test.Target[1]);
        }
        else before.AssertUnchangedExcept();
        before.AssertConserved();
        test.AssertSessions(emptyAlternative ? 1 : 0);
        Assert.Equal(emptyAlternative, test.ModifiedSlots.Count > 0);
    }
    #endregion

    #region Suitability
    /// <summary>Honors custom engine suitability rather than the first target in enumeration.</summary>
    [Fact]
    public void Suitability_HighestRankedDestinationReceivesItems()
    {
        var test = new TransferCase(createTarget: fixture => new ObservedInventory(2, fixture.Api));
        var source = test.Source(3);
        test.Target.OnGetSuitability = (_, slot, _) => ReferenceEquals(slot, test.Target[1]) ? 9 : 1;
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(3, test.Run());
        Assert.Same(test.Target[1], Assert.Single(source.Attempts).Destination);
        InventorySnapshot.AssertStack(test.Target[1], test.Item, 3);
        Assert.True(source.Empty);
        before.AssertUnchangedExcept(source, test.Target[1]);
        before.AssertConserved();
        test.AssertSessions(1);
        Assert.NotEmpty(test.ModifiedSlots);
    }
    #endregion
    #endregion
}
