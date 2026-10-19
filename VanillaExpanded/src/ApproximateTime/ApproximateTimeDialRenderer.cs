using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Draws a runtime drop shadow and rotates the hand around its registered spindle.</summary>
internal sealed class ApproximateTimeDialRenderer : IApproximateTimeDialRenderer
{
    private readonly Vec4f tint = new(1, 1, 1, 1);
    private readonly Vec4f shadowTint = new(0, 0, 0, 1);

    #region Public API
    /// <summary>Draws an offset translucent silhouette beneath the hand using its existing texture alpha.</summary>
    public void Draw(IRenderAPI renderer, int pointerTexture, RectangleF bounds, float opacity, float rotation)
    {
        shadowTint.W = opacity * .35f;
        tint.W = opacity;
        // Keep the light direction fixed on screen as the hand turns, scaling the offset with the gauge.
        var shadowBounds = new RectangleF(bounds.X + bounds.Width * 6 / RadialGaugeLayout.Canvas,
            bounds.Y + bounds.Height * 8 / RadialGaugeLayout.Canvas, bounds.Width, bounds.Height);
        DrawLayer(renderer, pointerTexture, shadowBounds, rotation, shadowTint, 51.5f);
        DrawLayer(renderer, pointerTexture, bounds, rotation, tint, 52);
    }
    #endregion

    #region Private
    /// <summary>Rotates one textured layer about its spindle and restores the caller's matrix even if drawing fails.</summary>
    private static void DrawLayer(IRenderAPI renderer, int pointerTexture, RectangleF bounds, float rotation, Vec4f color, float depth)
    {
        double pivotX = bounds.Width * RadialGaugeLayout.CenterX / RadialGaugeLayout.Canvas,
            pivotY = bounds.Height * RadialGaugeLayout.CenterY / RadialGaugeLayout.Canvas;
        renderer.GlPushMatrix();
        try
        {
            // Move the registered spindle to the origin before rotating the complete texture quad.
            // GUI Y increases downward, so positive Z rotation advances clockwise from noon.
            renderer.GlTranslate(bounds.X + pivotX, bounds.Y + pivotY, 0d);
            renderer.GlRotate(rotation * 180 / MathF.PI, 0, 0, 1);
            renderer.RenderTexture(pointerTexture, -pivotX, -pivotY, bounds.Width, bounds.Height, depth, color);
        }
        finally { renderer.GlPopMatrix(); }
    }
    #endregion
}
