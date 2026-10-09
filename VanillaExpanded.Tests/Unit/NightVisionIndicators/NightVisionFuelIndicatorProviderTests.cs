using VanillaExpanded.NightVisionIndicators;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.NightVisionIndicators;

/// <summary>Checks night-vision fuel indicators using the game's fuel APIs and per-device capacity.</summary>
[Trait("Category", "Unit")]
public sealed class NightVisionFuelIndicatorProviderTests
{
    #region Public API
    #region Eligibility
    /// <summary>Empty slots, ordinary items, and blocks receive no night-vision indicator.</summary>
    [Fact]
    public void NonDevice_ReturnsFalse()
    {
        var provider = new NightVisionFuelIndicatorProvider();
        var slot = new ItemSlot(null);
        Assert.False(provider.TryGetIndicator(slot, out _));
        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(1));
        Assert.False(provider.TryGetIndicator(slot, out _));
        slot.Itemstack = new ItemStack(new Block());
        Assert.False(provider.TryGetIndicator(slot, out _));
    }

    /// <summary>Malformed capacity cannot produce a usable fuel fraction.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidCapacity_ReturnsFalse(float capacity)
    {
        var slot = CreateDeviceSlot(12, new AdjustableDevice { CapacityHours = capacity });
        Assert.False(new NightVisionFuelIndicatorProvider().TryGetIndicator(slot, out _));
    }

    /// <summary>Nonfinite fuel exposed by the game API is rejected.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFuel_ReturnsFalse(double fuel)
    {
        Assert.False(new NightVisionFuelIndicatorProvider().TryGetIndicator(CreateDeviceSlot(fuel), out _));
    }
    #endregion

    #region Fuel
    /// <summary>Fuel is divided by the standard device capacity and overfill remains read-only.</summary>
    [Theory]
    [InlineData(6, 0.25f)]
    [InlineData(12, 0.5f)]
    [InlineData(24, 1)]
    [InlineData(48, 1)]
    public void Fuel_ProducesClampedProportionalFill(double fuel, float expected)
    {
        var slot = CreateDeviceSlot(fuel);
        Assert.True(new NightVisionFuelIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(expected, indicator.Fill);
        Assert.Equal(fuel, slot.Itemstack!.Attributes.GetDecimal("fuelHours"));
    }

    /// <summary>Missing, empty, and negative fuel remain eligible without mutating the stack.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(double.NegativeInfinity)]
    public void EmptyFuel_RemainsApplicableWithoutMutation(double? fuel)
    {
        var slot = CreateDeviceSlot(fuel);
        Assert.True(new NightVisionFuelIndicatorProvider().TryGetIndicator(slot, out _));
        Assert.Equal(fuel.HasValue, slot.Itemstack!.Attributes.HasAttribute("fuelHours"));
    }

    /// <summary>Capacity access samples each device instance and observes later field changes.</summary>
    [Fact]
    public void Capacity_UsesCurrentInstanceField()
    {
        var device = new AdjustableDevice { CapacityHours = 48 };
        var slot = CreateDeviceSlot(12, device);
        var provider = new NightVisionFuelIndicatorProvider();
        Assert.True(provider.TryGetIndicator(slot, out var quarter));
        Assert.Equal(0.25f, quarter.Fill);
        Assert.True(provider.TryGetIndicator(CreateDeviceSlot(12), out var standard));
        Assert.Equal(0.5f, standard.Fill);

        // A cached accessor must retain the field location, not the first observed capacity value.
        device.CapacityHours = 12;
        Assert.True(provider.TryGetIndicator(slot, out var full));
        Assert.Equal(1, full.Fill);
    }

    /// <summary>The next query reflects consumption and refueling through the game's methods.</summary>
    [Fact]
    public void FuelChanges_UpdateImmediately()
    {
        var slot = CreateDeviceSlot(24);
        var stack = slot.Itemstack!;
        var device = (ItemNightvisiondevice)stack.Collectible;
        var provider = new NightVisionFuelIndicatorProvider();
        Assert.True(provider.TryGetIndicator(slot, out var full));
        device.AddFuelHours(stack, -12);
        Assert.True(provider.TryGetIndicator(slot, out var half));
        Assert.Equal(0.5f, half.Fill);
        device.AddFuelHours(stack, -12);
        Assert.True(provider.TryGetIndicator(slot, out _));
        device.SetFuelHours(stack, 24);
        Assert.True(provider.TryGetIndicator(slot, out var refilled));
        Assert.Equal(full, refilled);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a genuine device stack with optionally initialized fuel.</summary>
    private static ItemSlot CreateDeviceSlot(double? fuel, ItemNightvisiondevice? device = null)
    {
        device ??= new ItemNightvisiondevice();
        var stack = new ItemStack(device);
        if (fuel.HasValue) device.SetFuelHours(stack, fuel.Value);
        return new ItemSlot(null) { Itemstack = stack };
    }

    /// <summary>Exposes the protected capacity field for testing derived device definitions.</summary>
    private sealed class AdjustableDevice : ItemNightvisiondevice
    {
        #region Public API
        /// <summary>Sets the actual field consumed by the base game's refueling policy.</summary>
        public float CapacityHours { set => fuelHoursCapacity = value; }
        #endregion
    }
    #endregion
}
