using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Draws the independent pointer through the engine's standard textured GUI path.</summary>
internal interface IApproximateTimeDialRenderer
{
    /// <summary>Draws the pointer about its registered pivot using the supplied visibility opacity.</summary>
    void Draw(IRenderAPI renderer, int pointerTexture, RectangleF bounds, float opacity, float rotation);
}
