using System.Collections.Generic;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Describes every independent weather channel without relying on icon colour or shape.</summary>
internal static class WeatherConditionsText
{
    #region Public API
    /// <summary>Retains every active precipitation, fog, and thunder intensity in the compact text readout.</summary>
    public static string Format(WeatherConditionsSample sample)
    {
        var parts = new List<string>();
        if (sample.Precipitation.HasValue) parts.Add($"{sample.Precipitation.Value.ToString().ToUpperInvariant()}{sample.PrecipitationIntensity}");
        if (sample.Fog) parts.Add($"FOG{sample.FogIntensity}");
        if (sample.ThunderIntensity > 0) parts.Add($"THR{sample.ThunderIntensity}");
        return string.Join(" ", parts);
    }
    #endregion
}
