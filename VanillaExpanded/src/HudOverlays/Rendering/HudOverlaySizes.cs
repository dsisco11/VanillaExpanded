namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Defines absolute HUD dimensions in GUI units; only the game's GUI scale converts them to pixels.</summary>
public static class HudOverlaySizes
{
    /// <summary>Compact artwork and supporting symbols.</summary>
    public const float Small = 24;
    /// <summary>Standard square indicator footprint.</summary>
    public const float Standard = 36;
    /// <summary>Large item artwork.</summary>
    public const float Large = 48;
    /// <summary>Wide environmental readout footprint.</summary>
    public const float Wide = 54;
    /// <summary>Normal HUD font size.</summary>
    public const float Text = 14;
    /// <summary>Enlarged supporting text for item counts.</summary>
    public const float CountText = 21;
    /// <summary>Thermometer width.</summary>
    public const float ThermometerWidth = 21;
    /// <summary>Gap between thermometer and reading.</summary>
    public const float ReadingGap = 4.5f;
    /// <summary>Trend chevron width.</summary>
    public const float ChevronWidth = 12;
    /// <summary>Trend chevron height.</summary>
    public const float ChevronHeight = 7.5f;
    /// <summary>Gap between trend chevrons.</summary>
    public const float ChevronGap = 1.5f;
    /// <summary>Body-temperature reading band height.</summary>
    public const float BodyReadingHeight = 18;
    /// <summary>Wetness reading band height.</summary>
    public const float WetnessReadingHeight = 12;
    /// <summary>Environmental reading band height.</summary>
    public const float EnvironmentalReadingHeight = 21;
    /// <summary>Environmental scale width.</summary>
    public const float TemperatureScaleWidth = 45;
}
