using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Draws hover panels and permission cues, owning a reusable solid pixel for translucent filling.</summary>
internal sealed class HudOverlayGroupHighlight : IDisposable
{
    private readonly ICoreClientAPI api;
    private LoadedTexture? pixel;
    private readonly Vec4f fill;
    private readonly int border;
    private readonly IHudOverlayDashedBorder dashedBorder;
    private const float CornerLength = 8;

    #region Public API
    /// <summary>Copies the radial-menu hover palette without modifying its shared colors.</summary>
    public HudOverlayGroupHighlight(ICoreClientAPI api, IHudOverlayDashedBorder? dashedBorder = null)
    {
        this.api = api;
        this.dashedBorder = dashedBorder ?? new HudOverlayDashedBorder(api);
        var color = RadialMenuWedgeStyle.HoverFill;
        const float opacity = .45f;
        // The white pixel uses the premultiplied GUI path, so tint RGB must carry the same opacity as alpha.
        fill = new Vec4f(color.X * opacity, color.Y * opacity, color.Z * opacity, opacity);
        var line = RadialMenuWedgeStyle.HoverBorder;
        // RenderRectangle decodes ARGB; packing as texture-order ABGR would exchange orange's red and blue.
        border = ColorUtil.ToRgba(255, (int)(line.X * 255), (int)(line.Y * 255), (int)(line.Z * 255));
    }

    /// <summary>Draws a translucent panel behind hovered group content.</summary>
    public void Draw(RectangleF bounds, bool hovered)
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
    }

    /// <summary>Draws foreground corner grips for movable bounds or a dashed frame for fixed bounds.</summary>
    public void DrawOutline(RectangleF bounds, bool movable)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (!movable)
        {
            dashedBorder.Draw(bounds, HudOverlayDrawOpacity.Current);
            return;
        }
        int color = border;
        color = (color & 0x00FFFFFF) | ((int)(((uint)color >> 24) * HudOverlayDrawOpacity.Current) << 24);
        // Keep all segments inside the supplied footprint. Drawing after artwork prevents opaque
        // instrument housings from hiding the movement cue; members inherit their group's permission.
        if (movable)
        {
            float width = Math.Min(CornerLength, bounds.Width / 3);
            float height = Math.Min(CornerLength, bounds.Height / 3);
            foreach (float x in new[] { bounds.Left, bounds.Right - width })
                foreach (float y in new[] { bounds.Top, bounds.Bottom - Math.Min(1, bounds.Height) })
                    api.Render.RenderRectangle(x, y, 1, width, Math.Min(1, bounds.Height), color);
            foreach (float x in new[] { bounds.Left, bounds.Right - Math.Min(1, bounds.Width) })
                foreach (float y in new[] { bounds.Top, bounds.Bottom - height })
                    api.Render.RenderRectangle(x, y, 1, Math.Min(1, bounds.Width), height, color);
        }
    }

    /// <summary>Draws an alignment axis as a one-pixel strip, avoiding degenerate native rectangle meshes.</summary>
    public void DrawGuide(RectangleF guide)
    {
        api.Render.RenderRectangle(guide.X, guide.Y, 1, Math.Max(1, guide.Width), Math.Max(1, guide.Height), border);
    }

    /// <summary>Releases the fill pixel and dashed-border resources when the session ends.</summary>
    public void Dispose()
    {
        try { pixel?.Dispose(); }
        finally { pixel = null; dashedBorder.Dispose(); }
    }
    #endregion
}
