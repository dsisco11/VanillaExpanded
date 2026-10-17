using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Rotates the engine's textured GUI quad around the pointer's registered spindle.</summary>
internal sealed class ApproximateTimeDialRenderer : IApproximateTimeDialRenderer
{
    private readonly Vec4f tint = new(1, 1, 1, 1);

    #region Public API
    /// <summary>Draws the pointer with the standard GUI shader and restores the enclosing model-view matrix.</summary>
    public void Draw(IRenderAPI renderer, int pointerTexture, RectangleF bounds, float opacity, float rotation)
    {
        double pivotX = bounds.Width * RadialGaugeLayout.CenterX / RadialGaugeLayout.Canvas,
            pivotY = bounds.Height * RadialGaugeLayout.CenterY / RadialGaugeLayout.Canvas;
        tint.W = opacity;
        renderer.GlPushMatrix();
        try
        {
            // Move the registered spindle to the origin before rotating the complete texture quad.
            // GUI Y increases downward, so positive Z rotation advances clockwise from noon.
            renderer.GlTranslate(bounds.X + pivotX, bounds.Y + pivotY, 0d);
            renderer.GlRotate(rotation * 180 / MathF.PI, 0, 0, 1);
            renderer.RenderTexture(pointerTexture, -pivotX, -pivotY, bounds.Width, bounds.Height, 52, tint);
        }
        finally { renderer.GlPopMatrix(); }
    }
    #endregion
}
