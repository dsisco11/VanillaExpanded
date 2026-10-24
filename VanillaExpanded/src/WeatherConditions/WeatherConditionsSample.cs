using System;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Stores independent, stable weather bands without retaining mutable engine snapshots.</summary>
internal sealed record WeatherConditionsSample(EnumPrecipitationType? Precipitation, bool Fog)
{
    /// <summary>Gets precipitation strength: zero is absent; one through three are light to heavy.</summary>
    public int PrecipitationIntensity { get; init; } = Precipitation.HasValue ? 1 : 0;
    /// <summary>Gets estimated cloud coverage: zero through four are clear to overcast; null is unavailable.</summary>
    public int? CloudCoverage { get; init; }
    /// <summary>Gets combined fog and haze strength: zero is absent; one through three are light to dense.</summary>
    public int FogIntensity { get; init; } = Fog ? 1 : 0;
    /// <summary>Gets thunder activity: zero is absent; one through three indicate increasing native event rates.</summary>
    public int ThunderIntensity { get; init; }

    #region Public API
    /// <summary>Uses native automatic snow selection and separate entry/exit thresholds for blended conditions.</summary>
    public static WeatherConditionsSample FromWeather(float rainfall, EnumPrecipitationType type, float temperature,
        float snowThreshold, float fogDensity, WeatherConditionsSample? previous = null,
        float cloudCoverage = float.NaN, float thunderRate = 0, float mistOpacity = 0)
    {
        bool wet = float.IsFinite(rainfall) && rainfall > (previous?.Precipitation != null ? .005f : .01f);
        // Native weather particles resolve Auto from climate temperature and this snapshot's snow threshold.
        if (type == EnumPrecipitationType.Auto && float.IsFinite(temperature) && float.IsFinite(snowThreshold))
            type = temperature < snowThreshold ? EnumPrecipitationType.Snow : EnumPrecipitationType.Rain;
        EnumPrecipitationType? precipitation = wet && type is EnumPrecipitationType.Rain or EnumPrecipitationType.Snow or EnumPrecipitationType.Hail
            ? type : null;
        // Clear weather includes ordinary distance haze (~.001); report stronger weather fog only.
        bool fog = float.IsFinite(fogDensity) && fogDensity > (previous?.Fog == true ? .002f : .0025f);
        int fogLevel = Math.Max(Band(fogDensity, previous?.FogIntensity ?? 0,
            [.0025f, .008f, .02f], [.002f, .006f, .016f]),
            Band(mistOpacity, previous?.FogIntensity ?? 0, [.15f, .4f, .75f], [.12f, .32f, .65f]));
        return new(precipitation, fog || fogLevel > 0)
        {
            PrecipitationIntensity = precipitation == null ? 0 : Math.Max(1, Band(rainfall,
                previous?.PrecipitationIntensity ?? 0, [.01f, .25f, .65f], [.005f, .20f, .55f])),
            CloudCoverage = float.IsFinite(cloudCoverage) && cloudCoverage >= 0
                ? Band(cloudCoverage, previous?.CloudCoverage ?? 0, [.05f, .25f, .60f, .90f], [.03f, .20f, .55f, .85f]) : null,
            FogIntensity = fogLevel,
            ThunderIntensity = Band(thunderRate, previous?.ThunderIntensity ?? 0, [.001f, .25f, .65f], [.0005f, .20f, .55f])
        };
    }
    #endregion

    #region Private
    /// <summary>Uses separate entry and exit boundaries to prevent independently blended channels from flickering.</summary>
    private static int Band(float value, int previous, ReadOnlySpan<float> entry, ReadOnlySpan<float> exit)
    {
        if (!float.IsFinite(value) || value <= 0) return 0;
        int level = 0;
        // Evaluate every boundary so a large weather change can cross several levels in one refresh.
        for (int index = 0; index < entry.Length; index++)
            if (value > (previous > index ? exit[index] : entry[index])) level = index + 1;
        return level;
    }
    #endregion
}
