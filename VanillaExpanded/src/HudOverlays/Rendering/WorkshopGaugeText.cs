using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns cached normal-sized instrument lettering, fitted and centered within a registered aperture.</summary>
internal sealed class WorkshopGaugeText : IDisposable
{
    private readonly Func<HudOverlayPreparationContext, string, LoadedTexture> create;
    private LoadedTexture? texture;
    private HudOverlayPreparationContext? context;
    private string content = string.Empty;
    private bool dirty = true;

    #region Public API
    #region Content and preparation
    /// <summary>Gets prepared lettering dimensions in GUI units without fitting or enlarging its font.</summary>
    public SizeF Size => texture == null || context == null ? SizeF.Empty
        : new SizeF((float)(texture.Width / context.GuiScale), (float)(texture.Height / context.GuiScale));
    /// <summary>Accepts a native text factory or a headless substitute with the same ownership contract.</summary>
    public WorkshopGaugeText(Func<HudOverlayPreparationContext, string, LoadedTexture>? create = null)
    {
        this.create = create ?? CreateText;
    }

    /// <summary>Invalidates pixels only when the displayed characters change.</summary>
    public void SetContent(string value)
    {
        if (content == value) return;
        content = value;
        dirty = true;
    }

    /// <summary>Replaces owned pixels after content, locale, or GUI preparation changes.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!dirty && context == preparation) return;
        var replacement = create(preparation, content);
        texture?.Dispose();
        texture = replacement;
        context = preparation;
        dirty = false;
    }

    #endregion
    #region Drawing and lifetime
    /// <summary>Centers prepared characters without enlarging their normal HUD font size.</summary>
    public void Draw(IRenderAPI renderer, RectangleF aperture, float depth, Vec4f? tint = null)
    {
        if (texture == null || dirty) return;
        double fit = Math.Min(1, Math.Min(aperture.Width / Math.Max(1, texture.Width), aperture.Height / Math.Max(1, texture.Height)));
        double width = texture.Width * fit, height = texture.Height * fit;
        renderer.Render2DTexturePremultipliedAlpha(texture.TextureId,
            aperture.X + (aperture.Width - width) / 2, aperture.Y + (aperture.Height - height) / 2,
            width, height, depth, tint ?? DrawOpacity.PremultipliedTint);
    }

    /// <summary>Calculates a centered glyph target during geometry preparation.</summary>
    public RectangleF Fit(RectangleF aperture)
    {
        if (texture == null || dirty) return RectangleF.Empty;
        double fit = Math.Min(1, Math.Min(aperture.Width / Math.Max(1, texture.Width), aperture.Height / Math.Max(1, texture.Height)));
        double width = texture.Width * fit, height = texture.Height * fit;
        return new((float)(aperture.X + (aperture.Width - width) / 2),
            (float)(aperture.Y + (aperture.Height - height) / 2), (float)width, (float)height);
    }

    /// <summary>Draws an already fitted target without measuring or arranging glyphs.</summary>
    public void DrawAt(IRenderAPI renderer, RectangleF target, float depth, Vec4f? tint = null)
    {
        if (texture == null || dirty) return;
        renderer.Render2DTexturePremultipliedAlpha(texture.TextureId,
            (double)target.X, target.Y, target.Width, target.Height, depth, tint ?? DrawOpacity.PremultipliedTint);
    }

    /// <summary>Releases session-owned pixels and forces preparation on the next session.</summary>
    public void Reset()
    {
        texture?.Dispose();
        texture = null;
        context = null;
        dirty = true;
    }

    /// <summary>Releases the owned text texture at final removal.</summary>
    public void Dispose()
    {
        Reset();
        content = string.Empty;
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Creates warm white lettering with a dark stroke at the shared normal HUD size.</summary>
    private static LoadedTexture CreateText(HudOverlayPreparationContext context, string value)
    {
        var font = CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text)
            .WithStroke(new double[] { 0, 0, 0, .85 }, 1);
        return HudOverlayTextureFiltering.Prepare(context.Api.Gui.TextTexture.GenTextTexture(value, font));
    }
    #endregion
}
