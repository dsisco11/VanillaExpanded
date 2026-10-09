using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VanillaExpanded.SpawnDecal;

/// <summary>Synchronizes explicit spawn changes and engine-driven exhaustion of personal respawn uses.</summary>
[Harmony]
internal class ServerPlayerPatches
{
    #region Private
    /// <summary>Sends a newly assigned personal spawn position to its owning client.</summary>
    [HarmonyPatch(typeof(ServerPlayer), nameof(ServerPlayer.SetSpawnPosition))]
    [HarmonyPostfix]
    private static void OnSetSpawnPosition(PlayerSpawnPos pos, ref ServerPlayer __instance)
    {
        if (pos is null) return;
        SpawnDecalServerSystem.OnSpawnPositionSet(__instance, pos);
    }

    /// <summary>Sends an explicit personal spawn removal to its owning client.</summary>
    [HarmonyPatch(typeof(ServerPlayer), nameof(ServerPlayer.ClearSpawnPosition))]
    [HarmonyPostfix]
    private static void OnClearSpawnPosition(ref ServerPlayer __instance)
    {
        SpawnDecalServerSystem.OnSpawnPositionCleared(__instance);
    }

    /// <summary>Records whether a consuming lookup begins with an owned personal spawn point.</summary>
    [HarmonyPatch(typeof(ServerPlayer), nameof(ServerPlayer.GetSpawnPosition))]
    [HarmonyPrefix]
    private static void BeforeGetSpawnPosition(bool consumeSpawnUse, ServerPlayer __instance, out bool __state)
    {
        // Reading the default spawn does not consume uses; only watch the personal point owned by this player.
        __state = consumeSpawnUse && (__instance.WorldData as ServerWorldPlayerData)?.SpawnPosition is not null;
    }

    /// <summary>Notifies the client when consuming the last use removes the stored personal spawn point.</summary>
    [HarmonyPatch(typeof(ServerPlayer), nameof(ServerPlayer.GetSpawnPosition))]
    [HarmonyPostfix]
    private static void AfterGetSpawnPosition(ServerPlayer __instance, bool __state)
    {
        // The engine clears this field directly and still returns the last consumed point for this respawn.
        // Observe the ownership transition rather than the returned position, which can also be a role/global fallback.
        if (__state && (__instance.WorldData as ServerWorldPlayerData)?.SpawnPosition is null)
            SpawnDecalServerSystem.OnSpawnPositionCleared(__instance);
    }
    #endregion
}
