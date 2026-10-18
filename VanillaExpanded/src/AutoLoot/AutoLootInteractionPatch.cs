using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoLoot;

[HarmonyPatch(typeof(EntityBehaviorHarvestable))]
internal static class AutoLootInteractionPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(EntityBehaviorHarvestable.OnInteract))]
    private static void AfterInteract(EntityBehaviorHarvestable __instance, EntityAgent byEntity,
        Vec3d hitPosition, EnumInteractMode mode, GuiDialogCreatureContents? ___dlg)
    {
        AutoLootSystem_Client.Active?.Client?.OnInteract(__instance, ___dlg, byEntity, hitPosition, mode);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(EntityBehaviorHarvestable.GetInteractionHelp))]
    internal static void AppendInteractionHelp(EntityBehaviorHarvestable __instance,
        IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref WorldInteraction[]? __result)
    {
        if (!VanillaExpandedModSystem.Config.EnableAutoLoot || world.Side != EnumAppSide.Client
            || !AutoLootTarget.IsSupportedCorpse(__instance)
            || __instance.Inventory.FirstNonEmptySlot is null
            || !ReferenceEquals(es.Entity, __instance.entity)
            || !ReferenceEquals(world.GetEntityById(__instance.entity.EntityId), __instance.entity)
            || player.Entity.Pos.Dimension != __instance.entity.Pos.Dimension
            || es.HitPosition is not Vec3d hitPosition
            || !AutoLootTarget.IsInReach(player.Entity, __instance.entity, hitPosition))
        {
            return;
        }

        __result = [.. __result ?? [], new WorldInteraction
        {
            ActionLangCode = "vanillaexpanded:entityhelp-autoloot",
            MouseButton = EnumMouseButton.Right
        }];
    }
}
