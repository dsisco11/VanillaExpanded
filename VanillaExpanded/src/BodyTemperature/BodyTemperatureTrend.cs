using System;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Measures converted temperature change between native simulation timestamps.</summary>
internal sealed class BodyTemperatureTrend
{
    private float? previous;
    private double hours;
    private long receivedAt;
    private int trend;

    #region Public API
    /// <summary>Retains direction between synchronized updates and rejects stale or discontinuous history.</summary>
    public int Update(float raw, double simulationHours, long elapsedMilliseconds)
    {
        if (!float.IsFinite(raw) || !double.IsFinite(simulationHours))
        {
            Reset();
            return 0;
        }
        float temperature = BodyTemperatureSample.ToCelsius(raw);
        if (previous == null || simulationHours < hours || elapsedMilliseconds < receivedAt || elapsedMilliseconds - receivedAt > 15000)
        {
            previous = temperature;
            hours = simulationHours;
            receivedAt = elapsedMilliseconds;
            trend = 0;
            return 0;
        }
        // Repeated HUD polls are not new simulation samples; do not erase an active trend every frame.
        if (simulationHours == hours) return trend;
        double rate = (temperature - previous.Value) / (simulationHours - hours);
        previous = temperature;
        hours = simulationHours;
        receivedAt = elapsedMilliseconds;
        trend = Math.Abs(rate) < .05 ? 0 : Math.Sign(rate) * (Math.Abs(rate) >= 1 ? 2 : 1);
        return trend;
    }

    /// <summary>Discards history when the player, session, or synchronized source changes.</summary>
    public void Reset()
    {
        previous = null;
        hours = 0;
        receivedAt = 0;
        trend = 0;
    }
    #endregion
}
