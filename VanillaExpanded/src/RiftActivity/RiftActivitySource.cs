using HarmonyLib;
using Vintagestory.GameContent;

namespace VanillaExpanded.RiftActivity;

/// <summary>Reads the native client packet state without invoking the server-only pattern dictionary.</summary>
internal static class RiftActivitySource
{
    private static readonly AccessTools.FieldRef<ModSystemRiftWeather, CurrentPattern> Pattern =
        AccessTools.FieldRefAccess<ModSystemRiftWeather, CurrentPattern>("curPattern");

    #region Public API
    /// <summary>Returns no activity before synchronization, during calm weather, or for unsupported categories.</summary>
    public static RiftActivityLevel? Read(ModSystemRiftWeather? weather)
    {
        if (weather == null || !weather.Enabled) return null;
        // CurrentPattern dereferences a dictionary that vanilla populates only on the server.
        return Pattern(weather)?.Code switch
        {
            "low" => RiftActivityLevel.Low,
            "medium" => RiftActivityLevel.Medium,
            "high" or "veryhigh" => RiftActivityLevel.High,
            "apocalyptic" => RiftActivityLevel.Apocalyptic,
            _ => null
        };
    }
    #endregion
}
