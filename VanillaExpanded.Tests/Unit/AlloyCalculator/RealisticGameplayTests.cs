using VanillaExpanded.AlloyCalculator;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>
/// Tests using realistic Vintage Story alloy recipes and gameplay scenarios.
/// These tests validate that the calculator works correctly for actual in-game use cases.
/// </summary>
[Trait("Category", "Unit")]
public class RealisticGameplayTests
{
    #region Constants - Real Alloy Ratios from Vintage Story

    // Bronze: 88-92% Copper, 8-12% Tin
    private const int BronzeCopperMin = 88;
    private const int BronzeCopperMax = 92;
    private const int BronzeTinMin = 8;
    private const int BronzeTinMax = 12;

    // Brass: 55-65% Copper, 35-45% Zinc
    private const int BrassCopperMin = 55;
    private const int BrassCopperMax = 65;
    private const int BrassZincMin = 35;
    private const int BrassZincMax = 45;

    // Bismuth Bronze: 50-65% Copper, 20-30% Zinc, 10-20% Bismuth
    private const int BismuthBronzeCopperMin = 50;
    private const int BismuthBronzeCopperMax = 65;
    private const int BismuthBronzeZincMin = 20;
    private const int BismuthBronzeZincMax = 30;
    private const int BismuthBronzeBismuthMin = 10;
    private const int BismuthBronzeBismuthMax = 20;

    // Crucible has 4 cooking slots
    private const int CrucibleSlots = 4;

    // Common target amounts
    private const int OneIngot = 100;
    private const int FiveIngots = 500;
    private const int MinimumUseful = 25;

    #endregion

    #region Bronze Alloy Tests (2 ingredients)

