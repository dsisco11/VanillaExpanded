using System;
using System.Collections.Generic;
using System.Drawing;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Draws directional grouping feedback separately from the orange movement chrome.</summary>
internal sealed class HudSnapHighlight : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Dictionary<HudSnapSide, LoadedTexture> gradients = new();
    private readonly int line = ColorUtil.ToRgba(255, 215, 85, 100);

    #region Public API
    /// <summary>Borrows the session render API and owns only cached gradient textures.</summary>
    public HudSnapHighlight(ICoreClientAPI api) { this.api = api; }

    /// <summary>Draws a band strongest beside the target item, fading into the incoming item.</summary>
    public void DrawConnection(RectangleF destination, RectangleF insertion, HudSnapSide side)
    {
        var band = ConnectionBounds(destination, insertion, side);
        if (band.Width <= 0 || band.Height <= 0) return;
        if (!gradients.TryGetValue(side, out var texture))
        {
            // Each direction is rasterized once; movement changes only its destination rectangle.
            using var surface = new ImageSurface(Format.Argb32, 64, 64);
            using var context = new Context(surface);
            bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
            bool reverse = side is HudSnapSide.Left or HudSnapSide.Above;
            using var gradient = new LinearGradient(reverse && horizontal ? 64 : 0, reverse && !horizontal ? 64 : 0,
                horizontal && !reverse ? 64 : 0, !horizontal && !reverse ? 64 : 0);
            gradient.AddColorStop(0, new Cairo.Color(.84, .22, .3, .85));
            gradient.AddColorStop(1, new Cairo.Color(.84, .22, .3, 0));
            context.SetSource(gradient);
            context.Paint();
            texture = new LoadedTexture(api) { TextureId = api.Gui.LoadCairoTexture(surface, true), Width = 64, Height = 64 };
            gradients.Add(side, texture);
        }
        api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, band.X, band.Y, band.Width, band.Height, 0);
    }

    /// <summary>Draws alignment-only feedback without implying group membership.</summary>
    public void DrawGuide(RectangleF guide) => api.Render.RenderRectangle(guide.X, guide.Y, 1,
        Math.Max(1, guide.Width), Math.Max(1, guide.Height), line);

    /// <summary>Restricts the band to the shared edge span and the incoming item's interior.</summary>
    public static RectangleF ConnectionBounds(RectangleF destination, RectangleF insertion, HudSnapSide side)
    {
        bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
        float start = horizontal ? Math.Max(destination.Top, insertion.Top) : Math.Max(destination.Left, insertion.Left);
        float end = horizontal ? Math.Min(destination.Bottom, insertion.Bottom) : Math.Min(destination.Right, insertion.Right);
        if (end <= start || destination.Width <= 0 || destination.Height <= 0 || insertion.Width <= 0 || insertion.Height <= 0) return RectangleF.Empty;
        float depth = Math.Min(12, horizontal ? insertion.Width : insertion.Height);
        return side switch
        {
            HudSnapSide.Left => new(insertion.Right - depth, start, depth, end - start),
            HudSnapSide.Right => new(insertion.Left, start, depth, end - start),
            HudSnapSide.Above => new(start, insertion.Bottom - depth, end - start, depth),
            _ => new(start, insertion.Top, end - start, depth)
        };
    }

    /// <summary>Releases every session-owned gradient.</summary>
    public void Dispose()
    {
        foreach (var texture in gradients.Values) texture.Dispose();
        gradients.Clear();
    }
    #endregion
}

