using VanillaExpanded.AlloyCalculator;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>
/// Tests for nugget calculation logic in AlloyCalculatorLogic.
/// </summary>
[Trait("Category", "Unit")]
public class NuggetCalculationTests
{
    #region CalculateNuggetsRequired - Basic Cases

    [Fact]
    public void CalculateNuggetsRequired_ExactMultipleOfFive_ReturnsExactNuggets()
    {
        // 100 units * 50% = 50 units / 5 = 10 nuggets
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(100, 50);

        Assert.Equal(10, result);
    }

    [Fact]
    public void CalculateNuggetsRequired_NotMultipleOfFive_RoundsUp()
    {
        // 100 units * 33% = 33 units / 5 = 6.6 → 7 nuggets
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(100, 33);

        Assert.Equal(7, result);
    }

    [Fact]
    public void CalculateNuggetsRequired_SmallAmount_RoundsUpToOne()
    {
        // 100 units * 1% = 1 unit / 5 = 0.2 → 1 nugget
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(100, 1);

        Assert.Equal(1, result);
    }

    #endregion

    #region CalculateNuggetsRequired - Edge Cases

    [Fact]
    public void CalculateNuggetsRequired_ZeroTargetUnits_ReturnsZero()
    {
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(0, 50);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateNuggetsRequired_NegativeTargetUnits_ReturnsZero()
    {
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(-100, 50);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateNuggetsRequired_ZeroPercentage_ReturnsZero()
    {
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(100, 0);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateNuggetsRequired_NegativePercentage_ReturnsZero()
    {
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(100, -10);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateNuggetsRequired_100Percent_ReturnsFullAmount()
    {
        // 100 units * 100% = 100 units / 5 = 20 nuggets
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(100, 100);

        Assert.Equal(20, result);
    }

    #endregion

    #region CalculateNuggetsRequired - Realistic Alloy Scenarios

    [Theory]
    [InlineData(100, 88, 18)] // Bronze copper: 88 units / 5 = 17.6 → 18
    [InlineData(100, 12, 3)]  // Bronze tin: 12 units / 5 = 2.4 → 3
    [InlineData(200, 50, 20)] // Equal split at 200 units: 100 / 5 = 20
    [InlineData(50, 60, 6)]   // 50 * 0.6 = 30 / 5 = 6
    public void CalculateNuggetsRequired_RealisticValues_CorrectResults(int targetUnits, int percentage, int expectedNuggets)
    {
        var result = AlloyCalculatorLogic.CalculateNuggetsRequired(targetUnits, percentage);

        Assert.Equal(expectedNuggets, result);
    }

    #endregion

    #region CalculateAllNuggetsRequired

    [Fact]
    public void CalculateAllNuggetsRequired_MultipleIngredients_CalculatesAll()
    {
        var percentages = new Dictionary<int, int>
        {
            { 0, 88 }, // Copper
            { 1, 12 }  // Tin
        };

        var result = AlloyCalculatorLogic.CalculateAllNuggetsRequired(100, percentages);

        Assert.Equal(2, result.Count);
        Assert.Equal(18, result[0]); // 88 units / 5 = 17.6 → 18
        Assert.Equal(3, result[1]);  // 12 units / 5 = 2.4 → 3
    }

    [Fact]
    public void CalculateAllNuggetsRequired_ZeroPercentage_ExcludedFromResult()
    {
        var percentages = new Dictionary<int, int>
        {
            { 0, 50 },
            { 1, 0 },  // Zero percentage
            { 2, 50 }
        };

        var result = AlloyCalculatorLogic.CalculateAllNuggetsRequired(100, percentages);

        Assert.Equal(2, result.Count);
        Assert.False(result.ContainsKey(1)); // Zero percentage excluded
    }

    [Fact]
    public void CalculateAllNuggetsRequired_EmptyPercentages_ReturnsEmpty()
    {
        var result = AlloyCalculatorLogic.CalculateAllNuggetsRequired(100, new Dictionary<int, int>());

        Assert.Empty(result);
    }

    #endregion

    #region CalculateMidpointPercentage

    [Theory]
    [InlineData(0.0, 1.0, 50)]   // Full range: midpoint is 50%
    [InlineData(0.2, 0.4, 30)]   // 20-40%: midpoint is 30%
    [InlineData(0.85, 0.92, 88)] // 85-92%: midpoint is 88 (85+92)/2 = 88.5 truncated
    [InlineData(0.1, 0.1, 10)]   // Same min/max: midpoint equals both
    public void CalculateMidpointPercentage_VariousRanges_CorrectMidpoint(double minRatio, double maxRatio, int expectedMidpoint)
    {
        var result = AlloyCalculatorLogic.CalculateMidpointPercentage(minRatio, maxRatio);

        Assert.Equal(expectedMidpoint, result);
    }

    #endregion
}
