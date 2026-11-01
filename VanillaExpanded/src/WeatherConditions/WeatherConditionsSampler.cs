using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Reads weather-owned inputs without altering native simulation.</summary>
internal static class WeatherConditionsSampler
{
    #region Public API
    /// <summary>Copies local weather, retaining transition history supplied by the overlay's entity lifetime.</summary>
    public static WeatherConditionsSample Sample(ICoreClientAPI api,
        WeatherDataSnapshot snapshot, ClimateCondition climate, WeatherConditionsSample? previous)
    {
        var entity = api.World.Player.Entity;
        var ambient = snapshot.Ambient;
        float fog = Weighted(ambient?.FogDensity);
        float mist = LocalMistOpacity(Weighted(ambient?.FlatFogDensity),
            api.World.SeaLevel + Weighted(ambient?.FlatFogYPos), entity.CameraPos.Y);
        // Thunder follows the same temperature gate and rain-cloud multiplier as native lightning.
        float thunder = ThunderRate(climate.Temperature, snapshot.lightningMinTemp, climate.RainCloudOverlay,
            snapshot.nearLightningRate, snapshot.distantLightningRate);
        return WeatherConditionsSample.FromWeather(climate.Rainfall, snapshot.BlendedPrecType,
            climate.Temperature, snapshot.snowThresholdTemp, fog, previous,
            thunder, mist);
    }

    /// <summary>Combines eligible native thunder rates without interpreting rain or wind as a storm.</summary>
    public static float ThunderRate(float temperature, float minimumTemperature, float rainCloudOverlay,
        float nearRate, float distantRate)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(minimumTemperature) || temperature < minimumTemperature
            || !float.IsFinite(rainCloudOverlay)) return 0;
        float near = float.IsFinite(nearRate) ? Math.Max(0, nearRate) : 0;
        float distant = float.IsFinite(distantRate) ? Math.Max(0, distantRate) : 0;
        return Math.Clamp((near + distant) * Math.Clamp(rainCloudOverlay, 0, 1), 0, 1);
    }

    /// <summary>Evaluates weather mist at eye height using the native signed flat-fog expression.</summary>
    public static float LocalMistOpacity(float density, double layerY, double eyeY)
    {
        if (!float.IsFinite(density) || !double.IsFinite(layerY) || !double.IsFinite(eyeY)) return 0;
        return (float)Math.Clamp(1 - Math.Exp(-(eyeY - layerY) * density), 0, 1);
    }

    #endregion

    #region Private
    /// <summary>Applies a finite native ambient weight without admitting unrelated scene modifiers.</summary>
    private static float Weighted(WeightedFloat? value)
    {
        if (value == null || !float.IsFinite(value.Value) || !float.IsFinite(value.Weight)) return 0;
        return value.Value * Math.Clamp(value.Weight, 0, 1);
    }
    #endregion
}
