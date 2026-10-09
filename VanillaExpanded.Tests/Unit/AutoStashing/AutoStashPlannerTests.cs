using Moq;
using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.AutoStashing.Transfers;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects the pure planning boundary and live cursor behavior independently of operation lifecycle.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class AutoStashPlannerTests
{
    #region Public API
    #region Policy snapshots
    /// <summary>Captures matching codes once and rejects types introduced into the target afterward.</summary>
    [Fact]
    public void MatchingPolicy_DoesNotExpandWhenContentsChange()
    {
        var test = new TransferCase();
        test.Target[0].Itemstack = new ItemStack(test.Item, 1);
        var policy = new MatchingContentsPolicy(new[] { test.Target[0].Itemstack });
        var other = new MockItem(9, api: test.Fixture.Api) { Code = new AssetLocation("game:later-type") };
        test.Target[1].Itemstack = new ItemStack(other, 1);
        var pass = new AutoStashSourcePass(test.Fixture.BackpackInventory);

        Assert.True(policy.IsEligible(new ItemStack(test.Item), pass));
        Assert.False(policy.IsEligible(new ItemStack(other), pass));
    }

    /// <summary>Captures the first initial crate type even when later slots contain a different type.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CratePolicy_UsesOnlyFirstInitialType(bool empty)
    {
        var test = new TransferCase();
        var other = new MockItem(9, api: test.Fixture.Api) { Code = new AssetLocation("game:second-type") };
        if (!empty)
        {
            test.Target[0].Itemstack = new ItemStack(test.Item, 1);
            test.Target[1].Itemstack = new ItemStack(other, 1);
        }
        var policy = new CratePolicy(test.Target);
        test.Target[0].Itemstack = new ItemStack(other, 1);
        var pass = new AutoStashSourcePass(test.Fixture.BackpackInventory);

        Assert.Equal(!empty, policy.IsEligible(new ItemStack(test.Item), pass));
        Assert.False(policy.IsEligible(new ItemStack(other), pass));
    }
    #endregion

    #region Selection and advancement
    /// <summary>Probing and selecting an instruction never mutate inventories or acquire/persist an execution session.</summary>
    [Fact]
    public void Planning_LeavesInventoriesAndLifecycleUntouched()
    {
        var test = new TransferCase();
        var source = test.Source(3);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        var policy = new MatchingContentsPolicy(_ => true);
        Assert.True(AutoStashPlanner.HasWork(test.Target, policy, test.Fixture.BackpackInventory, null));
        using var planner = new AutoStashPlanner(test.Fixture.World, test.Target, policy, test.Fixture.BackpackInventory, null);

        InventoryTransfer transfer = Assert.IsType<InventoryTransfer>(planner.GetNextTransfer());

        Assert.Same(source, transfer.Source);
        Assert.Equal(3, transfer.RequestedQuantity);
        snapshot.AssertUnchangedExcept();
        Assert.Empty(source.Attempts);
        Assert.Empty(test.ModifiedSlots);
        test.AssertSessions(0);
    }

    /// <summary>Requires an actual outcome before another selection rather than repeatedly issuing the same move.</summary>
    [Fact]
    public void Planner_RequiresOutcomeBeforeNextSelection()
    {
        var test = new TransferCase();
        test.Source(3);
        using var planner = new AutoStashPlanner(test.Fixture.World, test.Target, new MatchingContentsPolicy(_ => true),
            test.Fixture.BackpackInventory, null);
        var transfer = Assert.IsType<InventoryTransfer>(planner.GetNextTransfer());
        Assert.Throws<InvalidOperationException>(() => planner.GetNextTransfer());
        planner.Advance(transfer, InventoryTransferExecutor.Execute(test.Fixture.World, transfer));
        Assert.Throws<InvalidOperationException>(() => planner.Advance(transfer, new InventoryTransferResult(3, 0, null)));
        Assert.Null(planner.GetNextTransfer());
    }

    /// <summary>Reads live capacity on the next source and visits backpack before hotbar.</summary>
    [Fact]
    public void Planner_AdvancesUsingActualMovementAndLiveCapacity()
    {
        var test = new TransferCase(1);
        var backpack = test.Source(3);
        var hotbar = test.Source(5, hotbar: true);
        test.Target[0].MaxSlotStackSize = 5;
        using var planner = new AutoStashPlanner(test.Fixture.World, test.Target, new MatchingContentsPolicy(_ => true),
            test.Fixture.BackpackInventory, test.Fixture.HotbarInventory);
        var first = Assert.IsType<InventoryTransfer>(planner.GetNextTransfer());
        Assert.Same(backpack, first.Source);
        planner.Advance(first, InventoryTransferExecutor.Execute(test.Fixture.World, first));
        var second = Assert.IsType<InventoryTransfer>(planner.GetNextTransfer());
        Assert.Same(hotbar, second.Source);
        var result = InventoryTransferExecutor.Execute(test.Fixture.World, second);
        Assert.Equal(2, result.MovedQuantity);
        planner.Advance(second, result);
        Assert.Null(planner.GetNextTransfer());
        InventorySnapshot.AssertStack(hotbar, test.Item, 3);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 5);
        Assert.False(planner.DirectMergeFailed);
    }

    /// <summary>Skips absent source inventories without acquiring lifecycle or producing an instruction.</summary>
    [Fact]
    public void Planner_MissingSourcesTerminate()
    {
        var test = new TransferCase();
        var policy = new MatchingContentsPolicy(_ => true);
        using var planner = new AutoStashPlanner(test.Fixture.World, test.Target, policy, null, null);
        Assert.False(AutoStashPlanner.HasWork(test.Target, policy, null, null));
        Assert.Null(planner.GetNextTransfer());
        test.AssertSessions(0);
    }
    #endregion

    #region Required routing
    /// <summary>Attempts ore once per source and recalculates fuel from actual ore deposited through the engine.</summary>
    [Fact]
    public void BloomeryPlanner_UsesActualOreAndRequiredSlots()
    {
        var test = new BloomeryCase();
        var ore = test.Source(test.Ore, 5);
        ore.QuantityLimit = 2;
        var fuel = test.Source(test.Fuel, 5, 1);
        using var planner = new AutoStashPlanner(test.Fixture.World, test.Target.TestInventory,
            new BloomeryPolicy(test.Target, test.Target.TestInventory), test.Fixture.BackpackInventory, null);
        var first = Assert.IsType<InventoryTransfer>(planner.GetNextTransfer());
        Assert.Same(ore, first.Source);
        Assert.Same(test.Target.OreSlot, first.Destination);
        Assert.Equal(InventoryTransferInvocation.EngineDefaults, first.Invocation);
        planner.Advance(first, InventoryTransferExecutor.Execute(test.Fixture.World, first));
        var second = Assert.IsType<InventoryTransfer>(planner.GetNextTransfer());
        Assert.Same(fuel, second.Source);
        Assert.Same(test.Target.FuelSlot, second.Destination);
        Assert.Equal(1, second.RequestedQuantity);
        planner.Advance(second, InventoryTransferExecutor.Execute(test.Fixture.World, second));
        Assert.Null(planner.GetNextTransfer());
        Assert.Single(ore.Attempts);
        test.AssertSlot(ore, test.Ore, 3);
        test.AssertSlot(fuel, test.Fuel, 4);
        test.TargetMock.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
    }
    #endregion
    #endregion
}
