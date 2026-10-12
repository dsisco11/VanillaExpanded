using System;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Maintains a continuous opacity pulse whose rate increases through the imminent warning window.</summary>
internal sealed class TemporalStormPulse
{
    private double phase;
    private double frequency = 0.5;

    #region Public API
    /// <summary>Maps time remaining to a pulse rate from one cycle per two seconds to three cycles per second.</summary>
    public void SetRemainingDays(double remainingDays)
    {
        double progress = double.IsFinite(remainingDays)
            ? Math.Clamp(1 - remainingDays / TemporalStormSource.ImminentWindowDays, 0, 1) : 0;
        frequency = 0.5 + 2.5 * progress;
    }

    /// <summary>Advances in real frame time and returns opacity between 35 percent and fully opaque.</summary>
    public float Advance(float deltaTime)
    {
        // Keep phase continuous when sampling changes the rate; calendar acceleration affects
        // urgency, while real frame time keeps the visual animation smooth.
        if (float.IsFinite(deltaTime) && deltaTime > 0) phase = (phase + deltaTime * frequency) % 1;
        return (float)(0.35 + 0.65 * (0.5 + 0.5 * Math.Cos(phase * Math.Tau)));
    }

    /// <summary>Starts a fresh warning fully opaque at the slowest pulse rate.</summary>
    public void Reset()
    {
        phase = 0;
        frequency = 0.5;
    }
    #endregion
}
