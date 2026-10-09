using System.Numerics;
using Moq;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators;

/// <summary>Checks deterministic refresh scheduling and context invalidation at the system boundary.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorSchedulingTests
{
    #region Public API
    #region Scheduling
    /// <summary>Default registration caches results through 999 milliseconds and refreshes at 1000, including absent results.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultInterval_ExpiresAtOneSecond(bool applicable)
    {
        long now = 0;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        var provider = new CountingProvider { Applicable = applicable };
        var slot = CreateSlot();
        system.Register(provider);
        Assert.Equal(applicable, system.TryGetRenderSelection(slot, out var first));
        now = 999;
        Assert.Equal(applicable, system.TryGetRenderSelection(slot, out var cached));
        Assert.Equal(first, cached);
        Assert.Equal(1, provider.Calls);
        now = 1000;
        Assert.Equal(applicable, system.TryGetRenderSelection(slot, out _));
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>An explicit zero interval samples on every query without requiring clock changes.</summary>
    [Fact]
    public void ZeroInterval_RemainsImmediate()
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var provider = new CountingProvider();
        var slot = CreateSlot();
        system.Register(provider, refreshIntervalMilliseconds: 0);
        system.TryGetRenderSelection(slot, out _);
        system.TryGetRenderSelection(slot, out _);
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>Each provider and stack gets an independent schedule, and false results permit fallback.</summary>
    [Fact]
    public void RegistrationsAndStacks_HaveIndependentSchedules()
    {
        long now = 0;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        var first = new CountingProvider { Applicable = false };
        var second = new CountingProvider();
        var slot = CreateSlot();
        var other = CreateSlot();
        system.Register(first, 10, 1000);
        system.Register(second, 0, 2000);
        Assert.True(system.TryGetRenderSelection(slot, out _));
        now = 500;
        Assert.True(system.TryGetRenderSelection(other, out _));
        Assert.Equal(2, first.Calls);
        Assert.Equal(2, second.Calls);
        now = 1000;
        Assert.True(system.TryGetRenderSelection(slot, out _));
        Assert.True(system.TryGetRenderSelection(other, out _));
        Assert.Equal(3, first.Calls);
        Assert.Equal(2, second.Calls);
    }

    /// <summary>Value-based configuration changes invalidate the sample before its timer expires.</summary>
    [Fact]
    public void ContextKeyChange_RefreshesImmediately()
    {
        var key = (Enabled: true, Intensity: 0.5f);
        var provider = new CountingProvider();
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var slot = CreateSlot();
        system.Register(provider, refreshIntervalMilliseconds: 1000, contextKey: () => key);
        system.TryGetRenderSelection(slot, out _);
        system.TryGetRenderSelection(slot, out _);
        Assert.Equal(1, provider.Calls);
        key = (true, 0.2f);
        system.TryGetRenderSelection(slot, out _);
        key = (false, 0.2f);
        provider.Applicable = false;
        Assert.False(system.TryGetRenderSelection(slot, out _));
        Assert.Equal(3, provider.Calls);
    }

    /// <summary>Clearing registrations discards samples even when the same provider is registered again.</summary>
    [Fact]
    public void Clear_DiscardsCachedResults()
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var provider = new CountingProvider();
        var slot = CreateSlot();
        system.Register(provider, refreshIntervalMilliseconds: 1000);
        Assert.True(system.TryGetRenderSelection(slot, out _));
        system.Clear();
        Assert.False(system.TryGetRenderSelection(slot, out _));
        system.Register(provider, refreshIntervalMilliseconds: 1000);
        Assert.True(system.TryGetRenderSelection(slot, out _));
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>Negative refresh intervals are rejected without adding a registration.</summary>
    [Fact]
    public void NegativeInterval_IsRejected()
    {
        var system = new ItemSlotIndicatorSystem();
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Register(new CountingProvider(), refreshIntervalMilliseconds: -1));
        Assert.False(system.TryGetRenderSelection(CreateSlot(), out _));
    }
    #endregion

    #region Context
    /// <summary>Replacing the stack, collectible, slot, or API invalidates a cached result immediately.</summary>
    [Theory]
    [InlineData("stack")]
    [InlineData("collectible")]
    [InlineData("slot")]
    [InlineData("api")]
    public void ContextIdentityChange_RefreshesImmediately(string context)
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var provider = new CountingProvider();
        var slot = CreateSlot();
        system.Register(provider, refreshIntervalMilliseconds: 1000);
        Assert.True(system.TryGetRenderSelection(slot, out _));
        // Alter one ownership identity while leaving time fixed.
        switch (context)
        {
            case "stack": slot.Itemstack = new ItemStack(slot.Itemstack!.Collectible); break;
            case "collectible": slot.Itemstack!.SetFrom(new ItemStack(MockItem.CreateNonLightSource(2))); break;
            case "slot": slot = new ItemSlot(slot.Inventory) { Itemstack = slot.Itemstack }; break;
            case "api": slot.Inventory.Api = Mock.Of<ICoreAPI>(); break;
        }
        Assert.True(system.TryGetRenderSelection(slot, out _));
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>API mutation during sampling discards the obsolete result and retries on the next query.</summary>
    [Fact]
    public void ApiMutationDuringSampling_DiscardsResult()
    {
        var slot = CreateSlot();
        var provider = new CountingProvider { DuringSample = value => value.Inventory.Api = Mock.Of<ICoreAPI>() };
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        system.Register(provider, refreshIntervalMilliseconds: 1000);
        Assert.False(system.TryGetRenderSelection(slot, out var discarded));
        Assert.Equal(default, discarded);
        provider.DuringSample = null;
        Assert.True(system.TryGetRenderSelection(slot, out _));
        Assert.Equal(2, provider.Calls);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a stack in an actual inventory with an isolated API identity.</summary>
    private static ItemSlot CreateSlot()
    {
        var inventory = new InventoryGeneric(1, "schedule", Guid.NewGuid().ToString(), null!)
        {
            Api = Mock.Of<ICoreAPI>(),
            InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>()
        };
        inventory[0].Itemstack = new ItemStack(MockItem.CreateNonLightSource(1));
        return inventory[0];
    }

    /// <summary>Records sampling and can simulate context mutation without graphics dependencies.</summary>
    private sealed class CountingProvider : IItemSlotIndicatorProvider
    {
        internal int Calls;
        internal bool Applicable = true;
        internal Action<ItemSlot>? DuringSample;

        #region Public API
        /// <summary>Returns a distinct result for each sample and optionally mutates its context.</summary>
        public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
        {
            Calls++;
            DuringSample?.Invoke(slot);
            indicator = new ItemSlotIndicator(Calls / 10f, Vector4.One);
            return Applicable;
        }
        #endregion
    }
    #endregion
}
