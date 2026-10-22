using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;

using HarmonyLib;

using VanillaExpanded.AutoStashing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.src.AutoStashing;

[HarmonyPatch]
public static class AutoStashPatch
{
    // Server interactions are concurrent; client-only stashingState cannot identify their opening gesture.
    private static readonly ConditionalWeakTable<IPlayer, BlockEntityQuern> serverQuernGestures = new();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockBehaviorContainer), nameof(BlockBehaviorContainer.OnBlockInteractStart))]
    public static bool QuernOpenInventory(BlockBehaviorContainer __instance, IWorldAccessor world,
        IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref bool __result)
    {
        if (__instance.block is not BlockQuern
            || world.Api is not ICoreClientAPI client
            || __instance.block.GetCollectibleBehavior<BlockBehaviorAutoStashable>(true)?.HasActiveGesture(world) != true)
            return true;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityQuern quern)
            return true;
        BlockSelection selection = blockSel.Clone();
        // Vanilla sends StartBlockUse after this callback; opening now sends the inventory packet first.
        client.Event.EnqueueMainThreadTask(() =>
        {
            if (ReferenceEquals(world.BlockAccessor.GetBlockEntity(selection.Position), quern))
                quern.OnPlayerRightClick(byPlayer, selection);
        }, "vanillaexpanded-quern-open");
        handling = EnumHandling.PreventSubsequent;
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockQuern), nameof(BlockQuern.OnBlockInteractStart))]
    public static void QuernStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        out BlockEntityQuern? __state)
    {
        __state = null;
        if (world.Side != EnumAppSide.Server) return;
        serverQuernGestures.Remove(byPlayer);
        if (!VanillaExpandedModSystem.Config.EnableAutoStash) return;
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityQuern quern
            || quern.Inventory.HasOpened(byPlayer)) return;
        var assessment = AutoStashService.AssessBlock(byPlayer.InventoryManager, quern, true, blockSel, byPlayer);
        if (assessment.CanStartInteraction && assessment.HasAvailableItems) __state = quern;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BlockQuern), nameof(BlockQuern.OnBlockInteractStart))]
    public static void QuernStarted(IPlayer byPlayer, bool __result, BlockEntityQuern? __state)
    {
        if (__result && __state is not null)
            serverQuernGestures.Add(byPlayer, __state);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockQuern), nameof(BlockQuern.OnBlockInteractStep))]
    public static bool QuernStep(BlockQuern __instance, float secondsUsed, IWorldAccessor world,
        IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (world.Side == EnumAppSide.Server
            && serverQuernGestures.TryGetValue(byPlayer, out var quern))
        {
            __result = VanillaExpandedModSystem.Config.EnableAutoStash
                && blockSel.SelectionBoxIndex == 0 && quern.Pos.Equals(blockSel.Position)
                && ReferenceEquals(world.BlockAccessor.GetBlockEntity(blockSel.Position), quern);
            return false;
        }
        var behavior = __instance.GetCollectibleBehavior<BlockBehaviorAutoStashable>(true);
        if (behavior?.HasActiveGesture(world) != true) return true;
        EnumHandling handling = EnumHandling.PassThrough;
        if (!VanillaExpandedModSystem.Config.EnableAutoStash)
        {
            behavior.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
            __result = false;
        }
        else __result = behavior.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref handling);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockQuern), nameof(BlockQuern.OnBlockInteractStop))]
    public static bool QuernStop(BlockQuern __instance, float secondsUsed, IWorldAccessor world,
        IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.Side == EnumAppSide.Server) serverQuernGestures.Remove(byPlayer);
        var behavior = __instance.GetCollectibleBehavior<BlockBehaviorAutoStashable>(true);
        if (behavior?.HasActiveGesture(world) != true) return true;
        EnumHandling handling = EnumHandling.PassThrough;
        behavior.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, ref handling);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockQuern), nameof(BlockQuern.OnBlockInteractCancel))]
    public static bool QuernCancel(BlockQuern __instance, float secondsUsed, IWorldAccessor world,
        IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (world.Side == EnumAppSide.Server) serverQuernGestures.Remove(byPlayer);
        var behavior = __instance.GetCollectibleBehavior<BlockBehaviorAutoStashable>(true);
        if (behavior?.HasActiveGesture(world) != true) return true;
        EnumHandling handling = EnumHandling.PassThrough;
        behavior.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
        __result = true;
        return false;
    }

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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityBehaviorAttachable), nameof(EntityBehaviorAttachable.OnInteract))]
    public static bool EntityBehaviorAttachable_OnInteract_Prefix(
        EntityBehaviorAttachable __instance,
        EntityAgent byEntity,
        Vec3d hitPosition,
        EnumInteractMode mode,
        ref EnumHandling handled)
        => EntityAttachedContainerAutoStash.HandleInteract(__instance, byEntity, mode, ref handled);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntityBehaviorAttachable), nameof(EntityBehaviorAttachable.GetInteractionHelp))]
    public static void EntityBehaviorAttachable_GetInteractionHelp_Postfix(
        EntityBehaviorAttachable __instance,
        IClientWorldAccessor world,
        EntitySelection es,
        IClientPlayer player,
        ref WorldInteraction[] __result)
        => EntityAttachedContainerAutoStash.AppendInteractionHelp(__instance, world, es, player, ref __result);

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
