using System;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Stores the daily solar distribution as 24 independently tinted hourly segments.</summary>
internal sealed class ApproximateTimeDayPhases : IEquatable<ApproximateTimeDayPhases>
{
    private const int SamplesPerHour = 12;
    private readonly double[] colors;

    #region Public API
    /// <summary>Gets the untinted fallback used before a valid solar distribution is available.</summary>
    public static ApproximateTimeDayPhases Neutral { get; } = new(new double[72]);

    /// <summary>Samples the native solar path at five-minute intervals and blends phase coverage within each hour.</summary>
    public static ApproximateTimeDayPhases? Sample(double totalDays, Func<double, float> sunHeight)
    {
        if (!double.IsFinite(totalDays)) return null;
        var colors = new double[72];
        double day = Math.Floor(totalDays);
        for (int hour = 0; hour < 24; hour++)
        for (int sample = 0; sample < SamplesPerHour; sample++)
        {
            // Each hour is centered on its dial position, including noon at the top.
            double instant = day + (hour - .5 + (sample + .5) / SamplesPerHour) / 24;
            float earlier = sunHeight(instant - 1d / 1440), current = sunHeight(instant), later = sunHeight(instant + 1d / 1440);
            if (!float.IsFinite(earlier) || !float.IsFinite(current) || !float.IsFinite(later)) return null;
            var phase = ApproximateTimeSolarPeriod.FromSunHeight(earlier, current, later);
            var rgb = phase switch
            {
                ApproximateTimePeriod.Night => (52d, 69d, 83d),
                ApproximateTimePeriod.Dawn => (208d, 155d, 115d),
                ApproximateTimePeriod.Dusk => (135d, 101d, 81d),
                _ => (197d, 164d, 75d)
            };
            colors[hour * 3] += rgb.Item1 / (255 * SamplesPerHour);
            colors[hour * 3 + 1] += rgb.Item2 / (255 * SamplesPerHour);
            colors[hour * 3 + 2] += rgb.Item3 / (255 * SamplesPerHour);
        }
        return new(colors);
    }

    /// <summary>Returns one normalized RGB channel for a fixed hour position.</summary>
    public double Channel(int hour, int channel) => colors[hour * 3 + channel];

    /// <summary>Compares display colors so unchanged solar coverage reuses its texture.</summary>
    public bool Equals(ApproximateTimeDayPhases? other) => other != null && colors.AsSpan().SequenceEqual(other.colors);
    /// <summary>Compares daily phase content by value.</summary>
    public override bool Equals(object? other) => other is ApproximateTimeDayPhases phases && Equals(phases);
    /// <summary>Hashes the complete distribution consistently with value equality.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (double value in colors) hash.Add(value);
        return hash.ToHashCode();
    }

    #endregion
    #region Private
    /// <summary>Constructs a complete distribution with privately owned color data.</summary>
    private ApproximateTimeDayPhases(double[] colors) => this.colors = colors;
    #endregion
}
