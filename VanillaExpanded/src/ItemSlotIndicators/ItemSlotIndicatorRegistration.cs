using System;
using System.Runtime.CompilerServices;

using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns one provider's optional refresh schedule and weakly held stack samples.</summary>
internal sealed class ItemSlotIndicatorRegistration
{
    private readonly IItemSlotIndicatorProvider provider;
    private readonly long refreshIntervalMilliseconds;
    private readonly Func<object?>? contextKey;
    private readonly ConditionalWeakTable<ItemStack, Sample> samples = new();

    #region Public API
    /// <summary>Creates a registration; zero interval samples every query, and context keys invalidate cached configuration.</summary>
    internal ItemSlotIndicatorRegistration(IItemSlotIndicatorProvider provider, int priority,
        long refreshIntervalMilliseconds, Func<object?>? contextKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfNegative(refreshIntervalMilliseconds);
        this.provider = provider;
        Priority = priority;
        this.refreshIntervalMilliseconds = refreshIntervalMilliseconds;
        this.contextKey = contextKey;
    }

    /// <summary>Gets the selection priority of this registration.</summary>
    internal int Priority { get; }

    /// <summary>Returns a current sample and discards output if sampling changes the item or its context.</summary>
    internal bool TryGetIndicator(ItemSlot slot, long now, out ItemSlotIndicator indicator)
    {
        indicator = default;
        ItemStack? stack = slot.Itemstack;
        if (stack is null) return provider.TryGetIndicator(slot, out indicator);
        CollectibleObject collectible = stack.Collectible;
        InventoryBase? inventory = slot.Inventory;
        ICoreAPI? api = inventory?.Api;
        object? key = contextKey?.Invoke();
        Sample? sample = refreshIntervalMilliseconds > 0 ? samples.GetOrCreateValue(stack) : null;
        if (sample is not null && sample.Initialized && now >= sample.UpdatedAt
            && now - sample.UpdatedAt < refreshIntervalMilliseconds
            && ReferenceEquals(sample.Slot, slot) && ReferenceEquals(sample.Inventory, inventory)
            && ReferenceEquals(sample.Api, api) && ReferenceEquals(sample.Collectible, collectible)
            && Equals(sample.ContextKey, key))
        {
            indicator = sample.Indicator;
            return sample.Applicable;
        }

        // Record both applicable and absent results, but never cache a transition's obsolete item identity.
        bool applicable = provider.TryGetIndicator(slot, out indicator);
        if (!ReferenceEquals(slot.Itemstack, stack) || !ReferenceEquals(stack.Collectible, collectible)
            || !ReferenceEquals(slot.Inventory, inventory) || !ReferenceEquals(inventory?.Api, api))
        {
            if (sample is not null) sample.Initialized = false;
            indicator = default;
            return false;
        }
        if (!applicable) indicator = default;
        if (sample is not null)
        {
            sample.Initialized = true;
            sample.UpdatedAt = now;
            sample.Slot = slot;
            sample.Inventory = inventory;
            sample.Api = api;
            sample.Collectible = collectible;
            sample.ContextKey = key;
            sample.Applicable = applicable;
            sample.Indicator = indicator;
        }
        return applicable;
    }
    #endregion

    #region Private
    /// <summary>Stores one provider result and the context in which it was sampled.</summary>
    private sealed class Sample
    {
        internal bool Initialized;
        internal long UpdatedAt;
        internal ItemSlot? Slot;
        internal InventoryBase? Inventory;
        internal ICoreAPI? Api;
        internal CollectibleObject? Collectible;
        internal object? ContextKey;
        internal bool Applicable;
        internal ItemSlotIndicator Indicator;
    }
    #endregion
}
