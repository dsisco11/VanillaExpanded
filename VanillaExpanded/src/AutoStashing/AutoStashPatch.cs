using System;
using System.Collections.Generic;
using System.Reflection.Emit;

using HarmonyLib;

using VanillaExpanded.AutoStashing;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.src.AutoStashing;

[HarmonyPatch]
public static class AutoStashPatch
{
    /// <summary>
    /// Find all block types which use BlockBehaviorContainer and amend our BlockBehaviorAutoStashable behavior to them
    /// </summary>
    public static void AmendContainerBehaviors(in ICoreAPI api)
    {
        foreach (Block block in api.World.Blocks)
        {
            if (block?.Code is null) continue;
            // have to add the crate behavior to top of list first, so that it ends up as the 2nd behavior (after our container behavior)
            if (block.EntityClass == "Crate" && !block.HasBehavior<BehaviorCrateEntityEventBridge>())
            {
                BehaviorCrateEntityEventBridge behavior = new(block);

                // add behavior to block-behaviors array
                block.BlockBehaviors = [behavior, .. block.BlockBehaviors];

                // add behavior to collectible-behaviors array
                block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors];
            }

            // now our container behavior goes before all other behaviors
            if (block.HasBehavior<BlockBehaviorContainer>() && !block.HasBehavior<BlockBehaviorAutoStashable>())
            {
                //api.World.Logger.Debug($"[AutoStashPatch] AmendBlockBehaviors Invoked on Block: {block.Code}");
                BlockBehaviorAutoStashable behavior = new(block);

                // add behavior to block-behaviors array
                block.BlockBehaviors = [behavior, .. block.BlockBehaviors];

                // add behavior to collectible-behaviors array
                block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors];
            }

            // Add auto-stash behavior to bloomeries (they don't use BlockBehaviorContainer)
            if (block is BlockBloomery && !block.HasBehavior<BlockBehaviorAutoStashable>())
            {
                BlockBehaviorAutoStashable behavior = new(block);

                // add behavior to block-behaviors array
                block.BlockBehaviors = [behavior, .. block.BlockBehaviors];

                // add behavior to collectible-behaviors array
                block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors];
            }
        }
    }

    /// <summary>
    /// Reverse patch to call Block.OnBlockInteractStart directly, bypassing BlockBloomery's override.
    /// </summary>
    [HarmonyReversePatch]
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockInteractStart))]
    public static bool BaseOnBlockInteractStart(Block instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Stub - Harmony replaces this with the original Block.OnBlockInteractStart implementation
        throw new NotImplementedException("Harmony reverse patch stub");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockCrate), nameof(BlockCrate.OnBlockInteractStart))]
    public static bool BlockCrate_OnBlockInteractStart(
        BlockCrate __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ref bool __result)
    {        
        // Call base.OnBlockInteractStart via reverse patch to invoke behaviors
        bool baseResult = BaseOnBlockInteractStart(__instance, world, byPlayer, blockSel);

        // If base returned true, a behavior handled it - skip the original method
        if (baseResult)
        {
            __result = true;
            return false;
        }

        // No behaviors handled it, let the original method run
        return true;
    }

    /// <summary>
    /// Prefix patch for BlockBloomery.OnBlockInteractStart.
    /// The vanilla method doesn't call base.OnBlockInteractStart(), which means behaviors are never invoked.
    /// This prefix calls the base method first and skips the original if behaviors handled the interaction.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockBloomery), nameof(BlockBloomery.OnBlockInteractStart))]
    public static bool BlockBloomery_OnBlockInteractStart_Prefix(
        BlockBloomery __instance,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ref bool __result)
    {
        // Call base.OnBlockInteractStart via reverse patch to invoke behaviors
        bool baseResult = BaseOnBlockInteractStart(__instance, world, byPlayer, blockSel);

        // If base returned true, a behavior handled it - skip the original method
        if (baseResult)
        {
            __result = true;
            return false;
        }

        // No behaviors handled it, let the original method run
        return true;
    }
}
