using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoLoot;

[HarmonyPatch(typeof(EntityBehaviorHarvestable), nameof(EntityBehaviorHarvestable.OnInteract))]
internal static class AutoLootInteractionPatch
{
    [HarmonyPostfix]
    private static void AfterInteract(EntityBehaviorHarvestable __instance, EntityAgent byEntity,
        Vec3d hitPosition, EnumInteractMode mode, GuiDialogCreatureContents? ___dlg)
    {
        AutoLootSystem_Client.Active?.Client?.OnInteract(__instance, ___dlg, byEntity, hitPosition, mode);
    }
}
