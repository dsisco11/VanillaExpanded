using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Rendering;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Defines the shared horseshoe calibration for texture masking, ticks, and labels.</summary>
internal static class EnvironmentalTemperatureGaugeLayout
{
    public static readonly SizeF Canvas = new(512, 512);
    public static readonly RectangleF Reading = new(81, 166, 350, 180);
    public const float MechanismScale = .74f;
    public const float Center = 256, TubeRadius = 196, StartDegrees = 150, SweepDegrees = 240;

    #region Public API
    /// <summary>Fits the tube, bulb, and clamps inside the generic radial housing aperture.</summary>
    public static RectangleF MechanismBounds(RectangleF housing) => RadialGaugeLayout.MechanismBounds(housing, MechanismScale);

    /// <summary>Clamps only the visual fill while preserving the actual numeric reading.</summary>
    public static float Fill(float celsius) => Math.Clamp((celsius + 40) / 100, 0, 1);

    /// <summary>Returns the clockwise screen-space angle on the calibrated horseshoe.</summary>
    public static float Angle(float celsius) => (StartDegrees + SweepDegrees * Fill(celsius)) * MathF.PI / 180;

    /// <summary>Positions a scale element at the same angle as its corresponding liquid level.</summary>
    public static PointF Position(float celsius, float radius)
    {
        float angle = Angle(celsius);
        return new(Center + MathF.Cos(angle) * radius, Center + MathF.Sin(angle) * radius);
    }

    /// <summary>Reveals the full sprite clockwise with a one-pixel edge while retaining the bulb and neck.</summary>
    public static float Coverage(float x, float y, float fill)
    {
        if (fill >= 1) return 1;
        float bulb = MathF.Sqrt((x - 110) * (x - 110) + (y - 384) * (y - 384));
        float neck = MathF.Sqrt((x - 96) * (x - 96) + (y - 365) * (y - 365));
        float reservoir = Math.Clamp(MathF.Max(36.5f - bulb, 17.5f - neck), 0, 1);
        if (fill <= 0) return reservoir;
        float dx = x - Center, dy = y - Center;
        float progress = (MathF.Atan2(dy, dx) - StartDegrees * MathF.PI / 180 + MathF.Tau) % MathF.Tau;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float edge = (SweepDegrees * MathF.PI / 180 * fill - progress) * distance;
        return MathF.Max(reservoir, Math.Clamp(edge + .5f, 0, 1));
    }
    #endregion
}
