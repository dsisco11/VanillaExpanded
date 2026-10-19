using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Draws the independent pointer and its runtime shadow through the engine's textured GUI path.</summary>
internal interface IApproximateTimeDialRenderer
{
    /// <summary>Draws the shadow beneath the pointer about its registered pivot using the supplied visibility opacity.</summary>
    void Draw(IRenderAPI renderer, int pointerTexture, RectangleF bounds, float opacity, float rotation);
}
