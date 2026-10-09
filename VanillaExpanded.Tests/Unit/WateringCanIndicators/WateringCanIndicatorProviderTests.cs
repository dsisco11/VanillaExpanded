using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.WateringCanIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WateringCanIndicators;

/// <summary>Checks real watering-can attributes and the reported water fractions.</summary>
[Trait("Category", "Unit")]
public sealed class WateringCanIndicatorProviderTests
{
    #region Public API
    #region Applicability
    /// <summary>Empty slots and other collectibles receive no watering-can indicator.</summary>
    [Fact]
    public void NonWateringCan_ReturnsFalse()
    {
        var provider = new WateringCanIndicatorProvider();
        var slot = new ItemSlot(null);

        Assert.False(provider.TryGetIndicator(slot, out var indicator));
        Assert.Equal(default, indicator);
        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(id: 1));
        Assert.False(provider.TryGetIndicator(slot, out indicator));
        Assert.Equal(default, indicator);
        slot.Itemstack = new ItemStack(new Block());
        Assert.False(provider.TryGetIndicator(slot, out _));
    }

    /// <summary>A malformed capacity does not produce an invalid indicator.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidCapacity_ReturnsFalse(float capacity)
    {
        var slot = CreateCanSlot(10, capacity);

        Assert.False(new WateringCanIndicatorProvider().TryGetIndicator(slot, out _));
    }
    #endregion

    #region Water Levels
    /// <summary>Water fill respects each can's capacity and clamps overfilled values.</summary>
    [Theory]
    [InlineData(32, 32, 1)]
    [InlineData(8, 32, 0.25f)]
    [InlineData(25, 100, 0.25f)]
    [InlineData(200, 100, 1)]
    public void WaterLevel_ReportsClampedResourceFraction(float remaining, float capacity, float expected)
    {
        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(CreateCanSlot(remaining, capacity), out var indicator));

        Assert.Equal(expected, indicator.Fill);
    }

    /// <summary>Empty and negative levels remain eligible for an indicator.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(float.NaN)]
    public void EmptyCan_RemainsApplicable(float remaining)
    {
        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(CreateCanSlot(remaining), out _));
    }

    /// <summary>A newly created can without a water attribute remains eligible for an indicator.</summary>
    [Fact]
    public void MissingWaterAttribute_RemainsApplicable()
    {
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(new BlockWateringCan()) };

        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(slot, out _));
    }

    /// <summary>Pouring, emptying, and refilling the same stack update on the next query.</summary>
    [Fact]
    public void WaterChanges_UpdateImmediately()
    {
        var slot = CreateCanSlot(32);
        var stack = Assert.IsType<ItemStack>(slot.Itemstack);
        var can = Assert.IsType<BlockWateringCan>(stack.Collectible);
        var provider = new WateringCanIndicatorProvider();
        Assert.True(provider.TryGetIndicator(slot, out var full));
        Assert.Equal(1, full.Fill);

        can.SetRemainingWateringSeconds(stack, 16);
        Assert.True(provider.TryGetIndicator(slot, out var half));
        Assert.Equal(0.5f, half.Fill);

        can.SetRemainingWateringSeconds(stack, 0);
        Assert.True(provider.TryGetIndicator(slot, out _));

        can.SetRemainingWateringSeconds(stack, 32);
        Assert.True(provider.TryGetIndicator(slot, out var refilled));
        Assert.Equal(full, refilled);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Creates a real can stack using the game's water-level setter.</summary>
    private static ItemSlot CreateCanSlot(float remaining, float capacity = 32)
    {
        var can = new BlockWateringCan { CapacitySeconds = capacity };
        var stack = new ItemStack(can);
        can.SetRemainingWateringSeconds(stack, remaining);
        return new ItemSlot(null) { Itemstack = stack };
    }
    #endregion
}
