using Vintagestory.GameContent;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Maps the base game's synchronized storm schedule to HUD artwork.</summary>
internal static class TemporalStormSource
{
    internal const double ImminentWindowDays = 0.02;
    #region Public API
    /// <summary>Uses native warning windows and the authoritative active flag.</summary>
    public static TemporalStormStage? Read(SystemTemporalStability? system, double totalDays)
    {
        var data = system?.StormData;
        if (data == null || !double.IsFinite(totalDays)) return null;
        // Active takes precedence, including the native waning notification near storm end.
        if (data.nowStormActive) return TemporalStormStage.Active;
        // Warning notifications do not broadcast new runtime data. Compare the synchronized
        // deadline to the local calendar, retaining approaching across the native 0.03-day gap.
        double remaining = data.nextStormTotalDays - totalDays;
        if (!double.IsFinite(remaining) || remaining > 0.35) return null;
        return remaining <= ImminentWindowDays ? TemporalStormStage.Imminent : TemporalStormStage.Approaching;
    }
    #endregion
}
