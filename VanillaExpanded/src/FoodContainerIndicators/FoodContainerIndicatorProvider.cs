using System;
using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.FoodContainerIndicators;

/// <summary>Supplies serving-level indicators for portioned foods and food vessels independently of freshness.</summary>
internal sealed class FoodContainerIndicatorProvider : IItemSlotIndicatorProvider
{
    private readonly FoodContainerParticlePalette palettes = new();
    #region Public API
    /// <summary>Maps remaining servings to food or vessel capacity, retaining ingredient colors and excluding empty stacks.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators) return false;
        if (slot.Itemstack is not ItemStack stack || slot.Inventory?.Api is not ICoreAPI api) return false;
        var container = stack.Collectible.GetCollectibleInterface<IBlockMealContainer>();
        bool hasPortionData = stack.Attributes.HasAttribute("quantityServings");
        if (!hasPortionData && container is null) return false;
        float servings = hasPortionData
            ? (float)stack.Attributes.GetDecimal("quantityServings")
            : container!.GetQuantityServings(api.World, stack);
        // The game measures pies in whole-pie servings; each slice starts with a quarter serving.
        float defaultCapacity = stack.Collectible is BlockPie ? Math.Max(1, stack.Attributes.GetAsInt("pieSize")) * 0.25f : 1;
        float capacity = stack.Collectible.Attributes?["servingCapacity"].AsFloat(defaultCapacity) ?? defaultCapacity;
        if (!float.IsFinite(servings) || servings <= 0 || !float.IsFinite(capacity) || capacity <= 0) return false;
        // Resource amount controls grain height; freshness only controls the independently selected background.
        float opacity = Math.Clamp(VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity, 0, 1);
        indicator = new ItemSlotIndicator(Math.Clamp(servings / capacity, 0, 1), new Vector4(1, 1, 1, opacity),
            VanillaExpandedModSystem.Config.FoodGrainEffectEnabled ? FoodGrainIndicatorEffect.DrawRange : null)
        {
            ParticlePalette = VanillaExpandedModSystem.Config.FoodGrainEffectEnabled && api is ICoreClientAPI client ? palettes.Resolve(client, stack, container) : null
        };
        return true;
    }
    #endregion
}
