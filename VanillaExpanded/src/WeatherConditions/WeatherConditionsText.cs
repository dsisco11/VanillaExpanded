using System.Collections.Generic;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Describes every independent weather channel without relying on icon colour or shape.</summary>
internal static class WeatherConditionsText
{
    #region Public API
    /// <summary>Retains cloud coverage and every active weather intensity in the compact text readout.</summary>
    public static string Format(WeatherConditionsSample sample)
    {
        var parts = new List<string>();
        // Zero cloud cover is meaningful clear-sky data; absent channels otherwise need no token.
        parts.Add(sample.CloudCoverage is int cloud ? $"CLD{cloud}" : "CLD?");
        if (sample.Precipitation.HasValue) parts.Add($"{sample.Precipitation.Value.ToString().ToUpperInvariant()}{sample.PrecipitationIntensity}");
        if (sample.WindIntensity > 0) parts.Add($"WND{sample.WindIntensity}");
        if (sample.Fog) parts.Add($"FOG{sample.FogIntensity}");
        if (sample.ThunderIntensity > 0) parts.Add($"THR{sample.ThunderIntensity}");
        return string.Join(" ", parts);
    }
    #endregion
}
