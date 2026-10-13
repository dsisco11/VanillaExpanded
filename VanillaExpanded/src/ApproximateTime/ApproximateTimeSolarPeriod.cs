using System;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Chooses artwork from estimated solar elevation and the native seasonal sun path.</summary>
internal static class ApproximateTimeSolarPeriod
{
    private static readonly float NightHeight = MathF.Sin(-6 * MathF.PI / 180);
    private static readonly float EnterNightHeight = MathF.Sin(-6.5f * MathF.PI / 180);
    private static readonly float LeaveNightHeight = MathF.Sin(-5.5f * MathF.PI / 180);
    private static readonly float HorizonMargin = MathF.Sin(.5f * MathF.PI / 180);

    #region Public API
    /// <summary>Classifies night below six degrees beneath the horizon, with half-degree hysteresis for retained phases.</summary>
    public static ApproximateTimePeriod FromSunHeight(float earlier, float current, float later, ApproximateTimePeriod? previous = null)
    {
        if (!float.IsFinite(earlier) || !float.IsFinite(current) || !float.IsFinite(later))
            return ApproximateTimePeriod.Unknown;
        bool rising = later > earlier;
        // A previous phase moves the boundary in the direction of travel so tiny elevation changes do not flicker.
        bool hasPrevious = previous.HasValue && previous != ApproximateTimePeriod.Unknown;
        float nightBoundary = !hasPrevious ? NightHeight
            : previous == ApproximateTimePeriod.Night ? LeaveNightHeight : EnterNightHeight;
        if (current < nightBoundary) return ApproximateTimePeriod.Night;
        bool wasDaylight = previous is ApproximateTimePeriod.Morning or ApproximateTimePeriod.Noon or ApproximateTimePeriod.Afternoon;
        float horizonBoundary = !hasPrevious ? 0 : wasDaylight ? -HorizonMargin : HorizonMargin;
        if (current < horizonBoundary) return rising ? ApproximateTimePeriod.Dawn : ApproximateTimePeriod.Dusk;
        // Solar noon follows the daily high point, including the low winter sun.
        if (current > 0 && Math.Abs(later - earlier) <= .025f &&
            (current > .15f || current >= earlier && current >= later)) return ApproximateTimePeriod.Noon;
        return rising ? ApproximateTimePeriod.Morning : ApproximateTimePeriod.Afternoon;
    }
    #endregion
}
