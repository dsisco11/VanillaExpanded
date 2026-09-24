using Vintagestory.API.MathTools;

namespace VanillaExpanded.RadialMenu;

/// <summary>Shares screen-space wedge shape and appearance settings between rendering and hit testing.</summary>
internal static class RadialMenuWedgeStyle
{
    internal const float BorderWidthPixels = 1.5f;
    internal const float CornerRadiusPixels = 4f;
    internal const float HoverDurationSeconds = 0.1f;
    internal const float HoverScale = 1.15f;
    internal const float EnabledOpacity = 0.72f;
    internal const float DisabledOpacity = 0.58f;
    internal const float DefaultGrainStrength = 0.035f;
    internal static readonly Vec3f DisabledFill = new(0.17f, 0.11f, 0.075f);
    internal static readonly Vec3f EnabledFill = new(0.38f, 0.21f, 0.075f);
    internal static readonly Vec3f HoverFill = new(0.72f, 0.40f, 0.12f);
    internal static readonly Vec3f SelectedFill = new(0.90f, 0.56f, 0.19f);
    internal static readonly Vec3f Border = new(0.52f, 0.31f, 0.13f);
    internal static readonly Vec3f HoverBorder = new(0.94f, 0.60f, 0.22f);
}
