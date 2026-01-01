using VanillaExpanded.AlloyCalculator;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>
/// Tests for the slot allocation algorithm in AlloyCalculatorLogic.
/// </summary>
[Trait("Category", "Unit")]
public class SlotAllocationTests
{
    #region Edge Cases - Empty/Zero Inputs

    [Fact]
    public void AllocateSlotsProportionally_EmptyIngredients_ReturnsEmptyArray()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([], 4);

        Assert.Empty(result);
    }

    [Fact]
    public void AllocateSlotsProportionally_ZeroSlots_ReturnsZeroAllocations()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([10, 20], 0);

        Assert.Equal(2, result.Length);
        Assert.All(result, allocation => Assert.Equal(0, allocation));
    }

    [Fact]
    public void AllocateSlotsProportionally_NegativeSlots_ReturnsZeroAllocations()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([10, 20], -5);

        Assert.Equal(2, result.Length);
        Assert.All(result, allocation => Assert.Equal(0, allocation));
    }

    #endregion

    #region Single Ingredient

    [Fact]
    public void AllocateSlotsProportionally_SingleIngredient_GetsAllSlots()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([50], 4);

        Assert.Single(result);
        Assert.Equal(4, result[0]);
    }

    [Fact]
    public void AllocateSlotsProportionally_SingleIngredientOneSlot_GetsOneSlot()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([100], 1);

        Assert.Single(result);
        Assert.Equal(1, result[0]);
    }

    #endregion

    #region More Ingredients Than Slots

    [Fact]
    public void AllocateSlotsProportionally_MoreIngredientsThanSlots_EachGetsOneUntilSlotsExhausted()
    {
        // 5 ingredients, only 3 slots
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([10, 20, 30, 40, 50], 3);

        Assert.Equal(5, result.Length);
        Assert.Equal(1, result[0]);
        Assert.Equal(1, result[1]);
        Assert.Equal(1, result[2]);
        Assert.Equal(0, result[3]); // No slot for this ingredient
        Assert.Equal(0, result[4]); // No slot for this ingredient
    }

    [Fact]
    public void AllocateSlotsProportionally_EqualIngredientsAndSlots_EachGetsOne()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([10, 20, 30], 3);

        Assert.Equal(3, result.Length);
        // Each gets at least 1, total = 3
        Assert.Equal(3, result.Sum());
    }

    #endregion

    #region Proportional Distribution

    [Fact]
    public void AllocateSlotsProportionally_UnequalAmounts_LargerGetsMoreSlots()
    {
        // 2 ingredients: 25 and 75 units, 4 slots
        // Each gets 1 minimum, 2 remaining distributed proportionally
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([25, 75], 4);

        Assert.Equal(2, result.Length);
        Assert.Equal(4, result.Sum()); // Total should equal available slots

        // Larger amount should get more slots
        Assert.True(result[1] >= result[0]);
    }

    [Fact]
    public void AllocateSlotsProportionally_EqualAmounts_EqualDistribution()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([50, 50], 4);

        Assert.Equal(2, result.Length);
        Assert.Equal(4, result.Sum());
        Assert.Equal(result[0], result[1]); // Should be equal
    }

    [Fact]
    public void AllocateSlotsProportionally_ThreeIngredients_ProportionalDistribution()
    {
        // Bronze: ~88% copper, ~12% tin typically
        // With 4 slots and 2 ingredients getting minimum 1 each, 2 extra go proportionally
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([88, 12], 4);

        Assert.Equal(2, result.Length);
        Assert.Equal(4, result.Sum());
        Assert.True(result[0] > result[1]); // Copper should get more
    }

    #endregion

    #region Total Slots Constraint

    [Fact]
    public void AllocateSlotsProportionally_AlwaysSumsToTotalSlots()
    {
        var testCases = new[]
        {
            (amounts: new[] { 10, 20, 30 }, slots: 6),
            (amounts: new[] { 50, 50 }, slots: 4),
            (amounts: new[] { 10, 10, 10, 10 }, slots: 8),
            (amounts: new[] { 1, 99 }, slots: 4),
            (amounts: new[] { 33, 33, 34 }, slots: 4),
        };

        foreach (var (amounts, slots) in testCases)
        {
            var result = AlloyCalculatorLogic.AllocateSlotsProportionally(amounts, slots);

            Assert.Equal(amounts.Length, result.Length);
            Assert.Equal(slots, result.Sum());
        }
    }

    [Fact]
    public void AllocateSlotsProportionally_MinimumOneSlotPerIngredient_WhenEnoughSlots()
    {
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([1, 1, 1, 97], 6);

        Assert.Equal(4, result.Length);
        // Each ingredient should have at least 1 slot
        Assert.All(result, allocation => Assert.True(allocation >= 1));
    }

    #endregion

    #region Rounding Edge Cases

    [Fact]
    public void AllocateSlotsProportionally_OddSlotsEvenIngredients_HandlesRounding()
    {
        // 2 equal ingredients, 5 slots (odd)
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([50, 50], 5);

        Assert.Equal(2, result.Length);
        Assert.Equal(5, result.Sum());
        // Difference should be at most 1 due to rounding
        Assert.True(Math.Abs(result[0] - result[1]) <= 1);
    }

    [Fact]
    public void AllocateSlotsProportionally_ZeroAmounts_StillGetsMinimumSlot()
    {
        // Edge case: ingredient with 0 target amount
        var result = AlloyCalculatorLogic.AllocateSlotsProportionally([0, 100], 4);

        Assert.Equal(2, result.Length);
        Assert.Equal(4, result.Sum());
        // First ingredient still gets minimum 1 slot
        Assert.True(result[0] >= 1);
    }

    #endregion
}
