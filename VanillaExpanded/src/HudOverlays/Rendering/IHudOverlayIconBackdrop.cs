using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Separates prepared backdrop resources from feature-owned icon content.</summary>
internal interface IHudOverlayIconBackdrop
{
    /// <summary>Queues a standard-size background during the host's collection pass at the supplied pixel position and scale.</summary>
    void Queue(IRenderAPI renderer, double x, double y, double scale);
}
