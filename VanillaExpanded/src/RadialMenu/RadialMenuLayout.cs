using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Defines one circular entry arrangement and its shared pointer hit test.</summary>
public sealed class RadialMenuLayout
{
    private readonly double radiusScale;
    private readonly Func<float>? sizeMultiplier;
    private float wedgeAlignedIconSize;
    private double wedgeIconRadiusPixels;
    private double wedgeIconInsetPixels;
    private float[]? screenAlignedIconSizes;
    private double iconSizeRadiusPixels;
    private double iconSizeInsetPixels;

    #region Public API
    /// <summary>Creates one concentric menu ring with an optional inner menu and live size multiplier bounded to 0.15–2.5.</summary>
    public RadialMenuLayout(IReadOnlyList<string> entryIds, double innerRadius, double outerRadius,
        RadialMenuLayout? innerMenu = null, double startAngleDegrees = 0, bool clockwise = true,
        double separatorDegrees = 0.3, double radiusScale = 1, bool renderAsCenter = false, Func<float>? sizeMultiplier = null, double labelFontScale = 1)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        bool singleOptionLeaf = entryIds.Count == 1 && innerMenu is null;
        bool centerMenu = singleOptionLeaf || renderAsCenter;
        if (entryIds.Count is 0 or > 63 || entryIds.Count + (innerMenu?.EntryCount ?? 0) > 64
            || !double.IsFinite(innerRadius) || !double.IsFinite(outerRadius)
            || !double.IsFinite(startAngleDegrees) || !double.IsFinite(separatorDegrees) || !double.IsFinite(radiusScale)
            || !double.IsFinite(labelFontScale) || labelFontScale <= 0
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
        this.radiusScale = radiusScale;
        this.sizeMultiplier = sizeMultiplier;
        RenderAsCenter = centerMenu;
        LabelFontScale = labelFontScale;
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

    /// <summary>Gets the preferred text size relative to the standard GUI label, before circle fitting.</summary>
    public double LabelFontScale { get; }

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
    /// <summary>Gets this caller's scale relative to the shared screen-space menu radius, including its current size multiplier.</summary>
    public double RadiusScale
    {
        get
        {
            // Read live settings for both rendering and hit testing, with safe bounds for hand-edited configuration.
            float multiplier = sizeMultiplier?.Invoke() ?? 1f;
            return radiusScale * (float.IsFinite(multiplier) ? Math.Clamp(multiplier, 0.15f, 2.5f) : 1f);
        }
    }
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

    /// <summary>Gets the largest icon square supported by this ring's radial and angular dimensions.</summary>
    internal float GetIconSizePixels(double radiusPixels, double insetPixels)
    {
        if (radiusPixels <= 0 || insetPixels < 0) throw new ArgumentOutOfRangeException(nameof(radiusPixels));
        if (IsSingleOption) return (float)Math.Max(1d, OuterRadius * radiusPixels * 2d - insetPixels * 2d);
        double midRadius = (InnerRadius + OuterRadius) / 2d;
        double halfAngle = Math.Clamp(StepDegrees / 2d - SeparatorDegrees, 0d, 90d) * Math.PI / 180d;
        // A circle centered in the wedge fits both arcs and side boundaries. Inscribe the icon square
        // in that circle so every corner fits regardless of wedge direction or authored icon rotation.
        double radialClearance = (OuterRadius - InnerRadius) * radiusPixels / 2d;
        double angularClearance = midRadius * radiusPixels * Math.Sin(halfAngle);
        double clearance = Math.Min(radialClearance, angularClearance) - insetPixels;
        return (float)Math.Max(1d, Math.Sqrt(2d) * clearance);
    }

    /// <summary>Fits a wedge-aligned square directly between the arcs and sides, caching its screen-space allowance.</summary>
    internal float GetWedgeAlignedIconSizePixels(double radiusPixels, double insetPixels)
    {
        if (radiusPixels <= 0 || insetPixels < 0) throw new ArgumentOutOfRangeException(nameof(radiusPixels));
        if (wedgeAlignedIconSize > 0f && radiusPixels == wedgeIconRadiusPixels && insetPixels == wedgeIconInsetPixels)
            return wedgeAlignedIconSize;

        if (IsSingleOption) wedgeAlignedIconSize = GetIconSizePixels(radiusPixels, insetPixels);
        else
        {
            double midRadius = (InnerRadius + OuterRadius) * radiusPixels / 2d;
            double outerRadius = Math.Max(0d, OuterRadius * radiusPixels - insetPixels);
            double halfAngle = Math.Clamp(StepDegrees / 2d - SeparatorDegrees, 0d, 90d) * Math.PI / 180d;
            // In wedge-local coordinates the square is centered at (0, midRadius). Its inner
            // edge, farthest outer corner, and side-normal support give three exact limits.
            double innerHalfSize = midRadius - InnerRadius * radiusPixels - insetPixels;
            double outerHalfSize = (Math.Sqrt(Math.Max(0d, 2d * outerRadius * outerRadius - midRadius * midRadius)) - midRadius) / 2d;
            double sideHalfSize = (midRadius * Math.Sin(halfAngle) - insetPixels)
                / (Math.Sin(halfAngle) + Math.Cos(halfAngle));
            wedgeAlignedIconSize = (float)Math.Max(1d, 2d * Math.Min(innerHalfSize, Math.Min(outerHalfSize, sideHalfSize)));
        }
        wedgeIconRadiusPixels = radiusPixels;
        wedgeIconInsetPixels = insetPixels;
        return wedgeAlignedIconSize;
    }

    /// <summary>Fits an upright icon square to one wedge's arcs and sides, retaining the requested padding.</summary>
    internal float GetScreenAlignedIconSizePixels(int index, double radiusPixels, double insetPixels)
    {
        if ((uint)index >= (uint)EntryIds.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (radiusPixels <= 0 || insetPixels < 0) throw new ArgumentOutOfRangeException(nameof(radiusPixels));

        // Geometry is immutable for this layout. Reuse its per-entry values until a screen-space
        // input changes; configuration scaling is applied later by the icon renderer.
        screenAlignedIconSizes ??= new float[EntryIds.Count];
        if (radiusPixels != iconSizeRadiusPixels || insetPixels != iconSizeInsetPixels)
        {
            Array.Clear(screenAlignedIconSizes);
            iconSizeRadiusPixels = radiusPixels;
            iconSizeInsetPixels = insetPixels;
        }
        if (screenAlignedIconSizes[index] == 0f)
            screenAlignedIconSizes[index] = CalculateScreenAlignedIconSizePixels(index, radiusPixels, insetPixels);
        return screenAlignedIconSizes[index];
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
    /// <summary>Calculates the padded upright square fit when a cached entry size is unavailable.</summary>
    private float CalculateScreenAlignedIconSizePixels(int index, double radiusPixels, double insetPixels)
    {
        if (IsSingleOption) return GetIconSizePixels(radiusPixels, insetPixels);

        double angle = (StartAngleDegrees + (Clockwise ? 1 : -1) * index * StepDegrees) * Math.PI / 180d;
        double midRadius = (InnerRadius + OuterRadius) * radiusPixels / 2d;
        double x = Math.Abs(Math.Sin(angle) * midRadius);
        double y = Math.Abs(Math.Cos(angle) * midRadius);
        double outer = Math.Max(0d, OuterRadius * radiusPixels - insetPixels);
        double inner = InnerRadius * radiusPixels + insetPixels;

        // The farthest corner limits the outer arc. The nearest point of the square (which can
        // lie on an edge rather than a corner) limits the inner arc.
        double difference = Math.Abs(x - y);
        double outerHalfSize = (Math.Sqrt(Math.Max(0d, 2d * outer * outer - difference * difference)) - x - y) / 2d;
        double innerHalfSize = difference >= inner
            ? Math.Max(x, y) - inner
            : (x + y - Math.Sqrt(Math.Max(0d, 2d * inner * inner - difference * difference))) / 2d;

        // Project the square's half extent onto both side normals. Their screen orientation
        // determines how much space an upright square uses, unlike the rotation-safe circle fit.
        double halfAngle = Math.Clamp(StepDegrees / 2d - SeparatorDegrees, 0d, 90d) * Math.PI / 180d;
        double sideExtent = Math.Max(
            Math.Abs(Math.Cos(angle - halfAngle)) + Math.Abs(Math.Sin(angle - halfAngle)),
            Math.Abs(Math.Cos(angle + halfAngle)) + Math.Abs(Math.Sin(angle + halfAngle)));
        double sideHalfSize = (midRadius * Math.Sin(halfAngle) - insetPixels) / sideExtent;
        return (float)Math.Max(1d, 2d * Math.Min(sideHalfSize, Math.Min(innerHalfSize, outerHalfSize)));
    }

    /// <summary>Wraps an angle into the positive full circle.</summary>
    private static double NormalizeDegrees(double degrees) => (degrees % 360d + 360d) % 360d;
    #endregion
}



