using System;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;

namespace VanillaExpanded.PreparationIndicators;

/// <summary>Shows increasing progress for items with drying or curing transitions.</summary>
internal sealed class PreparationIndicatorProvider : IItemSlotIndicatorProvider
{
    #region Public API
    /// <summary>Samples the game's transition progress using the item's actual inventory context.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        ItemStack? stack = slot.Itemstack;
        ICoreAPI? api = slot.Inventory?.Api;
        if (stack is null || api is null) return false;

        TransitionState[]? states = stack.Collectible.UpdateAndGetTransitionStates(api.World, slot);
        if (states is null) return false;
        foreach (TransitionState? state in states)
        {
            if (state?.Props?.Type is not (EnumTransitionType.Dry or EnumTransitionType.Cure)
                || !float.IsFinite(state.TransitionLevel)) continue;

            float progress = Math.Clamp(state.TransitionLevel, 0, 1);
            // Slate-to-teal distinguishes preparation from depletion warnings.
            var color = ColorUtilEx.MultiLerp(IndicatorColorPallette.PreparationColors.AsSpan(), progress, smooth: true);
            indicator = new ItemSlotIndicator(progress, IndicatorColorPallette.WithOpacity(color, 0.5f));
            return true;
        }
        return false;
    }
    #endregion
}
