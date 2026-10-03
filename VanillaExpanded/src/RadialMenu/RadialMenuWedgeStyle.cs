using Vintagestory.API.MathTools;

namespace VanillaExpanded.RadialMenu;

/// <summary>Shares screen-space wedge shape and appearance settings between rendering and hit testing.</summary>
internal static class RadialMenuWedgeStyle
{
    /// <summary>Thickness of the wedge outline in screen pixels.</summary>
    internal const float BorderWidthPixels = 2.5f;
    /// <summary>Radius of rounded wedge corners in screen pixels, shared with pointer hit testing.</summary>
    internal const float CornerRadiusPixels = 4f;
    /// <summary>Time in seconds for a wedge to animate fully into or out of its hovered state.</summary>
    internal const float HoverDurationSeconds = 0.1f;
    /// <summary>Maximum size multiplier applied to a hovered wedge and its icon.</summary>
    internal const float HoverScale = 1.135f;
    /// <summary>Background opacity of enabled entries; hovered enabled entries become fully opaque.</summary>
    internal const float EnabledOpacity = 0.6f;
    /// <summary>Background opacity of disabled entries, making unavailable choices less prominent.</summary>
    internal const float DisabledOpacity = 0.4f;
    /// <summary>Opacity of the screen-darkening backdrop behind the modal menu; zero is transparent and one is opaque.</summary>
    internal const float BackdropOpacity = 0.60f;
    /// <summary>Amplitude of the subtle procedural grain added to wedge background colors.</summary>
    internal const float DefaultGrainStrength = 0.035f;
    /// <summary>Space reserved around icons for their halo, the wedge border, and additional padding.</summary>
    internal const float IconInsetPixels = IconHaloRadiusPixels + BorderWidthPixels + 2f;
    /// <summary>Extent of the soft icon halo in screen pixels.</summary>
    internal const float IconHaloRadiusPixels = 3f;
    /// <summary>Dark brown RGB tint and opacity of the halo that separates icons from the background.</summary>
    internal static readonly Vec4f IconHaloTint = new(0.055f, 0.035f, 0.02f, 0.7f);
    /// <summary>Muted brown background color for unavailable entries.</summary>
    internal static readonly Vec3f DisabledFill = new(0.17f, 0.11f, 0.075f);
    /// <summary>Default bronze background color for available entries.</summary>
    internal static readonly Vec3f EnabledFill = new(0.38f, 0.21f, 0.075f);
    /// <summary>Brighter bronze background color blended in as an enabled entry is hovered.</summary>
    internal static readonly Vec3f HoverFill = new(0.72f, 0.40f, 0.12f);
    /// <summary>Gold background color identifying the currently selected entry.</summary>
    internal static readonly Vec3f SelectedFill = new(0.90f, 0.56f, 0.19f);
    /// <summary>Default bronze color of wedge outlines.</summary>
    internal static readonly Vec3f Border = new(0.62f, 0.38f, 0.17f);
    /// <summary>Highlighted outline color blended in as an enabled entry is hovered.</summary>
    internal static readonly Vec3f HoverBorder = new(0.94f, 0.60f, 0.22f);
}
