using VanillaExpanded.AlloyCalculator;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class FuelCalculationTests
{
    [Fact]
    public void CalculateFuelRequired_IncludesMetalWarmupTime()
    {
        int required = AlloyCalculatorLogic.CalculateFuelRequired(
            stackSize: 20,
            meltingPoint: 1_000,
            meltingDuration: 600,
            furnaceTemperature: 20,
            inputTemperature: 20,
            cookingTime: 0,
            activeFuelBurnTime: 0,
            activeFuelTemperature: 0,
            fuelBurnDuration: 40,
            fuelTemperature: 1_300);

        Assert.True(required > 15);
    }

    [Fact]
    public void CalculateFuelRequired_PreheatedMetalRequiresLessFuel()
    {
        int coldRequired = Calculate(inputTemperature: 20);
        int hotRequired = Calculate(inputTemperature: 1_000);

        Assert.True(hotRequired < coldRequired);
    }

    [Fact]
    public void CalculateFuelRequired_ActiveFuelReducesAdditionalFuel()
    {
        int withoutActiveFuel = Calculate(inputTemperature: 20);
        int withActiveFuel = AlloyCalculatorLogic.CalculateFuelRequired(
            stackSize: 20,
            meltingPoint: 1_000,
            meltingDuration: 600,
            furnaceTemperature: 1_100,
            inputTemperature: 20,
            cookingTime: 0,
            activeFuelBurnTime: 40,
            activeFuelTemperature: 1_300,
            fuelBurnDuration: 40,
            fuelTemperature: 1_300);

        Assert.True(withActiveFuel < withoutActiveFuel);
    }

    private static int Calculate(float inputTemperature)
    {
        return AlloyCalculatorLogic.CalculateFuelRequired(
            stackSize: 20,
            meltingPoint: 1_000,
            meltingDuration: 600,
            furnaceTemperature: 20,
            inputTemperature: inputTemperature,
            cookingTime: 0,
            activeFuelBurnTime: 0,
            activeFuelTemperature: 0,
            fuelBurnDuration: 40,
            fuelTemperature: 1_300);
    }
}