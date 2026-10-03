using System;
using System.Collections.Immutable;

using HarmonyLib;

using VanillaExpanded.ClothingIndicators;
using VanillaExpanded.LiquidContainerIndicators;
using VanillaExpanded.NightVisionIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.PreparationIndicators;
using VanillaExpanded.WateringCanIndicators;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns the client indicator lifecycle and selects one indicator in descending provider priority.</summary>
internal sealed class ItemSlotIndicatorSystem : ModSystem
{
    private ImmutableArray<ItemSlotIndicatorRegistration> providers = [];
    private Harmony? harmony;

    /// <summary>Gets the initialized client system used by the shared GUI render hook.</summary>
    internal static ItemSlotIndicatorSystem? Active { get; private set; }

    /// <summary>Supplies monotonic time for refresh scheduling, replaceable for deterministic tests.</summary>
    internal Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    #region Public API
    #region Lifecycle
    /// <summary>Loads indicators only on the client, independently of any individual feature setting.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Registers built-in providers, initializes rendering, and installs the indicator hook.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        Register(new FreshnessIndicatorProvider(),
            contextKey: static () => (VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators,
                VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity));
        Register(new PreparationIndicatorProvider(), priority: -10);
        Register(new ClothingIndicatorProvider(), priority: -10);
        // Preserve freshness when applicable; otherwise show the container's liquid volume.
        var adaptiveSampling = new AdaptiveSamplingOptions();
        Register(new LiquidContainerIndicatorProvider(), priority: -10, adaptiveSampling: adaptiveSampling);
        Register(new WateringCanIndicatorProvider(), priority: 10, adaptiveSampling: adaptiveSampling);
        Register(new NightVisionFuelIndicatorProvider(), priority: 10, adaptiveSampling: adaptiveSampling);
        ItemSlotIndicatorRenderer.InitializeTexture(api);
        Active = this;
        harmony = new Harmony(Constants.ModId + ".itemslotindicators");
        new PatchClassProcessor(harmony, typeof(ItemSlotIndicatorPatch)).Patch();
    }

    /// <summary>Removes this system's hook, providers, and shared rendering resources.</summary>
    public override void Dispose()
    {
        harmony?.UnpatchAll(harmony.Id);
        harmony = null;
        Clear();
        if (Active == this)
        {
            Active = null;
            ItemSlotIndicatorRenderer.DisposeTexture();
        }
        base.Dispose();
    }
    #endregion

    #region Providers
    /// <summary>Registers a provider with one-second caching by default; zero interval stays immediate, and equal priorities retain order.</summary>
    /// <param name="provider">Calculates an indicator when its cached result is absent or expired.</param>
    /// <param name="priority">Higher priorities are queried first.</param>
    /// <param name="refreshIntervalMilliseconds">Minimum time between samples of an unchanged stack context; defaults to 1,000 milliseconds, and zero disables caching.</param>
    /// <param name="contextKey">Optional immutable configuration value compared by equality on each query to invalidate cached results.</param>
    /// <param name="adaptiveSampling">Optional fast sampling policy for changing indicators; ignored when caching is disabled.</param>
    internal void Register(IItemSlotIndicatorProvider provider, int priority = 0,
        long refreshIntervalMilliseconds = 1_000, Func<object?>? contextKey = null,
        AdaptiveSamplingOptions? adaptiveSampling = null)
    {
        var registration = new ItemSlotIndicatorRegistration(provider, priority, refreshIntervalMilliseconds, contextKey, adaptiveSampling);
        int index = 0;
        while (index < providers.Length && providers[index].Priority >= priority) index++;
        providers = providers.Insert(index, registration);
    }

    /// <summary>Returns the first applicable indicator, including indicators with zero fill.</summary>
    internal bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        long now = Clock();
        foreach (var entry in providers)
        {
            if (entry.TryGetIndicator(slot, now, out indicator)) return true;
        }

        indicator = default;
        return false;
    }

    /// <summary>Releases registrations and all system-owned provider samples.</summary>
    internal void Clear() => providers = [];
    #endregion
    #endregion
}
