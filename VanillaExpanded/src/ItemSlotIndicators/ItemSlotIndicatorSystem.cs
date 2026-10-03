using System.Collections.Immutable;

using HarmonyLib;

using VanillaExpanded.ClothingIndicators;
using VanillaExpanded.LiquidContainerIndicators;
using VanillaExpanded.NightVisionIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.WateringCanIndicators;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns the client indicator lifecycle and selects one indicator in descending provider priority.</summary>
internal sealed class ItemSlotIndicatorSystem : ModSystem
{
    private ImmutableArray<(IItemSlotIndicatorProvider Provider, int Priority)> providers = [];
    private Harmony? harmony;

    /// <summary>Gets the initialized client system used by the shared GUI render hook.</summary>
    internal static ItemSlotIndicatorSystem? Active { get; private set; }

    #region Public API
    #region Lifecycle
    /// <summary>Loads indicators only on the client, independently of any individual feature setting.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Registers built-in providers, initializes rendering, and installs the indicator hook.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        Register(new FreshnessIndicatorProvider());
        Register(new ClothingIndicatorProvider(), priority: -10);
        // Preserve freshness when applicable; otherwise show the container's liquid volume.
        Register(new LiquidContainerIndicatorProvider(), priority: -10);
        Register(new WateringCanIndicatorProvider(), priority: 10);
        Register(new NightVisionFuelIndicatorProvider(), priority: 10);
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
    /// <summary>Registers a provider; equal priorities retain registration order.</summary>
    internal void Register(IItemSlotIndicatorProvider provider, int priority = 0)
    {
        int index = 0;
        while (index < providers.Length && providers[index].Priority >= priority) index++;
        providers = providers.Insert(index, (provider, priority));
    }

    /// <summary>Returns the first applicable indicator, including indicators with zero fill.</summary>
    internal bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        foreach (var entry in providers)
        {
            if (entry.Provider.TryGetIndicator(slot, out indicator)) return true;
        }

        indicator = default;
        return false;
    }

    /// <summary>Releases provider references and their feature-owned caches.</summary>
    internal void Clear() => providers = [];
    #endregion
    #endregion
}
