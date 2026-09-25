using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Defines one circular entry arrangement and its shared pointer hit test.</summary>
public sealed class RadialMenuLayout
{
    #region Public API
    /// <summary>Creates a circular layout with the first wedge centered at the supplied clockwise angle from screen up.</summary>
    public RadialMenuLayout(IReadOnlyList<string> wedgeIds, string centerId, double centerRadius, double innerRadius, double outerRadius, double startAngleDegrees = 0, bool clockwise = true, double separatorDegrees = 0.3)
    {
        ArgumentNullException.ThrowIfNull(wedgeIds);
        if (wedgeIds.Count > 63 || string.IsNullOrWhiteSpace(centerId) || !double.IsFinite(centerRadius) || !double.IsFinite(innerRadius) || !double.IsFinite(outerRadius) || !double.IsFinite(startAngleDegrees) || !double.IsFinite(separatorDegrees) || centerRadius <= 0 || innerRadius <= centerRadius || outerRadius <= innerRadius || separatorDegrees < 0 || (wedgeIds.Count > 0 && separatorDegrees >= 180d / wedgeIds.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(wedgeIds), "The radial layout needs distinct positive radii and a separator narrower than half a wedge.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal) { centerId };
        foreach (string id in wedgeIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !unique.Add(id)) throw new ArgumentException("Entry identifiers must be unique and nonempty.", nameof(wedgeIds));
        }

        WedgeIds = Array.AsReadOnly([.. wedgeIds]);
        CenterId = centerId;
        CenterRadius = centerRadius;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        StartAngleDegrees = startAngleDegrees;
        Clockwise = clockwise;
        SeparatorDegrees = separatorDegrees;
    }

    /// <summary>Gets immutable wedge identifiers in their supplied position order.</summary>
    public IReadOnlyList<string> WedgeIds { get; }
    /// <summary>Gets the center identifier.</summary>
    public string CenterId { get; }
    /// <summary>Gets the center disc radius relative to the rendered unit radius.</summary>
    public double CenterRadius { get; }
    /// <summary>Gets the wedge inner radius relative to the rendered unit radius.</summary>
    public double InnerRadius { get; }
    /// <summary>Gets the wedge outer radius relative to the rendered unit radius.</summary>
    public double OuterRadius { get; }
    /// <summary>Gets the first wedge center angle measured from screen up.</summary>
    public double StartAngleDegrees { get; }
    /// <summary>Gets whether increasing indices turn clockwise.</summary>
    public bool Clockwise { get; }
    /// <summary>Gets the angular half-gap at each wedge boundary.</summary>
    public double SeparatorDegrees { get; }
    /// <summary>Gets the angular width of one wedge in degrees.</summary>
    public double StepDegrees => WedgeIds.Count == 0 ? 0 : 360d / WedgeIds.Count;

    /// <summary>Checks only the parameters that change combined mesh vertices.</summary>
    public bool HasSameGeometry(RadialMenuLayout other) => other is not null
        && WedgeIds.Count == other.WedgeIds.Count
        && CenterRadius == other.CenterRadius
        && InnerRadius == other.InnerRadius
        && OuterRadius == other.OuterRadius
        && StartAngleDegrees == other.StartAngleDegrees
        && Clockwise == other.Clockwise;

    /// <summary>Returns the supplied identifier under a screen-space point, or null outside selectable radial bands.</summary>
    public string? HitTest(double x, double y, double centerX, double centerY, double radiusPixels)
    {
        if (radiusPixels <= 0) return null;
        double dx = (x - centerX) / radiusPixels;
        double dy = (y - centerY) / radiusPixels;
        double radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius <= CenterRadius) return CenterId;
        if (radius < InnerRadius) return null;
        if (WedgeIds.Count == 0) return null;

        // Screen Y grows downward, so atan2(dx, -dy) measures clockwise from up.
        double angle = Math.Atan2(dx, -dy) * 180d / Math.PI;
        double directed = NormalizeDegrees(Clockwise ? angle - StartAngleDegrees : StartAngleDegrees - angle);
        double nearest = Math.Floor(directed / StepDegrees + 0.5);
        double delta = directed - nearest * StepDegrees;
        if (delta > 180) delta -= 360;
        if (radius <= OuterRadius)
        {
            // Apply the same inward-rounded annular contour used by the shader, in screen pixels.
            double radialInside = Math.Min(radius - InnerRadius, OuterRadius - radius) * radiusPixels;
            double halfAngle = StepDegrees / 2d * Math.PI / 180d;
            double angularInside = radius * Math.Sin(halfAngle - Math.Abs(delta) * Math.PI / 180d) * radiusPixels;
            double corner = Math.Min(RadialMenuWedgeStyle.CornerRadiusPixels,
                Math.Max(0d, Math.Min((OuterRadius - InnerRadius) * radiusPixels / 2d,
                    radius * Math.Sin(halfAngle) * radiusPixels / 2d)));
            if (radialInside < corner && angularInside < corner
                && corner - Math.Sqrt(Math.Pow(corner - radialInside, 2d) + Math.Pow(corner - angularInside, 2d)) <= 0d)
                return null;
        }
        return WedgeIds[(int)nearest % WedgeIds.Count];
    }

    /// <summary>Gets the screen-space center of a wedge for icon and label placement.</summary>
    public (double X, double Y) GetWedgeCenter(int index, double centerX, double centerY, double radiusPixels, double radiusFraction)
    {
        if ((uint)index >= (uint)WedgeIds.Count) throw new ArgumentOutOfRangeException(nameof(index));
        double angle = (StartAngleDegrees + (Clockwise ? 1 : -1) * index * StepDegrees) * Math.PI / 180d;
        return (centerX + Math.Sin(angle) * radiusPixels * radiusFraction, centerY - Math.Cos(angle) * radiusPixels * radiusFraction);
    }
    #endregion

    #region Geometry helpers
    /// <summary>Wraps an angle into the positive full circle.</summary>
    private static double NormalizeDegrees(double degrees) => (degrees % 360d + 360d) % 360d;
    #endregion
}



