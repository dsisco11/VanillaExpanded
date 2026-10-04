using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.PerishableItemSlots;

using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.FoodContainerIndicators;

/// <summary>Supplies freshness indicators exclusively for perishable food in meal containers.</summary>
internal sealed class FoodContainerIndicatorProvider : IItemSlotIndicatorProvider
{
    private readonly FoodContainerParticlePalette palettes = new();
    #region Public API
    /// <summary>Returns meal freshness using the existing setting, color scale, and inventory transition rates.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators) return false;
        if (slot.Itemstack is not ItemStack stack || slot.Inventory?.Api is not ICoreAPI api) return false;
        if (!FoodContainerClassification.IsFoodContainer(stack.Collectible)) return false;
        if (stack.Collectible.GetCollectibleInterface<IBlockMealContainer>() is not IBlockMealContainer container) return false;

        TransitionState? state = ResolvePerishState(api.World, slot, container);
        if (state is null) return false;
        float freshness = FreshnessIndicatorProvider.CalculateFreshness(state);
        indicator = new ItemSlotIndicator(freshness, FreshnessIndicatorProvider.FreshnessColor(freshness),
            FoodGrainIndicatorEffect.DrawRange)
        {
            ParticlePalette = api is ICoreClientAPI client ? palettes.Resolve(client, stack, container) : null
        };
        return true;
    }
    #endregion

    #region Private
    /// <summary>Preserves direct container transition states before falling back to the first perishable meal content.</summary>
    private static TransitionState? ResolvePerishState(IWorldAccessor world, ItemSlot slot, IBlockMealContainer container)
    {
        ItemStack stack = slot.Itemstack!;
        TransitionState? state = stack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
        if (state is not null) return state;

        ItemStack[]? contents = container.GetNonEmptyContents(world, stack);
        if (contents is null || contents.Length == 0) return null;

        // Content transitions must retain the containing inventory's spoilage multiplier.
        var dummyInventory = new DummyInventory(slot.Inventory.Api);
        dummyInventory.OnAcquireTransitionSpeed += (type, contentStack, multiplier) =>
            type == EnumTransitionType.Perish
                ? slot.Inventory.GetTransitionSpeedMul(type, contentStack)
                : 0;
        ItemSlot contentSlot = BlockCrock.GetDummySlotForFirstPerishableStack(world, contents, null, dummyInventory);
        return contentSlot.Itemstack?.Collectible.UpdateAndGetTransitionState(world, contentSlot, EnumTransitionType.Perish);
    }
    #endregion
}
