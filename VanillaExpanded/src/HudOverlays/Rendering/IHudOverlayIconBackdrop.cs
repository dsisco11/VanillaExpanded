using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Separates prepared backdrop resources from feature-owned icon content.</summary>
internal interface IHudOverlayIconBackdrop
{
    /// <summary>Draws a 24-unit background at the supplied GUI pixel position and scale.</summary>
    void Draw(IRenderAPI renderer, double x, double y, double scale);
}
