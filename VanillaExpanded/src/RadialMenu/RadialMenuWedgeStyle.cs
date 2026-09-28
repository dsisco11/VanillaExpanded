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
    internal const float IconInsetPixels = IconHaloRadiusPixels + BorderWidthPixels + 2f;
    internal const float IconHaloRadiusPixels = 3f;
    internal const float HoverShadowOffsetXPixels = 3f;
    internal const float HoverShadowOffsetYPixels = 5f;
    internal const float HoverShadowExpansionPixels = 10f;
    internal const float HoverShadowFalloffPixels = 10f;
    internal static readonly Vec4f IconHaloTint = new(0.055f, 0.035f, 0.02f, 0.55f);
    internal static readonly Vec4f HoverShadow = new(0.015f, 0.01f, 0.008f, 0.38f);
    internal static readonly Vec3f DisabledFill = new(0.17f, 0.11f, 0.075f);
    internal static readonly Vec3f EnabledFill = new(0.38f, 0.21f, 0.075f);
    internal static readonly Vec3f HoverFill = new(0.72f, 0.40f, 0.12f);
    internal static readonly Vec3f SelectedFill = new(0.90f, 0.56f, 0.19f);
    internal static readonly Vec3f Border = new(0.62f, 0.38f, 0.17f);
    internal static readonly Vec3f HoverBorder = new(0.94f, 0.60f, 0.22f);
}
