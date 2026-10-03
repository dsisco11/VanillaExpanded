using System;
using System.Runtime.CompilerServices;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;

namespace VanillaExpanded.PreparationIndicators;

/// <summary>Shows increasing progress for items with drying or curing transitions.</summary>
internal sealed class PreparationIndicatorProvider : IItemSlotIndicatorProvider
{
    private readonly ConditionalWeakTable<ItemStack, ProgressSample> samples = new();

    #region Public API
    /// <summary>Samples the game's transition progress using the item's actual inventory context.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        ItemStack? stack = slot.Itemstack;
        ICoreAPI? api = slot.Inventory?.Api;
        if (stack is null || api is null) return false;

        ProgressSample sample = samples.GetOrCreateValue(stack);
        long now = Environment.TickCount64;
        // Re-sample moved or transformed stacks immediately; otherwise avoid advancing transitions every frame.
        if (sample.UpdatedAt < 0 || now - sample.UpdatedAt >= 1_000
            || !ReferenceEquals(sample.Inventory, slot.Inventory)
            || !ReferenceEquals(sample.Collectible, stack.Collectible))
        {
            sample.HasProgress = false;
            CollectibleObject sampledCollectible = stack.Collectible;
            TransitionState[]? states = sampledCollectible.UpdateAndGetTransitionStates(api.World, slot);
            // A completed transition may replace the item; never attach its old progress to the result.
            if (states is not null && ReferenceEquals(slot.Itemstack, stack)
                && ReferenceEquals(stack.Collectible, sampledCollectible))
            {
                foreach (TransitionState? state in states)
                {
                    if (state?.Props?.Type is not (EnumTransitionType.Dry or EnumTransitionType.Cure)
                        || !float.IsFinite(state.TransitionLevel)) continue;

                    sample.Progress = Math.Clamp(state.TransitionLevel, 0, 1);
                    sample.HasProgress = true;
                    break;
                }
            }

            sample.Inventory = slot.Inventory;
            sample.Collectible = sampledCollectible;
            sample.UpdatedAt = now;
        }

        if (!sample.HasProgress) return false;
        // Slate-to-teal conveys preparation rather than the red/yellow warning palette used for depletion.
        var color = ColorUtilEx.MultiLerp(IndicatorColorPallette.PreparationColors.AsSpan(), sample.Progress, smooth: true);
        indicator = new ItemSlotIndicator(sample.Progress, IndicatorColorPallette.WithOpacity(color, 0.5f));
        return true;
    }
    #endregion

    #region Private
    /// <summary>Keeps a short-lived progress sample tied to its owning inventory and collectible.</summary>
    private sealed class ProgressSample
    {
        internal long UpdatedAt = -1;
        internal InventoryBase? Inventory;
        internal CollectibleObject? Collectible;
        internal bool HasProgress;
        internal float Progress;
    }
    #endregion
}
