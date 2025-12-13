using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VanillaExpanded.SpawnDecal;

[Harmony]
internal class ServerPlayerPatches
{
    [HarmonyPatch(typeof(ServerPlayer), nameof(ServerPlayer.SetSpawnPosition))]
    [HarmonyPostfix]
    private static void OnSetSpawnPosition(PlayerSpawnPos pos, ref ServerPlayer __instance)
    {
        if (pos is null)
        {
            return;
        }

        SpawnDecalServerSystem.OnSpawnPositionSet(__instance, pos);
    }

    [HarmonyPatch(typeof(ServerPlayer), nameof(ServerPlayer.ClearSpawnPosition))]
    [HarmonyPostfix]
    private static void OnClearSpawnPosition(ref ServerPlayer __instance)
    {
        SpawnDecalServerSystem.OnSpawnPositionCleared(__instance);
    }
}
