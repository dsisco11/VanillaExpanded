using System;
using System.Drawing;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns the graphics resources used to draw one procedural fixed-item frame.</summary>
internal interface IHudOverlayDashedBorder : IDisposable
{
    /// <summary>Draws a frame inside physical-pixel bounds using the enclosing visibility opacity.</summary>
    void Draw(RectangleF bounds, float opacity);
}
