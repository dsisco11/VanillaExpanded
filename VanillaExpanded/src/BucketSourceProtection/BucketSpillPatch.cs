using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.BucketSourceProtection;

/// <summary>Rejects bucket spills that downgrade existing water sources.</summary>
[HarmonyPatch(typeof(BlockLiquidContainerBase), "SpillContents")]
internal static class BucketSpillPatch
{
    #region Constants
    public const int MaxLiquidLevel = 7;
    #endregion

    #region Public API
    /// <summary>Stops before fluid writes, content consumption, and pouring effects.</summary>
    [HarmonyPrefix]
    internal static bool Prefix(BlockLiquidContainerBase __instance, ItemSlot containerSlot,
        EntityAgent byEntity, BlockSelection blockSel, ref bool __result)
    {
        var containerStack = containerSlot.Itemstack;
        if (containerStack == null || __instance is not BlockBucket ||
            !byEntity.World.Config.GetBool("noLiquidSourceTransport", false)) return true;

        var props = __instance.GetContentProps(containerStack);
        if (props == null || !props.AllowSpill || props.WhenSpilled == null ||
            props.WhenSpilled.Action != WaterTightContainableProps.EnumSpilledAction.PlaceBlock) return true;

        // Partial contents use the game's DropContents branch, which never writes a fluid block.
        float litres = __instance.GetCurrentLitres(containerStack);
        if (litres > 0 && litres < 10) return true;
        var code = props.WhenSpilled.Stack.Code;
        if (props.WhenSpilled.StackByFillLevel != null &&
            props.WhenSpilled.StackByFillLevel.TryGetValue((int)litres, out var fillStack) && fillStack != null) code = fillStack.Code;
        var spilled = byEntity.World.GetBlock(code);
        if (spilled == null || !IsWater(spilled) || spilled.LiquidLevel >= MaxLiquidLevel) return true;

        var accessor = byEntity.World.BlockAccessor;
        BlockPos target = blockSel.Position;
        // Match vanilla's two destination branches, including its position argument for displacement.
        if (accessor.GetBlock(target).DisplacesLiquids(accessor, target))
        {
            var adjacent = target.AddCopy(blockSel.Face);
            if (accessor.GetBlock(adjacent).DisplacesLiquids(accessor, target)) return true;
            target = adjacent;
        }
        var existing = accessor.GetBlock(target, BlockLayersAccess.Fluid);
        if (!IsWater(existing) || existing.LiquidLevel != MaxLiquidLevel) return true;

        __result = false;
        return false;
    }
    #endregion

    #region Private
    /// <summary>Recognizes base-game fresh and salt water through their liquid identity.</summary>
    private static bool IsWater(Block? block)
    {
        return block != null && block.IsLiquid() && block.LiquidCode is "water" or "saltwater";
    }
    #endregion
}
