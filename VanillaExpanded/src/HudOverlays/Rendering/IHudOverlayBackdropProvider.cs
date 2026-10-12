using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Supplies prepared icon backdrops separately from foreground drawing.</summary>
internal interface IHudOverlayBackdropProvider
{
    /// <summary>Queues visible backdrop positions without drawing foreground content or sampling gameplay.</summary>
    void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds);
}
