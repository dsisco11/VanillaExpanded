using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Evaluates solar-only light at a native sun elevation without moonlight or graphics work.</summary>
internal static class ApproximateTimeSolarIntensity
{
    #region Public API
    /// <summary>Uses the native CPU sunlight lookup and high-sun contribution, retaining a custom-calendar approximation.</summary>
    public static float Get(IGameCalendar calendar, float height)
    {
        float coordinate = (Math.Clamp(height * 1.4f + .2f, -1, 1) + 1) / 2;
        if (calendar is not Vintagestory.Common.GameCalendar native) return coordinate;
        // This is the solar-only intensity used by ClientGameCalendar.Update; combined daylight includes moonlight.
        var sunlight = native.getSunlightPixelRel(Math.Clamp(coordinate + native.SunsetMod, 0, 1), .01f);
        return (sunlight.R + sunlight.G + sunlight.B) / (3f * 255) + Math.Max(0, (height - .4f) / 7.5f);
    }
    #endregion
}
