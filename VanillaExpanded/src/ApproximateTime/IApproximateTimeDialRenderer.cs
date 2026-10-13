using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Owns prepared GPU resources for drawing a rotating scenery disk through the copper aperture.</summary>
internal interface IApproximateTimeDialRenderer : IDisposable
{
    /// <summary>Draws the rotating disk inside the host clip using the supplied visibility opacity.</summary>
    void Draw(IRenderAPI renderer, int maskTexture, RectangleF bounds, float opacity, float rotation);
}
