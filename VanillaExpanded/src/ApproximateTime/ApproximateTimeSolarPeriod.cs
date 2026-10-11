using System;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Chooses artwork from solar intensity and the native seasonal sun path.</summary>
internal static class ApproximateTimeSolarPeriod
{
    #region Public API
    /// <summary>Classifies night and twilight by solar intensity, then daylight by solar direction and high point.</summary>
    public static ApproximateTimePeriod FromSunHeight(float earlier, float current, float later, float solarIntensity)
    {
        if (!float.IsFinite(earlier) || !float.IsFinite(current) || !float.IsFinite(later) || !float.IsFinite(solarIntensity))
            return ApproximateTimePeriod.Unknown;
        bool rising = later > earlier;
        // These presentation thresholds apply to solar light only: bright moonlight cannot create dawn.
        if (solarIntensity < .1f) return ApproximateTimePeriod.Night;
        if (solarIntensity < .65f) return rising ? ApproximateTimePeriod.Dawn : ApproximateTimePeriod.Dusk;
        // Solar noon follows the daily high point, including the low winter sun.
        if (current > 0 && Math.Abs(later - earlier) <= .025f &&
            (current > .15f || current >= earlier && current >= later)) return ApproximateTimePeriod.Noon;
        return rising ? ApproximateTimePeriod.Morning : ApproximateTimePeriod.Afternoon;
    }
    #endregion
}
