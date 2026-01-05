using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.IgnitionTools;

/// <summary>
/// Harmony patch for <see cref="ItemCandle"/> to prevent candle placement when targeting an ignitable block.
/// This allows the <see cref="CollectibleBehaviorIgnitionTool"/> behavior to handle the interaction instead.
/// </summary>
[HarmonyPatch]
internal static class ItemCandlePatch
{
    /// <summary>
    /// Prefix patch that skips <see cref="ItemCandle.OnHeldInteractStart"/> when targeting an ignitable block.
    /// </summary>
    /// <returns>False to skip the original method when targeting an ignitable block, true otherwise.</returns>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemCandle), nameof(ItemCandle.OnHeldInteractStart))]
    public static bool OnHeldInteractStart_Prefix(
        EntityAgent byEntity,
        BlockSelection blockSel)
    {
        // Only intercept when ignition tools feature is enabled
        if (!VanillaExpandedModSystem.Config.EnableIgnitionTools)
        {
            return true; // run original method
        }

        // Only intercept Shift+RightClick (which is what ItemCandle uses for placement)
        if (blockSel is null || byEntity?.World is null || !byEntity.Controls.ShiftKey)
        {
            return true; // run original method
        }

        // Check if targeted block is ignitable
        Block targetedBlock = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        if (targetedBlock is not IIgnitable ignitable)
        {
            return true; // run original method
        }

        // Check if the block can actually be ignited
        EnumIgniteState state = ignitable.OnTryIgniteBlock(byEntity, blockSel.Position, 0f);
        if (state is not (EnumIgniteState.Ignitable or EnumIgniteState.IgniteNow))
        {
            return true; // run original method
        }

        // Block is ignitable - skip ItemCandle's placement logic
        // The CollectibleBehaviorIgnitionTool will handle the ignition via base.OnHeldInteractStart
        return false;
    }
}
