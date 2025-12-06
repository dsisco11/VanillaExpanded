using HarmonyLib;

using System.Collections.Generic;

using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VanillaExpanded.src.IgnitionTools;

[HarmonyPatch]
public static class IgnitionSourcesPatch
{
    #region Harmony Patches

    private static List<ItemStack> emptyItemStackList = [];

    [HarmonyPrefix()]
    [HarmonyPatch(typeof(BlockBehaviorCanIgnite), nameof(BlockBehaviorCanIgnite.CanIgniteStacks))]
    public static bool CanIgniteStacks_Prefix(ICoreAPI api, bool withFirestarter, ref List<ItemStack> __result)
    {
        // This method can only function on the client side, so we return false on any other side to skip original method.
        if (api.Side == EnumAppSide.Client)
        {
            return true; // run original method
        }

        ObjectCacheUtil.GetOrCreate(api, "canIgniteStacks", () => emptyItemStackList);
        ObjectCacheUtil.GetOrCreate(api, "canIgniteStacksWithFirestarter", () => emptyItemStackList);
        __result = emptyItemStackList;
        return false; // skip original method
    }

    #endregion
}
