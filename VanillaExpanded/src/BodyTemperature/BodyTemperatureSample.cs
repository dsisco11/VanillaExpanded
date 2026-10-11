using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VanillaExpanded.BodyTemperature;

/// <summary>A synchronized raw simulation reading, native normal temperature, and visual warning band.</summary>
internal sealed record BodyTemperatureSample(float Celsius, float Normal, BodyTemperatureRisk Risk, float FreezingStrength = 0, int Trend = 0)
{
    public bool Dangerous => Risk is BodyTemperatureRisk.Freezing or BodyTemperatureRisk.Overheating;
    /// <summary>Matches the character panel's compression of simulation temperatures above 37°C before rounding.</summary>
    public int DisplayCelsius => (int)MathF.Round(ToCelsius(Celsius), MidpointRounding.AwayFromZero);
    /// <summary>Maps the converted unrounded reading onto the thermometer range.</summary>
    public float Fill => Math.Clamp((ToCelsius(Celsius) - (ToCelsius(Normal) - 6)) / 14, 0, 1);
    #region Public API
    /// <summary>Converts the native warmth buffer to the character panel's unrounded Celsius reading.</summary>
    internal static float ToCelsius(float raw) => raw > 37 ? 37 + (raw - 37) / 10 : raw;
    /// <summary>Reads the native synchronized data, using native freezing intensity and retaining the hot warning within its recovery margin.</summary>
    public static BodyTemperatureSample? Read(Entity entity, BodyTemperatureSample? previous = null)
    {
        var behavior = entity.GetBehavior<EntityBehaviorBodyTemperature>();
        var tree = entity.WatchedAttributes.GetTreeAttribute("bodyTemp");
        if (behavior == null || tree == null || !tree.HasAttribute("bodytemp")) return null;
        return FromTemperature(tree.GetFloat("bodytemp"), behavior.NormalBodyTemperature,
            entity.WatchedAttributes.GetFloat("freezingEffectStrength"), previous);
    }
    /// <summary>Uses synchronized freezing intensity for cold warnings and visual temperature policy for hot warnings.</summary>
    public static BodyTemperatureSample? FromTemperature(float current, float normal, float freezingStrength,
        BodyTemperatureSample? previous = null)
    {
        if (!float.IsFinite(current) || !float.IsFinite(normal)) return null;
        float strength = float.IsFinite(freezingStrength) ? Math.Clamp(freezingStrength, 0, 1) : 0;
        // The server already accounts for temperature simulation and publishes the active freezing effect.
        // Do not infer cold from body temperature or retain it after the native effect has cleared.
        if (strength > 0)
            return new(current, normal, strength > .5f ? BodyTemperatureRisk.Freezing : BodyTemperatureRisk.Cold, strength);
        float difference = ToCelsius(current) - ToCelsius(normal);
        // Raw warmth is a simulation buffer, not overheating: vanilla routinely reaches raw 45.
        // Apply visual hot policy only after the same conversion used by the character panel.
        bool wasHot = previous?.Normal == normal && previous.Risk is BodyTemperatureRisk.Hot or BodyTemperatureRisk.Overheating;
        if (difference > 5 || wasHot && difference > 4.75f)
            return new(current, normal, difference > 7 ? BodyTemperatureRisk.Overheating : BodyTemperatureRisk.Hot);
        return null;
    }
    #endregion

}
