using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Draws exact group bounds with radial-menu orange, owning only a reusable solid pixel for translucent filling.</summary>
internal sealed class HudOverlayGroupHighlight : IDisposable
{
    private readonly ICoreClientAPI api;
    private LoadedTexture? pixel;
    private readonly Vec4f fill;
    private readonly int border;

    #region Public API
    /// <summary>Copies the radial-menu hover palette without modifying its shared colors.</summary>
    public HudOverlayGroupHighlight(ICoreClientAPI api)
    {
        this.api = api;
        var color = RadialMenuWedgeStyle.HoverFill;
        const float opacity = .45f;
        // The white pixel uses the premultiplied GUI path, so tint RGB must carry the same opacity as alpha.
        fill = new Vec4f(color.X * opacity, color.Y * opacity, color.Z * opacity, opacity);
        var line = RadialMenuWedgeStyle.HoverBorder;
        // RenderRectangle decodes ARGB; packing as texture-order ABGR would exchange orange's red and blue.
        border = ColorUtil.ToRgba(255, (int)(line.X * 255), (int)(line.Y * 255), (int)(line.Z * 255));
    }

    /// <summary>Draws a translucent panel behind content and an optional native line-only rectangle at the same bounds.</summary>
    public void Draw(RectangleF bounds, bool hovered, bool movable)
    {
        if (hovered)
        {
            if (pixel == null)
            {
                using var surface = new ImageSurface(Format.Argb32, 1, 1);
                using var context = new Context(surface);
                context.SetSourceRGBA(1, 1, 1, 1);
                context.Paint();
                pixel = new LoadedTexture(api) { TextureId = api.Gui.LoadCairoTexture(surface, true), Width = 1, Height = 1 };
            }
            api.Render.Render2DTexturePremultipliedAlpha(pixel.TextureId, bounds.X, bounds.Y,
                bounds.Width, bounds.Height, 0, fill);
        }
        if (movable) api.Render.RenderRectangle(bounds.X, bounds.Y, 1, bounds.Width, bounds.Height, border);
    }

    /// <summary>Draws an alignment axis as a one-pixel strip, avoiding degenerate native rectangle meshes.</summary>
    public void DrawGuide(RectangleF guide)
    {
        api.Render.RenderRectangle(guide.X, guide.Y, 1, Math.Max(1, guide.Width), Math.Max(1, guide.Height), border);
    }

    /// <summary>Releases the owned pixel when the session ends.</summary>
    public void Dispose()
    {
        pixel?.Dispose();
        pixel = null;
    }
    #endregion
}
