using System;
using System.Numerics;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.PerishableItemSlots;

/// <summary>Supplies freshness indicators for perishable items and food-container contents.</summary>
internal sealed class FreshnessIndicatorProvider : IItemSlotIndicatorProvider
{
    private const float StaleOpacityMultiplier = 0.75f;
    private const float FullyFreshFreshness = 0.75f;

    #region Public API
    /// <summary>Returns no indicator for disabled freshness, empty slots, or nonperishable stacks.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators) return false;

        ItemStack? stack = slot.Itemstack;
        ICoreAPI? api = slot.Inventory?.Api;
        if (stack is null || api is null) return false;

        TransitionState? state = ResolvePerishState(api.World, slot);
        if (state is null) return false;
        float freshness = CalculateFreshness(state);
        indicator = new ItemSlotIndicator(freshness, FreshnessColor(freshness));
        return true;
    }

    /// <summary>Calculates the remaining fraction of the item's fresh and transition lifetime.</summary>
    internal static float CalculateFreshness(TransitionState state)
    {
        float totalHours = state.FreshHours + state.TransitionHours;
        if (totalHours <= 0) return 0;

        float remainingTransitionHours = Math.Max(0, state.TransitionHours - Math.Max(0, state.TransitionedHours - state.FreshHours));
        return Math.Clamp((state.FreshHoursLeft + remainingTransitionHours) / totalHours, 0, 1);
    }

    /// <summary>Transitions from green through yellow and orange to a less opaque red as freshness falls.</summary>
    internal static Vector4 FreshnessColor(float freshness)
    {
        float amount = Math.Clamp(freshness / FullyFreshFreshness, 0, 1);
        Vector4 color = ColorUtilEx.MultiLerp(IndicatorColorPallette.FreshnessColors.AsSpan(), amount, smooth: true);
        float freshOpacity = Math.Clamp(VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity, 0, 1);
        return IndicatorColorPallette.WithOpacity(color, freshOpacity * float.Lerp(StaleOpacityMultiplier, 1, amount));
    }
    #endregion

    #region Private
    /// <summary>Preserves direct container transition states before falling back to the first perishable meal content.</summary>
    private static TransitionState? ResolvePerishState(IWorldAccessor world, ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack!;
        TransitionState? state = stack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
        if (state is not null) return state;

        var container = stack.Collectible.GetCollectibleInterface<IBlockMealContainer>();
        ItemStack[]? contents = container?.GetNonEmptyContents(world, stack);
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
