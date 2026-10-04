using System;
using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.FoodContainerIndicators;

/// <summary>Supplies serving-level particles for food vessels independently of freshness.</summary>
internal sealed class FoodContainerIndicatorProvider : IItemSlotIndicatorProvider
{
    private readonly FoodContainerParticlePalette palettes = new();
    #region Public API
    /// <summary>Maps remaining servings to vessel capacity, retaining ingredient colors and excluding empty vessels.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators) return false;
        if (!VanillaExpandedModSystem.Config.EnableFoodGrainEffect) return false;
        if (slot.Itemstack is not ItemStack stack || slot.Inventory?.Api is not ICoreAPI api) return false;
        if (!FoodContainerClassification.IsFoodContainer(stack.Collectible)) return false;
        if (stack.Collectible.GetCollectibleInterface<IBlockMealContainer>() is not IBlockMealContainer container) return false;
        float servings = container.GetQuantityServings(api.World, stack);
        float capacity = stack.Collectible.Attributes?["servingCapacity"].AsFloat(1) ?? 1;
        if (!float.IsFinite(servings) || servings <= 0 || !float.IsFinite(capacity) || capacity <= 0) return false;
        // Resource amount controls grain height; freshness only controls the independently selected background.
        float opacity = Math.Clamp(VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity, 0, 1);
        indicator = new ItemSlotIndicator(Math.Clamp(servings / capacity, 0, 1), new Vector4(1, 1, 1, opacity),
            FoodGrainIndicatorEffect.DrawRange)
        {
            ParticlePalette = api is ICoreClientAPI client ? palettes.Resolve(client, stack, container) : null
        };
        return true;
    }
    #endregion
}
