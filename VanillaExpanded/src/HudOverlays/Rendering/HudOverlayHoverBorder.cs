using System;
using System.Drawing;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Draws a continuous edit-mode outline around the current indicator's visible bounds.</summary>
internal static class HudOverlayHoverBorder
{
    // Blend the shared orange outline equally with white to retain warmth and improve contrast.
    private static readonly int HoverColor = ColorUtil.ToRgba(255,
        (int)((RadialMenuWedgeStyle.HoverBorder.X + 1) * .5f * 255),
        (int)((RadialMenuWedgeStyle.HoverBorder.Y + 1) * .5f * 255),
        (int)((RadialMenuWedgeStyle.HoverBorder.Z + 1) * .5f * 255));

    #region Public API
    /// <summary>Draws four solid edges inside the supplied clipped footprint without allocating textures.</summary>
    public static void Draw(IRenderAPI renderer, RectangleF bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        float width = Math.Min(1, bounds.Width), height = Math.Min(1, bounds.Height);
        // Use the cue pass's foreground depth, keeping all edges inside the interactive footprint.
        renderer.RenderRectangle(bounds.Left, bounds.Top, 2, bounds.Width, height, HoverColor);
        renderer.RenderRectangle(bounds.Left, bounds.Bottom - height, 2, bounds.Width, height, HoverColor);
        renderer.RenderRectangle(bounds.Left, bounds.Top, 2, width, bounds.Height, HoverColor);
        renderer.RenderRectangle(bounds.Right - width, bounds.Top, 2, width, bounds.Height, HoverColor);
    }
    #endregion
}
