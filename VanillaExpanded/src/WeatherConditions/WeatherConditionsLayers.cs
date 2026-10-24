using System;
using System.Collections.Generic;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Defines the semantic layers of one composite independently of the artwork's visual style.</summary>
internal static class WeatherConditionsLayers
{
    /// <summary>Lists full-canvas transparent textures, each authored at 64 by 64 pixels.</summary>
    public static readonly string[] Names =
    [
        "sky-clear", "sky-mostly-clear", "sky-partly-cloudy", "sky-mostly-cloudy", "sky-overcast", "sky-unknown",
        "rain-light", "rain-moderate", "rain-heavy", "snow-light", "snow-moderate", "snow-heavy",
        "hail-light", "hail-moderate", "hail-heavy",
        "fog-light", "fog-moderate", "fog-dense", "thunder-light", "thunder-moderate", "thunder-heavy"
    ];

    #region Public API
    /// <summary>Selects independently varying channels in stable back-to-front order during preparation.</summary>
    public static int[] Select(WeatherConditionsSample sample)
    {
        var layers = new List<int>(4) { sample.CloudCoverage is int coverage ? Math.Clamp(coverage, 0, 4) : 5 };
        // Shared full-canvas origins let approved artwork reserve space without runtime repositioning.
        if (sample.Precipitation is EnumPrecipitationType type)
        {
            int start = type switch { EnumPrecipitationType.Snow => 9, EnumPrecipitationType.Hail => 12, _ => 6 };
            layers.Add(start + Math.Clamp(sample.PrecipitationIntensity, 1, 3) - 1);
        }
        if (sample.Fog) layers.Add(15 + Math.Clamp(sample.FogIntensity, 1, 3) - 1);
        if (sample.ThunderIntensity > 0) layers.Add(18 + Math.Clamp(sample.ThunderIntensity, 1, 3) - 1);
        return layers.ToArray();
    }
    #endregion
}
