using System;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Derives bounded personal-clock error from the server's persisted sunlight-observation age.</summary>
internal static class ApproximateTimeEstimate
{
    #region Public API
    /// <summary>Converts elapsed unobserved game hours into a signed calendar-day offset.</summary>
    public static double? GetDayOffset(double timeSinceSunObserved, double hoursPerDay, string playerUid)
    {
        if (!double.IsFinite(timeSinceSunObserved) || timeSinceSunObserved < 0 ||
            !double.IsFinite(hoursPerDay) || hoursPerDay <= 0) return null;
        // Stable ordinal hashing avoids process-random string hashes and entity-ID changes on reload.
        uint hash = 2166136261;
        foreach (char character in playerUid) hash = unchecked((hash ^ character) * 16777619);
        double direction = (hash & 1) == 0 ? 1 : -1;
        return direction * Math.Min(hoursPerDay / 3, timeSinceSunObserved * .1) / hoursPerDay;
    }
    #endregion
}
