using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Reads weather-owned inputs without altering native simulation or cloud caches.</summary>
internal static class WeatherConditionsSampler
{
    #region Public API
    /// <summary>Copies local weather, retaining transition history supplied by the overlay's entity lifetime.</summary>
    public static WeatherConditionsSample Sample(ICoreClientAPI api, WeatherSystemClient weather,
        WeatherDataSnapshot snapshot, ClimateCondition climate, WeatherConditionsSample? previous)
    {
        var entity = api.World.Player.Entity;
        var ambient = snapshot.Ambient;
        var wind = api.World.BlockAccessor.GetWindSpeedAt(entity.Pos.XYZ);
        float speed = wind == null ? 0 : (float)wind.Length();
        float fog = Weighted(ambient?.FogDensity);
        float mist = LocalMistOpacity(Weighted(ambient?.FlatFogDensity),
            api.World.SeaLevel + Weighted(ambient?.FlatFogYPos), entity.CameraPos.Y);
        // Thunder follows the same temperature gate and rain-cloud multiplier as native lightning.
        float thunder = ThunderRate(climate.Temperature, snapshot.lightningMinTemp, climate.RainCloudOverlay,
            snapshot.nearLightningRate, snapshot.distantLightningRate);
        return WeatherConditionsSample.FromWeather(climate.Rainfall, snapshot.BlendedPrecType,
            climate.Temperature, snapshot.snowThresholdTemp, fog, previous,
            EstimateCloudCoverage(weather, entity.Pos.XYZ), speed, thunder, mist);
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

    /// <summary>Estimates local cloud coverage from existing tiles; unavailable caches never imply clear skies.</summary>
    public static float EstimateCloudCoverage(WeatherSystemClient weather, Vec3d position)
    {
        var reader = weather.WeatherDataAtPlayer;
        if (reader == null || weather.CloudTileSize <= 0 || weather.rainOverlayPattern?.State == null) return float.NaN;
        foreach (var region in reader.AdjacentSims)
        {
            // The renderer owns cache population. Reading during startup or reload must not populate it ourselves.
            if (region == null || region.NewWePattern?.State == null || region.OldWePattern?.State == null
                || (!region.IsDummy && (region.NewWePattern.CloudDensityNoiseCache == null
                    || region.OldWePattern.CloudDensityNoiseCache == null))) return float.NaN;
        }
        int tileX = (int)position.X / weather.CloudTileSize, tileZ = (int)position.Z / weather.CloudTileSize;
        // The reader's getters depend on a private rain-overlay binding populated by its first render update.
        // HUD refresh can run earlier. Blend the validated public regional data without advancing the engine.
        var regions = reader.AdjacentSims;
        double opacity = BlendCloudValue(reader, regions[0].GetBlendedCloudOpaqueness(),
            regions[1].GetBlendedCloudOpaqueness(), regions[2].GetBlendedCloudOpaqueness(),
            regions[3].GetBlendedCloudOpaqueness(), weather.rainOverlayPattern.State.nowbaseOpaqueness);
        if (!double.IsFinite(opacity)) return float.NaN;
        double coverage = 0;
        // Cloud shader top-face alpha is opacity * min(1, 10 * thickness). Average a bounded neighborhood,
        // rather than mislabeling vertical thickness as coverage or depending on the user's view distance.
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
            {
                double thickness = BlendCloudValue(reader,
                    regions[0].GetBlendedCloudThicknessAt(tileX + x, tileZ + z),
                    regions[1].GetBlendedCloudThicknessAt(tileX + x, tileZ + z),
                    regions[2].GetBlendedCloudThicknessAt(tileX + x, tileZ + z),
                    regions[3].GetBlendedCloudThicknessAt(tileX + x, tileZ + z),
                    weather.rainOverlayPattern.State.nowbaseThickness);
                if (!double.IsFinite(thickness)) return float.NaN;
                coverage += Math.Clamp(opacity * Math.Clamp(10 * thickness, 0, 1), 0, 1);
            }
        return (float)(coverage / 25);
    }
    #endregion

    #region Private
    /// <summary>Matches native regional and rain-overlay interpolation using only initialized public inputs.</summary>
    private static double BlendCloudValue(WeatherDataReaderPreLoad reader, double first, double second,
        double third, double fourth, double rainOverlay)
    {
        double regional = GameMath.BiLerp(first, second, third, fourth, reader.LerpLeftRight, reader.LerpTopBot);
        return GameMath.Lerp(regional, rainOverlay, reader.lerpRainCloudOverlay);
    }

    /// <summary>Applies a finite native ambient weight without admitting unrelated scene modifiers.</summary>
    private static float Weighted(WeightedFloat? value)
    {
        if (value == null || !float.IsFinite(value.Value) || !float.IsFinite(value.Weight)) return 0;
        return value.Value * Math.Clamp(value.Weight, 0, 1);
    }
    #endregion
}
