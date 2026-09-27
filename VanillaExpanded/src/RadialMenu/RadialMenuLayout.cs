using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Defines one circular entry arrangement and its shared pointer hit test.</summary>
public sealed class RadialMenuLayout
{
    #region Public API
    /// <summary>Creates one concentric menu ring with an optional menu occupying its center.</summary>
    public RadialMenuLayout(IReadOnlyList<string> entryIds, double innerRadius, double outerRadius,
        RadialMenuLayout? innerMenu = null, double startAngleDegrees = 0, bool clockwise = true,
        double separatorDegrees = 0.3, double radiusScale = 1, bool renderAsCenter = false)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        bool singleOptionLeaf = entryIds.Count == 1 && innerMenu is null;
        bool centerMenu = singleOptionLeaf || renderAsCenter;
        if (entryIds.Count is 0 or > 63 || entryIds.Count + (innerMenu?.EntryCount ?? 0) > 64
            || !double.IsFinite(innerRadius) || !double.IsFinite(outerRadius)
            || !double.IsFinite(startAngleDegrees) || !double.IsFinite(separatorDegrees) || !double.IsFinite(radiusScale)
            || innerRadius < 0 || outerRadius <= innerRadius || separatorDegrees < 0 || radiusScale <= 0
            || (centerMenu && (innerRadius != 0 || innerMenu is not null))
            || (!centerMenu && innerRadius <= 0)
            || (entryIds.Count > 1 && separatorDegrees >= 180d / entryIds.Count)
            || (innerMenu is not null && innerMenu.OuterRadius > innerRadius))
            throw new ArgumentOutOfRangeException(nameof(entryIds), "Nested radial rings need valid nonoverlapping radii; a one-entry innermost ring renders as a disc.");

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in entryIds)
            if (string.IsNullOrWhiteSpace(id) || !unique.Add(id)) throw new ArgumentException("Entry identifiers must be unique and nonempty.", nameof(entryIds));
        if (innerMenu is not null)
            foreach (string id in innerMenu.AllEntryIds)
                if (!unique.Add(id)) throw new ArgumentException("Entry identifiers must be unique across nested rings.", nameof(innerMenu));

        EntryIds = Array.AsReadOnly([.. entryIds]);
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        InnerMenu = innerMenu;
        StartAngleDegrees = startAngleDegrees;
        Clockwise = clockwise;
        SeparatorDegrees = separatorDegrees;
        RadiusScale = radiusScale;
        RenderAsCenter = centerMenu;
        WedgeIds = EntryIds;
        CenterId = InnerMenu?.IsSingleOption == true ? InnerMenu.EntryIds[0] : IsSingleOption ? EntryIds[0] : string.Empty;
        CenterRadius = InnerMenu?.IsSingleOption == true ? InnerMenu.OuterRadius : IsSingleOption ? OuterRadius : 0;
    }

    /// <summary>Creates the legacy outer-wedge plus center-disc shape as two nested menus.</summary>
    public RadialMenuLayout(IReadOnlyList<string> wedgeIds, string centerId, double centerRadius, double innerRadius, double outerRadius, double startAngleDegrees = 0, bool clockwise = true, double separatorDegrees = 0.3, double radiusScale = 1)
        : this(wedgeIds.Count == 0 ? [centerId] : wedgeIds,
            wedgeIds.Count == 0 ? 0 : innerRadius,
            wedgeIds.Count == 0 ? centerRadius : outerRadius,
            wedgeIds.Count == 0 ? null : new RadialMenuLayout([centerId], 0, centerRadius),
            startAngleDegrees, clockwise, separatorDegrees, radiusScale)
    {
        WedgeIds = Array.AsReadOnly([.. wedgeIds]);
        CenterId = centerId;
        CenterRadius = centerRadius;
    }

    /// <summary>Gets immutable identifiers in this ring's supplied position order.</summary>
    public IReadOnlyList<string> EntryIds { get; }
    /// <summary>Gets the menu nested inside this ring.</summary>
    public RadialMenuLayout? InnerMenu { get; }
    /// <summary>Gets all identifiers from outermost to innermost menu.</summary>
    public IEnumerable<string> AllEntryIds
    {
        get
        {
            foreach (string id in EntryIds) yield return id;
            if (InnerMenu is not null)
                foreach (string id in InnerMenu.AllEntryIds) yield return id;
        }
    }
    /// <summary>Gets the total number of entries across all nested menus.</summary>
    public int EntryCount => EntryIds.Count + (InnerMenu?.EntryCount ?? 0);
    /// <summary>Gets whether this innermost one-entry menu renders as a disc.</summary>
    public bool IsSingleOption => EntryIds.Count == 1 && InnerMenu is null;
    /// <summary>Gets whether this leaf occupies and uses the visual treatment of the center disc.</summary>
    public bool RenderAsCenter { get; }
    /// <summary>Compatibility alias for this ring's identifiers.</summary>
    public IReadOnlyList<string> WedgeIds { get; private set; }
    /// <summary>Compatibility accessor for a directly nested one-entry menu.</summary>
    public string CenterId { get; private set; }
    /// <summary>Compatibility accessor for a directly nested one-entry menu's radius.</summary>
    public double CenterRadius { get; private set; }
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
    /// <summary>Gets this caller's scale relative to the shared screen-space menu radius.</summary>
    public double RadiusScale { get; }
    /// <summary>Gets the angular width of one wedge in degrees.</summary>
    public double StepDegrees => IsSingleOption ? 360 : 360d / EntryIds.Count;

    /// <summary>Checks only the parameters that change combined mesh vertices.</summary>
    public bool HasSameGeometry(RadialMenuLayout other) => other is not null
        && EntryIds.Count == other.EntryIds.Count
        && InnerRadius == other.InnerRadius
        && OuterRadius == other.OuterRadius
        && StartAngleDegrees == other.StartAngleDegrees
        && Clockwise == other.Clockwise
        && RenderAsCenter == other.RenderAsCenter
        && ((InnerMenu is null && other.InnerMenu is null) || InnerMenu?.HasSameGeometry(other.InnerMenu!) == true);

    /// <summary>Returns the supplied identifier under a screen-space point, or null outside selectable radial bands.</summary>
    public string? HitTest(double x, double y, double centerX, double centerY, double radiusPixels)
    {
        if (radiusPixels <= 0) return null;
        double dx = (x - centerX) / radiusPixels;
        double dy = (y - centerY) / radiusPixels;
        double radius = Math.Sqrt(dx * dx + dy * dy);
        if (IsSingleOption) return radius <= OuterRadius ? EntryIds[0] : null;
        if (RenderAsCenter && radius > OuterRadius) return null;
        if (radius < InnerRadius) return InnerMenu is not null && radius <= InnerMenu.OuterRadius
            ? InnerMenu.HitTest(x, y, centerX, centerY, radiusPixels)
            : null;

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
        return EntryIds[(int)nearest % EntryIds.Count];
    }

    /// <summary>Gets the screen-space center of a wedge for icon and label placement.</summary>
    public (double X, double Y) GetWedgeCenter(int index, double centerX, double centerY, double radiusPixels, double radiusFraction)
    {
        if ((uint)index >= (uint)EntryIds.Count) throw new ArgumentOutOfRangeException(nameof(index));
        double angle = (StartAngleDegrees + (Clockwise ? 1 : -1) * index * StepDegrees) * Math.PI / 180d;
        return (centerX + Math.Sin(angle) * radiusPixels * radiusFraction, centerY - Math.Cos(angle) * radiusPixels * radiusFraction);
    }

    /// <summary>Gets the rendered center for an entry anywhere in this nested menu.</summary>
    public bool TryGetEntryCenter(string id, double centerX, double centerY, double radiusPixels, out (double X, double Y) position)
    {
        for (int index = 0; index < EntryIds.Count; index++)
        {
            if (EntryIds[index] != id) continue;
            position = IsSingleOption
                ? (centerX, centerY)
                : GetWedgeCenter(index, centerX, centerY, radiusPixels, (InnerRadius + OuterRadius) / 2d);
            return true;
        }
        if (InnerMenu is not null) return InnerMenu.TryGetEntryCenter(id, centerX, centerY, radiusPixels, out position);
        position = default;
        return false;
    }
    #endregion

    #region Geometry helpers
    /// <summary>Wraps an angle into the positive full circle.</summary>
    private static double NormalizeDegrees(double degrees) => (degrees % 360d + 360d) % 360d;
    #endregion
}



