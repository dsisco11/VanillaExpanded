using System;
using System.Runtime.CompilerServices;

using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns one provider's optional refresh schedule and weakly held stack samples.</summary>
internal sealed class ItemSlotIndicatorRegistration
{
    private readonly IItemSlotIndicatorProvider provider;
    private readonly long refreshIntervalMilliseconds;
    private readonly Func<object?>? contextKey;
    private readonly AdaptiveSamplingOptions? adaptiveSampling;
    private readonly ConditionalWeakTable<ItemStack, Sample> samples = new();

    #region Public API
    /// <summary>Creates a registration; zero interval samples every query, and context keys invalidate cached configuration.</summary>
    internal ItemSlotIndicatorRegistration(IItemSlotIndicatorProvider provider, int priority,
        long refreshIntervalMilliseconds, Func<object?>? contextKey, AdaptiveSamplingOptions? adaptiveSampling = null,
        ItemSlotIndicatorEffectDefinition? effect = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfNegative(refreshIntervalMilliseconds);
        adaptiveSampling?.Validate();
        if (refreshIntervalMilliseconds > 0 && adaptiveSampling is { } options
            && options.ActiveIntervalMilliseconds >= refreshIntervalMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(adaptiveSampling), "Active interval must be shorter than the idle interval.");
        this.adaptiveSampling = adaptiveSampling;
        this.provider = provider;
        Priority = priority;
        Effect = effect;
        this.refreshIntervalMilliseconds = refreshIntervalMilliseconds;
        this.contextKey = contextKey;
    }

    /// <summary>Gets the selection priority of this registration.</summary>
    internal int Priority { get; }

    /// <summary>Gets the optional rendering effect, independently of cached provider samples.</summary>
    internal ItemSlotIndicatorEffectDefinition? Effect { get; }

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
        bool sameContext = sample is not null && sample.Initialized && now >= sample.UpdatedAt
            && ReferenceEquals(sample.Slot, slot) && ReferenceEquals(sample.Inventory, inventory)
            && ReferenceEquals(sample.Api, api) && ReferenceEquals(sample.Collectible, collectible)
            && Equals(sample.ContextKey, key);
        if (sameContext && now < sample!.NextSampleAt)
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
            // Compare consecutive samples only: slow drift must not accumulate into false activity.
            if (!sameContext) sample.Active = false;
            else if (adaptiveSampling is { } activeOptions)
            {
                if (HasMeaningfulChange(sample, applicable, indicator, activeOptions))
                {
                    sample.Active = true;
                    sample.LastMeaningfulChangeAt = now;
                }
                else if (sample.Active && now - sample.LastMeaningfulChangeAt >= activeOptions.SettleMilliseconds)
                    sample.Active = false;
            }
            long interval = sample.Active ? adaptiveSampling!.Value.ActiveIntervalMilliseconds : refreshIntervalMilliseconds;
            // Schedule from this query, without catching up on missed render-time samples.
            sample.NextSampleAt = now > long.MaxValue - interval ? long.MaxValue : now + interval;
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
    /// <summary>Detects applicability changes and significant differences in visible output.</summary>
    private static bool HasMeaningfulChange(Sample previous, bool applicable, ItemSlotIndicator current,
        AdaptiveSamplingOptions options)
    {
        if (previous.Applicable != applicable) return true;
        if (!applicable) return false;
        var difference = System.Numerics.Vector4.Abs(current.Color - previous.Indicator.Color);
        return Math.Abs(current.Fill - previous.Indicator.Fill) > options.FillChangeThreshold
            || difference.X > options.ColorChangeThreshold || difference.Y > options.ColorChangeThreshold
            || difference.Z > options.ColorChangeThreshold || difference.W > options.ColorChangeThreshold;
    }

    /// <summary>Stores one provider result and the context in which it was sampled.</summary>
    private sealed class Sample
    {
        internal bool Initialized;
        internal long UpdatedAt;
        internal long NextSampleAt;
        internal long LastMeaningfulChangeAt;
        internal bool Active;
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
