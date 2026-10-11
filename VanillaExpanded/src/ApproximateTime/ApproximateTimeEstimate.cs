using System;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Tracks a personal clock calibrated by sunlight, accumulating bounded error while unobserved.</summary>
internal sealed class ApproximateTimeEstimate
{
    private double? lastHours;
    private double? estimatedHours;
    private double drift;
    private double dayLength;
    private double signedDrift;

    #region Public API
    /// <summary>Gets the remembered hour normalized to a 24-hour day, or no value before calibration.</summary>
    public double? Hour => estimatedHours.HasValue
        ? ((estimatedHours.Value % dayLength + dayLength) % dayLength) / dayLength * 24 : null;

    /// <summary>Gets the personal clock offset in calendar days for the native seasonal solar calculation.</summary>
    public double? EstimatedDayOffset => estimatedHours.HasValue ? signedDrift / dayLength : null;
    /// <summary>Calibrates in sunlight or advances an imperfect clock using elapsed game time.</summary>
    public void Observe(double totalHours, double hourOfDay, double hoursPerDay, bool sunVisible, long entityId)
    {
        if (!double.IsFinite(totalHours) || !double.IsFinite(hourOfDay) ||
            !double.IsFinite(hoursPerDay) || hoursPerDay <= 0)
        {
            Reset();
            return;
        }
        double elapsed = lastHours.HasValue ? totalHours - lastHours.Value : 0;
        // Calendar reversals, large time commands, and day-length changes invalidate calibration.
        if (elapsed < 0 || elapsed > hoursPerDay || (dayLength != 0 && dayLength != hoursPerDay)) Reset();
        dayLength = hoursPerDay;
        lastHours = totalHours;
        // Seed a personal estimate at entry or after a calendar discontinuity, even under cover.
        // Only subsequent sunlight observations recalibrate it; otherwise uncertainty accumulates.
        if (sunVisible || !estimatedHours.HasValue)
        {
            estimatedHours = hourOfDay;
            drift = 0;
            signedDrift = 0;
        }
        else if (estimatedHours.HasValue)
        {
            // Six minutes of error per game hour, capped at one third of a day.
            // Character identity determines whether the clock runs fast or slow.
            double nextDrift = Math.Min(dayLength / 3, drift + elapsed * .1);
            double direction = (entityId & 1) == 0 ? 1 : -1;
            estimatedHours += elapsed + direction * (nextDrift - drift);
            drift = nextDrift;
            signedDrift = direction * nextDrift;
        }
    }

    /// <summary>Discards calibration on world, entity, or feature lifetime changes.</summary>
    public void Reset()
    {
        lastHours = null;
        estimatedHours = null;
        dayLength = 0;
        drift = 0;
        signedDrift = 0;
    }
    #endregion
}
