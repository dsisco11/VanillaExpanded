using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Exercises actual bloomery execution at classification and engine/AutoStash ratio boundaries.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class BloomeryBoundaryTests
{
    #region Public API
    #region Classification
    /// <summary>Isolates fuel temperature/duration boundaries with valid existing ore and free fuel capacity.</summary>
    [Theory]
    [InlineData(1199, 31, false)]
    [InlineData(1200, 31, true)]
    [InlineData(1200, 29.999, false)]
    [InlineData(1200, 30, false)]
    [InlineData(1200, 30.001, true)]
    public void FuelThresholds_ControlActualTransfer(int temperature, double duration, bool accepted)
    {
        var test = new BloomeryCase();
        test.Fuel.CombustibleProps!.BurnTemperature = temperature;
        test.Fuel.CombustibleProps.BurnDuration = (float)duration;
        test.Seed(test.Target.OreSlot, test.Ore, 5);
        var fuel = test.Source(test.Fuel, 4);
        var before = test.Snapshot();
        Assert.Equal(accepted, test.Target.CanAdd(fuel.Itemstack));
        Assert.Equal(accepted, test.Run());
        test.AssertSlot(fuel, test.Fuel, accepted ? 1 : 4);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, accepted ? 3 : 0);
        if (accepted) test.Verify(before, true, fuel, test.Target.FuelSlot);
        else test.Verify(before, false);
    }

    /// <summary>Checks inclusive minimum and exclusive maximum ore melting points through real movement.</summary>
    [Theory]
    [InlineData(BlockEntityBloomery.MinTemp - 1, false)]
    [InlineData(BlockEntityBloomery.MinTemp, true)]
    [InlineData(BlockEntityBloomery.MaxTemp - 1, true)]
    [InlineData(BlockEntityBloomery.MaxTemp, false)]
    public void OreMeltingPointBoundaries_ControlActualTransfer(int temperature, bool accepted)
    {
        var test = new BloomeryCase();
        test.Ore.CombustibleProps!.MeltingPoint = temperature;
        var ore = test.Source(test.Ore, 3);
        var before = test.Snapshot();
        Assert.Equal(accepted, test.Target.CanAdd(ore.Itemstack));
        Assert.Equal(accepted, test.Run());
        test.AssertSlot(ore, test.Ore, accepted ? 0 : 3);
        test.AssertSlot(test.Target.OreSlot, test.Ore, accepted ? 3 : 0);
        if (accepted) test.Verify(before, true, ore, test.Target.OreSlot);
        else test.Verify(before, false);
    }

    /// <summary>Rejects otherwise valid-temperature ore lacking smelted output, with a paired positive case.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmeltedOutput_IsRequiredForOre(bool hasOutput)
    {
        var test = new BloomeryCase();
        if (!hasOutput) test.Ore.CombustibleProps!.SmeltedStack = null;
        var ore = test.Source(test.Ore, 3);
        var before = test.Snapshot();
        Assert.Equal(hasOutput, test.Target.CanAdd(ore.Itemstack));
        Assert.Equal(hasOutput, test.Run());
        test.AssertSlot(ore, test.Ore, hasOutput ? 0 : 3);
        test.AssertSlot(test.Target.OreSlot, test.Ore, hasOutput ? 3 : 0);
        if (hasOutput) test.Verify(before, true, ore, test.Target.OreSlot);
        else test.Verify(before, false);
    }
    #endregion
    #region Ratio interaction
    /// <summary>Characterizes configured overrides and clamping while preserving the engine's initial empty-ore gate.</summary>
    [Theory]
    [InlineData(2, null, 12)]
    [InlineData(2, 3, 18)]
    [InlineData(2, 1, 6)]
    [InlineData(0, null, 6)]
    [InlineData(-2, null, 6)]
    [InlineData(2, 0, 6)]
    [InlineData(2, -2, 6)]
    [InlineData(0, 3, 18)]
    public void EmptyOre_ConfiguredAndNonpositiveRatiosUseObservedCapacity(int ratio, int? configured, int oreCount)
    {
        var test = new BloomeryCase(ratio);
        if (configured.HasValue)
            test.Ore.Attributes = JsonObject.FromJson("{\"bloomeryFuelRatio\":" + configured.Value + "}");
        var ore = test.Source(test.Ore, 20);
        var fuel = test.Source(test.Fuel, 10, 1);
        var before = test.Snapshot();
        // Before the first ore deposit vanilla reads its empty ore slot and uses ratio one.
        Assert.True(test.Target.CanAdd(ore.Itemstack));
        Assert.True(test.Run());
        test.AssertSlot(ore, test.Ore, 20 - oreCount);
        test.AssertSlot(fuel, test.Fuel, 4);
        test.AssertSlot(test.Target.OreSlot, test.Ore, oreCount);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 6);
        test.Verify(before, true, ore, fuel, test.Target.OreSlot, test.Target.FuelSlot);
    }

    /// <summary>Existing nonpositive engine ratios reject ore before AutoStash's clamped allowance but still permit fuel.</summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(-2, null)]
    [InlineData(2, 0)]
    [InlineData(2, -2)]
    public void ExistingOre_NonpositiveEngineRatioBlocksOreWhileFuelUsesClamp(int ratio, int? configured)
    {
        var test = new BloomeryCase(ratio);
        if (configured.HasValue)
            test.Ore.Attributes = JsonObject.FromJson("{\"bloomeryFuelRatio\":" + configured.Value + "}");
        test.Seed(test.Target.OreSlot, test.Ore, 2);
        var ore = test.Source(test.Ore, 3);
        var fuel = test.Source(test.Fuel, 5, 1);
        var before = test.Snapshot();
        Assert.False(test.Target.CanAdd(ore.Itemstack));
        Assert.True(test.Target.CanAdd(fuel.Itemstack));
        Assert.True(test.Run());
        test.AssertSlot(ore, test.Ore, 3);
        test.AssertSlot(fuel, test.Fuel, 3);
        test.AssertSlot(test.Target.FuelSlot, test.Fuel, 2);
        Assert.Empty(ore.Attempts);
        test.Verify(before, true, fuel, test.Target.FuelSlot);
    }
    #endregion
    #endregion
}
