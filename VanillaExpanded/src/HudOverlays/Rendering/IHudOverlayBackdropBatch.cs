using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns a frame's clipped backdrop collection and shared shader pass.</summary>
internal interface IHudOverlayBackdropBatch
{
    /// <summary>Collects one member's backdrops under its visible clip.</summary>
    void Collect(RectangleF clip, Action collect);
    /// <summary>Draws and clears the collected circles with one shader activation.</summary>
    void Flush(IRenderAPI renderer);
    /// <summary>Discards pending positions after frame completion or failure.</summary>
    void Clear();
}
