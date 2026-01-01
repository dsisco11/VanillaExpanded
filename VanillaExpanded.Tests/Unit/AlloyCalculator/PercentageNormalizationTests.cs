using VanillaExpanded.AlloyCalculator;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>
/// Tests for percentage normalization logic in AlloyCalculatorLogic.
/// </summary>
[Trait("Category", "Unit")]
public class PercentageNormalizationTests
{
    #region Sum to 100% Constraint

    [Fact]
    public void NormalizePercentages_AlreadyAt100_NoChange()
    {
        var sliderValues = new Dictionary<int, int> { { 0, 60 }, { 1, 40 } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) },
            { 1, (10, 90) }
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, -1);

        Assert.Equal(60, result[0]);
        Assert.Equal(40, result[1]);
    }

    [Fact]
    public void NormalizePercentages_Over100_DecreasesOthers()
    {
        // Total is 120, need to reduce by 20
        var sliderValues = new Dictionary<int, int> { { 0, 70 }, { 1, 50 } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) },
            { 1, (10, 90) }
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Slider 0 was changed, so slider 1 should be decreased
        Assert.Equal(70, result[0]); // Unchanged (was the changed slider)
        Assert.True(result[1] < 50); // Should be decreased
        Assert.True(result.Values.Sum() <= 100);
    }

    [Fact]
    public void NormalizePercentages_Under100_IncreasesOthers()
    {
        // Total is 80, need to increase by 20
        var sliderValues = new Dictionary<int, int> { { 0, 30 }, { 1, 50 } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) },
            { 1, (10, 90) }
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Slider 0 was changed, so slider 1 should be increased
        Assert.Equal(30, result[0]); // Unchanged
        Assert.True(result[1] > 50); // Should be increased
    }

    #endregion

    #region Min/Max Constraints

    [Fact]
    public void NormalizePercentages_RespectsMinConstraint()
    {
        // Total is 120, need to reduce by 20, but slider 1 is at min
        var sliderValues = new Dictionary<int, int> { { 0, 80 }, { 1, 40 } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) },
            { 1, (40, 50) } // Already at min!
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Slider 1 cannot go below 40
        Assert.True(result[1] >= 40);
    }

    [Fact]
    public void NormalizePercentages_RespectsMaxConstraint()
    {
        // Total is 60, need to increase by 40, but slider 1 is at max
        var sliderValues = new Dictionary<int, int> { { 0, 20 }, { 1, 40 } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) },
            { 1, (30, 40) } // Already at max!
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Slider 1 cannot go above 40
        Assert.True(result[1] <= 40);
    }

    #endregion

    #region Initial Normalization (changedIndex = -1)

    [Fact]
    public void NormalizePercentages_InitialNormalization_AdjustsAllSliders()
    {
        // Initial setup where midpoints don't sum to 100
        var sliderValues = new Dictionary<int, int> { { 0, 50 }, { 1, 30 }, { 2, 40 } }; // Total: 120
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) },
            { 1, (10, 90) },
            { 2, (10, 90) }
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, -1);

        // All sliders should be adjusted
        var total = result.Values.Sum();
        Assert.True(Math.Abs(total - 100) <= 1); // Should be close to 100
    }

    #endregion

    #region Proportional Distribution

    [Fact]
    public void NormalizePercentages_MultipleSliders_ProportionalAdjustment()
    {
        // 3 sliders, need to reduce by 30
        var sliderValues = new Dictionary<int, int> { { 0, 60 }, { 1, 40 }, { 2, 30 } }; // Total: 130
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (20, 80) },
            { 1, (20, 60) },
            { 2, (10, 50) }
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Slider 0 unchanged, others should be reduced proportionally
        Assert.Equal(60, result[0]);
        Assert.True(result[1] < 40);
        Assert.True(result[2] < 30);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NormalizePercentages_SingleSlider_NoOthersToAdjust()
    {
        var sliderValues = new Dictionary<int, int> { { 0, 80 } };
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) }
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Can't adjust anything, returns as-is
        Assert.Equal(80, result[0]);
    }

    [Fact]
    public void NormalizePercentages_EmptyInputs_ReturnsEmpty()
    {
        var result = AlloyCalculatorLogic.NormalizePercentages(
            new Dictionary<int, int>(),
            new Dictionary<int, (int min, int max)>(),
            -1);

        Assert.Empty(result);
    }

    [Fact]
    public void NormalizePercentages_MissingConstraint_SkipsSlider()
    {
        var sliderValues = new Dictionary<int, int> { { 0, 60 }, { 1, 50 } }; // Total: 110
        var constraints = new Dictionary<int, (int min, int max)>
        {
            { 0, (10, 90) }
            // Missing constraint for index 1
        };

        var result = AlloyCalculatorLogic.NormalizePercentages(sliderValues, constraints, 0);

        // Should not crash, slider 1 is skipped for adjustment
        Assert.Equal(60, result[0]);
    }

    #endregion
}
