using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.WateringCanIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WateringCanIndicators;

/// <summary>Checks real watering-can attributes and the resulting indicator fill and palette.</summary>
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

    #region Fill And Color
    /// <summary>Water fill respects each can's capacity and clamps overfilled values.</summary>
    [Theory]
    [InlineData(32, 32, 1)]
    [InlineData(8, 32, 0.25f)]
    [InlineData(25, 100, 0.25f)]
    [InlineData(200, 100, 1)]
    public void WaterLevel_ProducesProportionalBlueFill(float remaining, float capacity, float expected)
    {
        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(CreateCanSlot(remaining, capacity), out var indicator));

        Assert.Equal(expected, indicator.Fill);
        Assert.True(indicator.Color.Z > indicator.Color.Y);
        Assert.True(indicator.Color.Y > indicator.Color.X);
    }

    /// <summary>Empty and negative levels fill the slot with the shared freshness-red hue.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(float.NaN)]
    public void EmptyCan_ProducesFullSharedRedWarning(float remaining)
    {
        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(CreateCanSlot(remaining), out var indicator));

        Assert.Equal(1, indicator.Fill);
        var stale = FreshnessIndicatorProvider.FreshnessColor(0);
        Assert.Equal(stale.X, indicator.Color.X);
        Assert.Equal(stale.Y, indicator.Color.Y);
        Assert.Equal(stale.Z, indicator.Color.Z);
        Assert.Equal(0.6f, indicator.Color.W);
    }

    /// <summary>A newly created can without a water attribute is empty.</summary>
    [Fact]
    public void MissingWaterAttribute_ProducesFullRedWarning()
    {
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(new BlockWateringCan()) };

        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(slot, out var indicator));

        Assert.Equal(1, indicator.Fill);
        Assert.True(indicator.Color.X > indicator.Color.Z);
    }

    /// <summary>Near-empty water stays dark blue rather than becoming the empty-can warning.</summary>
    [Fact]
    public void Palette_LightensContinuouslyTowardFull()
    {
        var provider = new WateringCanIndicatorProvider();
        Assert.True(provider.TryGetIndicator(CreateCanSlot(0.01f), out var low));
        Assert.True(provider.TryGetIndicator(CreateCanSlot(16), out var half));
        Assert.True(provider.TryGetIndicator(CreateCanSlot(32), out var full));

        Assert.True(low.Color.Z > low.Color.X);
        Assert.True(low.Color.X < half.Color.X && half.Color.X < full.Color.X);
        Assert.True(low.Color.Y < half.Color.Y && half.Color.Y < full.Color.Y);
        Assert.True(low.Color.Z < half.Color.Z && half.Color.Z < full.Color.Z);
        Assert.InRange(full.Color.X, 0, 0.2f);
        Assert.InRange(full.Color.Y, 0.4f, 0.55f);
        Assert.InRange(full.Color.Z, 0.8f, 0.95f);
        Assert.True(full.Color.Y - low.Color.Y >= 0.4f);
        Assert.True(full.Color.Z - low.Color.Z >= 0.7f);
        Assert.Equal(0.75f, full.Color.W);
        Assert.Equal(0.65f, half.Color.W, precision: 5);
        Assert.Equal(0.55f, low.Color.W, precision: 3);
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
        Assert.True(provider.TryGetIndicator(slot, out var empty));
        Assert.Equal(1, empty.Fill);
        Assert.True(empty.Color.X > empty.Color.Z);

        can.SetRemainingWateringSeconds(stack, 32);
        Assert.True(provider.TryGetIndicator(slot, out var refilled));
        Assert.Equal(full, refilled);
    }

    /// <summary>The empty warning covers the whole slot with correctly premultiplied red.</summary>
    [Fact]
    public void EmptyWarning_RendersFullHeightAndPremultipliedRed()
    {
        Assert.True(new WateringCanIndicatorProvider().TryGetIndicator(CreateCanSlot(0), out var indicator));
        var bounds = ItemSlotIndicatorRenderer.CalculateBounds(100, 100, 48, indicator.Fill);
        var color = ItemSlotIndicatorRenderer.PremultiplyColor(indicator.Color);

        Assert.Equal(48, bounds.Height);
        Assert.Equal(76, bounds.Y);
        Assert.True(color.R > color.G && color.R > color.B);
        Assert.True(color.R <= color.A && color.G <= color.A && color.B <= color.A);
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