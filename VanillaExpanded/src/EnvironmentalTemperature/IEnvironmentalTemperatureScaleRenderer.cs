using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Owns prepared GPU resources for drawing a temperature gradient through a borrowed alpha mask.</summary>
internal interface IEnvironmentalTemperatureScaleRenderer : IDisposable
{
    /// <summary>Draws the gradient inside the host clip using the supplied visibility opacity.</summary>
    void Draw(IRenderAPI renderer, int maskTexture, RectangleF bounds, float opacity);
}
