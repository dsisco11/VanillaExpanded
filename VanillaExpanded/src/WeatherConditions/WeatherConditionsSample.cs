using System;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Copies active precipitation and weather haze without retaining mutable native snapshots.</summary>
internal sealed record WeatherConditionsSample(EnumPrecipitationType? Precipitation, bool Fog)
{
    #region Public API
    /// <summary>Uses native automatic snow selection and separate entry/exit thresholds for blended conditions.</summary>
    public static WeatherConditionsSample FromWeather(float rainfall, EnumPrecipitationType type, float temperature,
        float snowThreshold, float fogDensity, WeatherConditionsSample? previous = null)
    {
        bool wet = float.IsFinite(rainfall) && rainfall > (previous?.Precipitation != null ? .005f : .01f);
        // Native weather particles resolve Auto from climate temperature and this snapshot's snow threshold.
        if (type == EnumPrecipitationType.Auto && float.IsFinite(temperature) && float.IsFinite(snowThreshold))
            type = temperature < snowThreshold ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
        EnumPrecipitationType? precipitation = wet && type is EnumPrecipitationType.Rain or EnumPrecipitationType.Snow or EnumPrecipitationType.Hail
            ? type : null;
        // Clear weather includes ordinary distance haze (~.001); report stronger weather fog only.
        bool fog = float.IsFinite(fogDensity) && fogDensity > (previous?.Fog == true ? .002f : .0025f);
        return new(precipitation, fog);
    }
    #endregion
}
