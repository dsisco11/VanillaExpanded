using System;
using System.Collections.Generic;

namespace VanillaExpanded.WindSpeed;

/// <summary>Maintains a rolling five-second mean of valid native wind samples using elapsed client time.</summary>
internal sealed class WindSpeedAverage
{
    private const long WindowMilliseconds = 5000;
    private readonly Queue<(long Timestamp, double Speed)> history = new();
    private long timestamp;
    private double current, sum;
    private bool hasCurrent;

    #region Public API
    /// <summary>Averages recent samples without zero-padding startup or counting repeated same-time refreshes twice.</summary>
    public double Update(double speed, long now)
    {
        if (!double.IsFinite(speed) || speed < 0)
        {
            Reset();
            return 0;
        }
        if (hasCurrent && now < timestamp) Reset();
        // Keep the current timestamp separate so config invalidations can replace, rather than duplicate, it.
        if (hasCurrent && now > timestamp)
        {
            history.Enqueue((timestamp, current));
            sum += current;
        }
        while (history.TryPeek(out var oldest) && now - oldest.Timestamp >= WindowMilliseconds)
            sum -= history.Dequeue().Speed;
        if (history.Count == 0) sum = 0;
        timestamp = now;
        current = speed;
        hasCurrent = true;
        return (sum + current) / (history.Count + 1);
    }

    /// <summary>Clears all samples when player ownership, eligibility, or world lifetime changes.</summary>
    public void Reset()
    {
        history.Clear();
        timestamp = 0;
        current = sum = 0;
        hasCurrent = false;
    }
    #endregion
}
