using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects complete bloomery mutation, live capacities, source ordering and lifecycle effects.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class BloomeryExecutionTests
{
    #region Public API
    #region Capacity and source passes
    /// <summary>Checks mixed inputs, rounded allowance and exact routing across backpack/hotbar distributions.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MixedInputs_DepositFiveOreAndThreeFuel(int distribution)
    {
        var test = new BloomeryCase();
        var ore = test.Source(test.Ore, 5, hotbar: distribution == 1);
        var fuel = test.Source(test.Fuel, 10, 1, hotbar: distribution != 0);
        test.Source(test.Invalid, 7, 9);
        var before = test.Snapshot();

        Assert.True(test.Run());
        test.AssertSlot(ore, test.Ore, 0);
        test.AssertSlot(fuel, test.Fuel, 7);
        test.AssertSlot(test.Target.OreSlot, test.Ore, 5);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 3);
        Assert.Same(test.Target.OreSlot, Assert.Single(ore.Attempts).Destination);
        Assert.Same(test.Target.FuelSlot, Assert.Single(fuel.Attempts).Destination);
        test.Verify(before, true, ore, fuel, test.Target.OreSlot, test.Target.FuelSlot);
    }

    /// <summary>Subtracts existing fuel from the rounded five-ore allowance.</summary>
    [Fact]
    public void ExistingFuel_ReducesRoundedRemainingAllowance()
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.OreSlot, test.Ore, 5);
        test.Seed(test.Target.FuelSlot, test.Fuel, 1);
        var fuel = test.Source(test.Fuel, 5);
        var before = test.Snapshot();
        Assert.True(test.Run());
        test.AssertSlot(fuel, test.Fuel, 3);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 3);
        Assert.Equal(2, Assert.Single(fuel.Attempts).Quantity);
        test.Verify(before, true, fuel, test.Target.FuelSlot);
    }

    /// <summary>Uses the ore actually moved rather than the larger requested quantity to limit fuel.</summary>
    [Fact]
    public void PartialOreMove_FuelAllowanceUsesActualDeposit()
    {
        var test = new BloomeryCase();
        var ore = test.Source(test.Ore, 5);
        ore.QuantityLimit = 2;
        var fuel = test.Source(test.Fuel, 5, 1);
        var before = test.Snapshot();
        Assert.True(test.Run());
        test.AssertSlot(ore, test.Ore, 3);
        test.AssertSlot(fuel, test.Fuel, 4);
        test.AssertSlot(test.Target.OreSlot, test.Ore, 2);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 1);
        Assert.Equal(5, Assert.Single(ore.Attempts).Quantity);
        Assert.Equal(1, Assert.Single(fuel.Attempts).Quantity);
        test.Verify(before, true, ore, fuel, test.Target.OreSlot, test.Target.FuelSlot);
    }

    /// <summary>Earlier source stacks consume existing ore/fuel capacity without overflow routing.</summary>
    [Fact]
    public void ExistingContents_CompetingSourcesConsumeOnlyRemainingCapacity()
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.OreSlot, test.Ore, 10);
        test.Seed(test.Target.FuelSlot, test.Fuel, 5);
        var first = test.Source(test.Ore, 4);
        var second = test.Source(test.Ore, 4, 1);
        var fuel = test.Source(test.Fuel, 8, 2);
        var laterFuel = test.Source(test.Fuel, 3, hotbar: true);
        var before = test.Snapshot();
        Assert.True(test.Run());
        test.AssertSlot(first, test.Ore, 2);
        test.AssertSlot(second, test.Ore, 4);
        test.AssertSlot(fuel, test.Fuel, 7);
        test.AssertSlot(test.Target.OreSlot, test.Ore, 12);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 6);
        Assert.Empty(second.Attempts);
        Assert.Empty(laterFuel.Attempts);
        test.Verify(before, true, first, fuel, test.Target.OreSlot, test.Target.FuelSlot);
    }

    /// <summary>Characterizes per-inventory ore/fuel passes with outcomes different from processing all ore first.</summary>
    [Fact]
    public void SourcePassOrder_BackpackOreFuelThenHotbarOreFuel()
    {
        var test = new BloomeryCase();
        var backpackFuel = test.Source(test.Fuel, 6);
        var backpackOre = test.Source(test.Ore, 2, 1);
        var hotbarOre = test.Source(test.Ore, 4, hotbar: true);
        var hotbarFuel = test.Source(test.Fuel, 4, 1, hotbar: true);
        var attempts = new List<string>();
        backpackOre.BeforeMove = _ => attempts.Add("backpack ore");
        backpackFuel.BeforeMove = _ => attempts.Add("backpack fuel");
        hotbarOre.BeforeMove = _ => attempts.Add("hotbar ore");
        hotbarFuel.BeforeMove = _ => attempts.Add("hotbar fuel");
        var before = test.Snapshot();
        Assert.True(test.Run());
        Assert.Equal(new[] { "backpack ore", "backpack fuel", "hotbar ore", "hotbar fuel" }, attempts);
        test.AssertSlot(backpackOre, test.Ore, 0);
        test.AssertSlot(backpackFuel, test.Fuel, 5);
        test.AssertSlot(hotbarOre, test.Ore, 0);
        test.AssertSlot(hotbarFuel, test.Fuel, 2);
        test.AssertSlot(test.Target.OreSlot, test.Ore, 6);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 3);
        // Global ore-first processing would take three backpack fuel, leaving three rather than five.
        test.Verify(before, true, backpackOre, backpackFuel, hotbarOre, hotbarFuel,
            test.Target.OreSlot, test.Target.FuelSlot);
    }
    #endregion
    #region Rejection and eligibility boundaries
    /// <summary>Rejects burning or occupied-output targets despite valid sources and available input capacity.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableTarget_PreservesEverySlot(bool burning)
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.OreSlot, test.Ore, 2);
        test.Seed(test.Target.FuelSlot, test.Fuel, 1);
        test.Source(test.Ore, 3);
        test.Source(test.Fuel, 3, 1);
        if (burning) test.Target.SetBurning(true);
        else test.Seed(test.Target.OutSlot, test.Invalid, 1);
        var before = test.Snapshot();
        Assert.False(test.Run());
        test.Verify(before, false);
    }

    /// <summary>Full required slots reject without using another input/output slot or notifying synchronization.</summary>
    [Fact]
    public void FullInputs_DoNotRouteToOutput()
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.OreSlot, test.Ore, 12);
        test.Seed(test.Target.FuelSlot, test.Fuel, 6);
        var ore = test.Source(test.Ore, 4);
        var fuel = test.Source(test.Fuel, 4, 1);
        var before = test.Snapshot();
        Assert.False(test.Run());
        Assert.Empty(ore.Attempts);
        Assert.Empty(fuel.Attempts);
        test.Verify(before, false);
    }

    /// <summary>A full mandatory destination does not fall back into the other empty input slot.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullRequiredSlot_DoesNotUseOtherEmptyInput(bool ore)
    {
        var test = new BloomeryCase();
        var item = ore ? test.Ore : test.Fuel;
        test.Seed(ore ? test.Target.OreSlot : test.Target.FuelSlot, item, ore ? 12 : 6);
        test.Source(item, 4);
        var before = test.Snapshot();
        Assert.False(test.Run());
        test.Verify(before, false);
    }

    /// <summary>Attempted but rejected engine moves do not report success or mark the bloomery dirty.</summary>
    [Fact]
    public void RejectedActualMoves_DoNotSynchronize()
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.OreSlot, test.Ore, 2);
        var ore = test.Source(test.Ore, 5);
        var fuel = test.Source(test.Fuel, 5, 1);
        ore.Reject = fuel.Reject = true;
        var before = test.Snapshot();
        Assert.False(test.Run());
        Assert.Same(test.Target.OreSlot, Assert.Single(ore.Attempts).Destination);
        Assert.Same(test.Target.FuelSlot, Assert.Single(fuel.Attempts).Destination);
        test.Verify(before, false);
    }

    #endregion
    #region Types and client assessment
    /// <summary>Rejects different ore/fuel identities while still transferring valid matching sources.</summary>
    [Fact]
    public void MixedInvalidAndDifferentTypes_OnlyMatchingInputsMove()
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.OreSlot, test.Ore, 2);
        test.Seed(test.Target.FuelSlot, test.Fuel, 1);
        var differentOre = MockItem.CreateBloomeryOre(4, 2, test.Fixture.Api);
        differentOre.Code = new AssetLocation("game:different-ore");
        var differentFuel = MockItem.CreateBloomeryFuel(5, test.Fixture.Api);
        differentFuel.Code = new AssetLocation("game:different-fuel");
        test.Source(differentOre, 3);
        test.Source(differentFuel, 3, 1);
        test.Source(test.Invalid, 3, 2);
        var invalidCombustible = MockItem.CreateLowTempCombustible(6, test.Fixture.Api);
        invalidCombustible.Code = new AssetLocation("game:invalid-combustible");
        test.Source(invalidCombustible, 3, 3);
        var ore = test.Source(test.Ore, 2, 4);
        var fuel = test.Source(test.Fuel, 3, 5);
        var before = test.Snapshot();
        Assert.True(test.Run());
        test.AssertSlot(ore, test.Ore, 0);
        test.AssertSlot(fuel, test.Fuel, 2);
        test.AssertSlot(test.Target.OreSlot, test.Ore, 4);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 2);
        test.Verify(before, true, ore, fuel, test.Target.OreSlot, test.Target.FuelSlot);
    }

    /// <summary>Separates the empty-target active-item client gate from actual server execution.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyTarget_ActiveItemGateAffectsAssessmentOnly(bool validActiveItem)
    {
        using var scope = new AutoStashTestScope();
        var test = new BloomeryCase();
        var ore = test.Source(test.Ore, 5);
        var active = test.Source(validActiveItem ? test.Ore : test.Invalid, 1, hotbar: true);
        // The active item comes from a real hotbar slot in both paired cases.
        test.Fixture.InventoryManagerMock.Setup(manager => manager.ActiveHotbarSlot)
            .Returns(active);
        var before = test.Snapshot();
        var ids = BlockBehaviorAutoStashable.GetStashableItems(test.Fixture.Player, test.Target);
        if (validActiveItem) Assert.Equal(new[] { test.Ore.Id }, ids);
        else Assert.Empty(ids);
        before.AssertUnchangedExcept();
        Assert.Empty(ore.Attempts);
        test.TargetMock.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        Assert.True(test.Run());
        test.AssertSlot(ore, test.Ore, 0);
        test.AssertSlot(active, validActiveItem ? test.Ore : test.Invalid, validActiveItem ? 0 : 1);
        test.AssertSlot(test.Target.OreSlot, test.Ore, validActiveItem ? 6 : 5);
        if (validActiveItem) test.Verify(before, true, ore, active, test.Target.OreSlot);
        else test.Verify(before, true, ore, test.Target.OreSlot);
    }
    #endregion
    #endregion
}