    [Fact]
    public void Bronze_StandardRatio_CorrectNuggetCalculation()
    {
        // Arrange - Standard bronze: 90% copper, 10% tin for 100 units (1 ingot)
        const int copperPercent = 90;
        const int tinPercent = 10;

        // Act
        var copperNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, copperPercent);
        var tinNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, tinPercent);

        // Assert
        // Copper: 100 * 0.90 = 90 units / 5 = 18 nuggets
        Assert.Equal(18, copperNuggets);
        // Tin: 100 * 0.10 = 10 units / 5 = 2 nuggets
        Assert.Equal(2, tinNuggets);
    }

    [Fact]
    public void Bronze_FiveIngots_CorrectNuggetCalculation()
    {
        // Arrange - Making 5 bronze ingots (common batch size)
        const int copperPercent = 90;
        const int tinPercent = 10;

        // Act
        var copperNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(FiveIngots, copperPercent);
        var tinNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(FiveIngots, tinPercent);

        // Assert
        // Copper: 500 * 0.90 = 450 units / 5 = 90 nuggets
        Assert.Equal(90, copperNuggets);
        // Tin: 500 * 0.10 = 50 units / 5 = 10 nuggets
        Assert.Equal(10, tinNuggets);
    }

    [Fact]
    public void Bronze_SlotAllocation_CopperGetsMoreSlots()
    {
        // Arrange - Bronze with 90% copper (18 nuggets), 10% tin (2 nuggets)
        var ingredientAmounts = new[] { 18, 2 }; // Copper, Tin

        // Act
        var slots = AlloyCalculatorLogic.AllocateSlotsProportionally(ingredientAmounts, CrucibleSlots);

        // Assert - Copper should get 3 slots, tin should get 1
        Assert.Equal(4, slots.Sum());
        Assert.True(slots[0] >= 3, $"Copper should get at least 3 slots, got {slots[0]}");
        Assert.True(slots[1] >= 1, "Tin should get at least 1 slot");
    }

    [Fact]
    public void Bronze_InitialSliders_SumTo100Percent()
    {
        // Arrange - Bronze slider constraints
        var sliderValues = new Dictionary<int, int>
        {
            { 0, AlloyCalculatorLogic.CalculateMidpointPercentage(BronzeCopperMin / 100.0, BronzeCopperMax / 100.0) },
            { 1, AlloyCalculatorLogic.CalculateMidpointPercentage(BronzeTinMin / 100.0, BronzeTinMax / 100.0) }
        };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (BronzeCopperMin, BronzeCopperMax) },
            { 1, (BronzeTinMin, BronzeTinMax) }
        };

        // Act
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, -1);

        // Assert
        var total = normalized.Values.Sum();
        Assert.True(Math.Abs(total - 100) <= 1, $"Sliders should sum to ~100%, got {total}%");
    }

    [Fact]
    public void Bronze_CopperAtMax_TinAdjustsToComplement()
    {
        // Arrange - Player drags copper slider to maximum (92%)
        var sliderValues = new Dictionary<int, int>
        {
            { 0, BronzeCopperMax }, // 92% copper
            { 1, 10 }              // Tin starts at some value
        };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (BronzeCopperMin, BronzeCopperMax) },
            { 1, (BronzeTinMin, BronzeTinMax) }
        };

        // Act - Normalize after copper was changed
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Assert - Tin should be adjusted to make total ~100%
        Assert.Equal(92, normalized[0]); // Copper unchanged (was the changed slider)
        Assert.Equal(8, normalized[1]);  // Tin should be 8% (100 - 92)
    }

    [Fact]
    public void Bronze_TinAtMax_CopperAdjustsToComplement()
    {
        // Arrange - Player drags tin slider to maximum (12%)
        var sliderValues = new Dictionary<int, int>
        {
            { 0, 90 },             // Copper starts at some value
            { 1, BronzeTinMax }    // 12% tin
        };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (BronzeCopperMin, BronzeCopperMax) },
            { 1, (BronzeTinMin, BronzeTinMax) }
        };

        // Act - Normalize after tin was changed
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 1);

        // Assert
        Assert.Equal(88, normalized[0]); // Copper should be 88% (100 - 12)
        Assert.Equal(12, normalized[1]); // Tin unchanged
    }

    #endregion

    #region Brass Alloy Tests (2 ingredients, different ratios)

    [Fact]
    public void Brass_StandardRatio_CorrectNuggetCalculation()
    {
        // Arrange - Brass: 60% copper, 40% zinc for 100 units
        const int copperPercent = 60;
        const int zincPercent = 40;

        // Act
        var copperNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, copperPercent);
        var zincNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, zincPercent);

        // Assert
        // Copper: 60 units / 5 = 12 nuggets
        Assert.Equal(12, copperNuggets);
        // Zinc: 40 units / 5 = 8 nuggets
        Assert.Equal(8, zincNuggets);
    }

    [Fact]
    public void Brass_SlotAllocation_MoreEvenThanBronze()
    {
        // Arrange - Brass with 60% copper (12 nuggets), 40% zinc (8 nuggets)
        var ingredientAmounts = new[] { 12, 8 };

        // Act
        var slots = AlloyCalculatorLogic.AllocateSlotsProportionally(ingredientAmounts, CrucibleSlots);

        // Assert - Distribution should be more even than bronze (roughly 2-2 or 3-1)
        Assert.Equal(4, slots.Sum());
        Assert.True(slots[0] >= 2, "Copper should get at least 2 slots");
        Assert.True(slots[1] >= 1, "Zinc should get at least 1 slot");
    }

    #endregion

    #region Bismuth Bronze Tests (3 ingredients)

    [Fact]
    public void BismuthBronze_StandardRatio_CorrectNuggetCalculation()
    {
        // Arrange - Bismuth Bronze: 55% copper, 25% zinc, 20% bismuth for 100 units
        const int copperPercent = 55;
        const int zincPercent = 25;
        const int bismuthPercent = 20;

        // Act
        var copperNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, copperPercent);
        var zincNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, zincPercent);
        var bismuthNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(OneIngot, bismuthPercent);

        // Assert
        // Copper: 55 units / 5 = 11 nuggets
        Assert.Equal(11, copperNuggets);
        // Zinc: 25 units / 5 = 5 nuggets
        Assert.Equal(5, zincNuggets);
        // Bismuth: 20 units / 5 = 4 nuggets
        Assert.Equal(4, bismuthNuggets);
    }

    [Fact]
    public void BismuthBronze_ThreeIngredients_FourSlots_EachGetsAtLeastOne()
    {
        // Arrange - 3 ingredients in 4 slots
        var ingredientAmounts = new[] { 11, 5, 4 }; // Copper, Zinc, Bismuth

        // Act
        var slots = AlloyCalculatorLogic.AllocateSlotsProportionally(ingredientAmounts, CrucibleSlots);

        // Assert
        Assert.Equal(4, slots.Sum());
        Assert.True(slots[0] >= 1, "Copper should get at least 1 slot");
        Assert.True(slots[1] >= 1, "Zinc should get at least 1 slot");
        Assert.True(slots[2] >= 1, "Bismuth should get at least 1 slot");
        // Copper (largest) should get the extra slot
        Assert.True(slots[0] >= 2, $"Copper (largest) should get 2 slots, got {slots[0]}");
    }

    [Fact]
    public void BismuthBronze_InitialSliders_SumTo100Percent()
    {
        // Arrange - Bismuth Bronze slider constraints
        var sliderValues = new Dictionary<int, int>
        {
            { 0, AlloyCalculatorLogic.CalculateMidpointPercentage(BismuthBronzeCopperMin / 100.0, BismuthBronzeCopperMax / 100.0) },
            { 1, AlloyCalculatorLogic.CalculateMidpointPercentage(BismuthBronzeZincMin / 100.0, BismuthBronzeZincMax / 100.0) },
            { 2, AlloyCalculatorLogic.CalculateMidpointPercentage(BismuthBronzeBismuthMin / 100.0, BismuthBronzeBismuthMax / 100.0) }
        };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (BismuthBronzeCopperMin, BismuthBronzeCopperMax) },
            { 1, (BismuthBronzeZincMin, BismuthBronzeZincMax) },
            { 2, (BismuthBronzeBismuthMin, BismuthBronzeBismuthMax) }
        };

        // Act
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, -1);

        // Assert
        var total = normalized.Values.Sum();
        Assert.True(Math.Abs(total - 100) <= 2, $"Sliders should sum to ~100%, got {total}%");
    }

    [Fact]
    public void BismuthBronze_AdjustOneSlider_OthersNormalize()
    {
        // Arrange - Player adjusts copper to 60%
        var sliderValues = new Dictionary<int, int>
        {
            { 0, 60 }, // Copper adjusted to 60%
            { 1, 25 }, // Zinc
            { 2, 15 }  // Bismuth - total would be 100%
        };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (BismuthBronzeCopperMin, BismuthBronzeCopperMax) },
            { 1, (BismuthBronzeZincMin, BismuthBronzeZincMax) },
            { 2, (BismuthBronzeBismuthMin, BismuthBronzeBismuthMax) }
        };

        // Act
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Assert - Already at 100%, should be unchanged
        Assert.Equal(60, normalized[0]);
        var total = normalized.Values.Sum();
        Assert.Equal(100, total);
    }

    #endregion

    #region Common Batch Sizes

    [Theory]
    [InlineData(25, 90, 5)]   // Minimum useful: 25 * 0.9 = 22.5 / 5 = 4.5 → 5 nuggets
    [InlineData(100, 90, 18)] // 1 ingot: 100 * 0.9 = 90 / 5 = 18 nuggets
    [InlineData(200, 90, 36)] // 2 ingots: 200 * 0.9 = 180 / 5 = 36 nuggets
    [InlineData(500, 90, 90)] // 5 ingots: 500 * 0.9 = 450 / 5 = 90 nuggets
    public void CommonBatchSizes_CorrectCopperNuggets(int targetUnits, int copperPercent, int expectedNuggets)
    {
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(targetUnits, copperPercent);

        Assert.Equal(expectedNuggets, result);
    }

    [Fact]
    public void MinimumUsefulAmount_AllIngredientsGetAtLeastOneNugget()
    {
        // Arrange - Minimum useful amount with small percentages
        const int copperPercent = 90;
        const int tinPercent = 10;

        // Act
        var copperNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(MinimumUseful, copperPercent);
        var tinNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(MinimumUseful, tinPercent);

        // Assert - Even with rounding, should get meaningful amounts
        // Copper: 25 * 0.90 = 22.5 / 5 = 4.5 → 5 nuggets
        Assert.Equal(5, copperNuggets);
        // Tin: 25 * 0.10 = 2.5 / 5 = 0.5 → 1 nugget
        Assert.Equal(1, tinNuggets);
    }

    #endregion

    #region Impossible Constraint Scenarios

    [Fact]
    public void ImpossibleConstraints_CannotReach100_ReturnsClosestPossible()
    {
        // Arrange - Constraints that can't sum to 100%
        // Min: 50 + 60 = 110% (impossible to be at or below 100)
        var sliderValues = new Dictionary<int, int>
        {
            { 0, 55 },
            { 1, 65 }
        };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (50, 60) },
            { 1, (60, 70) }
        };

        // Act - Should not crash, returns best effort
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, -1);

        // Assert - Both at minimum still exceeds 100, but shouldn't crash
        Assert.Equal(2, normalized.Count);
        // Algorithm should reduce what it can
        Assert.True(normalized[0] >= 50);
        Assert.True(normalized[1] >= 60);
    }

    #endregion

    #region Full Workflow Simulation

    [Fact]
    public void FullWorkflow_BronzeForOneIngot_CorrectResultsAtEachStep()
    {
        // Step 1: Initialize sliders at midpoint
        var copperMid = AlloyCalculatorLogic.CalculateMidpointPercentage(0.88, 0.92);
        var tinMid = AlloyCalculatorLogic.CalculateMidpointPercentage(0.08, 0.12);
        Assert.Equal(90, copperMid);
        Assert.Equal(10, tinMid);

        // Step 2: Normalize (already at 100%)
        var sliderValues = new Dictionary<int, int> { { 0, copperMid }, { 1, tinMid } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (88, 92) },
            { 1, (8, 12) }
        };
        var normalized = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, -1);
        Assert.Equal(100, normalized.Values.Sum());

        // Step 3: Calculate nuggets for 1 ingot
        var nuggets = AlloyCalculatorLogic.CalculateAllNuggetsRequired(OneIngot, normalized);
        Assert.Equal(18, nuggets[0]); // Copper
        Assert.Equal(2, nuggets[1]);  // Tin

        // Step 4: Allocate slots
        var amounts = new[] { nuggets[0], nuggets[1] };
        var slots = AlloyCalculatorLogic.AllocateSlotsProportionally(amounts, CrucibleSlots);
        Assert.Equal(4, slots.Sum());
        Assert.True(slots[0] >= 3, "Copper should get most slots");
    }

    [Fact]
    public void FullWorkflow_BismuthBronzeForFiveIngots_CorrectResultsAtEachStep()
    {
        // Step 1: Use typical bismuth bronze ratios
        const int copperPercent = 55;
        const int zincPercent = 25;
        const int bismuthPercent = 20;

        // Step 2: Verify they sum to 100%
        Assert.Equal(100, copperPercent + zincPercent + bismuthPercent);

        // Step 3: Calculate nuggets for 5 ingots
        var copperNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(FiveIngots, copperPercent);
        var zincNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(FiveIngots, zincPercent);
        var bismuthNuggets = AlloyCalculatorLogic.CalculateNuggetsRequired(FiveIngots, bismuthPercent);

        // 500 * 0.55 = 275 / 5 = 55 nuggets copper
        Assert.Equal(55, copperNuggets);
        // 500 * 0.25 = 125 / 5 = 25 nuggets zinc
        Assert.Equal(25, zincNuggets);
        // 500 * 0.20 = 100 / 5 = 20 nuggets bismuth
        Assert.Equal(20, bismuthNuggets);

        // Step 4: Allocate slots
        var amounts = new[] { copperNuggets, zincNuggets, bismuthNuggets };
        var slots = AlloyCalculatorLogic.AllocateSlotsProportionally(amounts, CrucibleSlots);
        Assert.Equal(4, slots.Sum());
        // Each ingredient should get at least 1 slot
        Assert.All(slots, s => Assert.True(s >= 1));
    }

    #endregion
}
