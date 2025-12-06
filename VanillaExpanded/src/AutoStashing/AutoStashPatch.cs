using HarmonyLib;

using System.Collections.Generic;
using System.Reflection.Emit;

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
    public static void AmendContainerBehaviors(ICoreAPI api)
    {
        foreach (Block block in api.World.Blocks)
        {
            if (block is null || block.Code is null) continue;
            // have to add the crate behavior to top of list first, so that it ends up as the 2nd behavior (after our container behavior)
            if (block.EntityClass == "Crate")
            {
                BehaviorCrateEntityEventBridge behavior = new(block);

                // add behavior to block-behaviors array
                block.BlockBehaviors = [behavior, .. block.BlockBehaviors];

                // add behavior to collectible-behaviors array
                block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors];
            }

            // now our container behavior goes before all other behaviors
            if (block.HasBehavior<BlockBehaviorContainer>())
            {
                //api.World.Logger.Debug($"[AutoStashPatch] AmendBlockBehaviors Invoked on Block: {block.Code}");
                BlockBehaviorAutoStashable behavior = new(block);

                // add behavior to block-behaviors array
                block.BlockBehaviors = [behavior, .. block.BlockBehaviors];

                // add behavior to collectible-behaviors array
                block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors];
            }
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(BlockCrate), "OnBlockInteractStart")]
    public static IEnumerable<CodeInstruction> BlockCrate_OnBlockInteractStart(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        // Without ILGenerator, the CodeMatcher will not be able to create labels
        var codeMatcher = new CodeMatcher(instructions, generator);

        // find label0, which is the beginning of the call to "base.OnBlockInteractStart".
        // then remove all instruction prior to it.

        // find the call to the base OnBlockInteractStart method
        var methodCall = codeMatcher.MatchStartForward(
                CodeMatch.Calls(() => default(Block).OnBlockInteractStart)
            )
            .ThrowIfInvalid("Could not find call to base.OnBlockInteractStart")
            .Advance(-1); // back up to the instruction prior to the method call

        // At this point, the CodeMatcher is positioned at the first instruction of the base.OnBlockInteractStart call.
        // backup 4 instructions to include the ldarg instructions for the method parameters,
        codeMatcher.Advance(-4);

        // erase everything before this point, effectively removing the entire method body.
        codeMatcher.RemoveInstructionsInRange(0, codeMatcher.Pos);
        return codeMatcher.Instructions();
    }
}
