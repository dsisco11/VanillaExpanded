using System;

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
    /// Reverse patch to call <see cref="CollectibleObject.OnHeldInteractStart"/> without virtual dispatch.
    /// </summary>
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldInteractStart))]
    public static void BaseOnHeldInteractStart(
        CollectibleObject instance,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        // Harmony replaces this with a non-virtual call to CollectibleObject.OnHeldInteractStart
        throw new NotImplementedException("Harmony reverse patch stub");
    }

    /// <summary>
    /// Prefix patch that skips <see cref="ItemCandle.OnHeldInteractStart"/> when targeting an ignitable block.
    /// Since ItemCandle doesn't call base.OnHeldInteractStart(), we must call it manually to trigger behaviors.
    /// </summary>
    /// <returns>False to skip the original method when targeting an ignitable block, true otherwise.</returns>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemCandle), nameof(ItemCandle.OnHeldInteractStart))]
    public static bool OnHeldInteractStart_Prefix(
        ItemCandle __instance,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling)
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

        // Block is ignitable - skip ItemCandle's placement logic.
        // Call base CollectibleObject.OnHeldInteractStart via reverse patch to trigger behaviors.
        BaseOnHeldInteractStart(__instance, slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);

        return false; // skip ItemCandle.OnHeldInteractStart
    }
}
