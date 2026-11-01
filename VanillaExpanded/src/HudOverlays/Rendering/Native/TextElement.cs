using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.Gui;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Owns native contrast glyphs with prepared fitting and per-frame fade tint.</summary>
internal sealed class TextElement : GuiElementTextBase
{
    private readonly Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture> create;
    private readonly Action draw;
    private LoadedTexture? texture;
    private SizeF naturalSize;
    private HudOverlayPreparationContext? context;
    private RectangleF aperture, clip;
    private SizeF? fittedSize;
    private bool hasLayout, disposed;
    private readonly double strokeWidth;
    private readonly float depth;
    private readonly EnumTextOrientation orientation;

    #region Public API
    /// <summary>Gets pre-render clip, target, tint and failure state.</summary>
    public ElementState State { get; }
    /// <summary>Gets intrinsic glyph size in unscaled GUI units.</summary>
    public SizeF Size => naturalSize;
    #region Preparation
    /// <summary>Creates a passive native text element with an optional headless glyph factory.</summary>
    public TextElement(ICoreClientAPI api, ElementBounds bounds, Action<Exception> failure,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? create = null, double strokeWidth = 1, float depth = 51,
        EnumTextOrientation orientation = EnumTextOrientation.Left)
        : base(api, string.Empty, TextMetrics.Font(strokeWidth).WithOrientation(orientation), bounds)
    {
        this.create = create ?? CreateTexture;
        this.strokeWidth = strokeWidth; this.depth = depth; this.orientation = orientation;
        State = new(failure);
        draw = DrawTexture;
    }
    /// <summary>Replaces glyphs only when characters, font sizing or preparation context change.</summary>
    public void Prepare(HudOverlayPreparationContext preparation, string? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(preparation);
        if (!ReferenceEquals(api, preparation.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(preparation));
        value ??= string.Empty;
        if (context == preparation && text == value) return;
        CairoFont nextFont = TextMetrics.Font(strokeWidth)
            .WithOrientation(orientation)
            .WithFontSize((float)(HudOverlaySizes.Text * preparation.IndicatorScale))
            .WithStroke(new double[] { 0, 0, 0, .85 }, strokeWidth * preparation.IndicatorScale);
        // Allocate before replacing the surviving resource so generation failure preserves cleanup ownership.
        LoadedTexture? replacement = value.Length == 0 ? null : create(preparation, value, nextFont)
            ?? throw new InvalidOperationException("Native glyph preparation returned no texture.");
        LoadedTexture? previous = texture;
        texture = replacement; text = value; context = preparation; Font = nextFont;
        naturalSize = replacement == null ? SizeF.Empty
            : new((float)(replacement.Width / preparation.GuiScale), (float)(replacement.Height / preparation.GuiScale));
        fittedSize = null;
        if (!ReferenceEquals(previous, replacement)) previous?.Dispose();
        if (hasLayout) CacheGeometry();
    }
    /// <summary>Rerasterizes at the fitted font size after layout, then caches natural-size pixels.</summary>
    public void LayoutCompleted(RectangleF effectiveClip)
    {
        aperture = new((float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight);
        clip = effectiveClip; hasLayout = true;
        if (context != null && text.Length > 0 && aperture.Width > 0 && aperture.Height > 0
            && (texture?.Width > aperture.Width || texture?.Height > aperture.Height
                || fittedSize != aperture.Size && Font.UnscaledFontsize < (float)(HudOverlaySizes.Text * context.IndicatorScale)))
        {
            var nextFont = TextMetrics.Font(strokeWidth)
                .WithOrientation(orientation)
                .WithFontSize((float)(HudOverlaySizes.Text * context.IndicatorScale))
                .WithStroke(new double[] { 0, 0, 0, .85 }, strokeWidth * context.IndicatorScale);
            CairoTextSizing.FitRectangle(nextFont, text, aperture.Size);
            if (nextFont.UnscaledFontsize != Font.UnscaledFontsize || nextFont.StrokeWidth != Font.StrokeWidth)
            {
                var replacement = create(context, text, nextFont)
                    ?? throw new InvalidOperationException("Native glyph preparation returned no texture.");
                var previous = texture;
                texture = replacement; Font = nextFont;
                if (!ReferenceEquals(previous, replacement)) previous?.Dispose();
            }
            fittedSize = aperture.Size;
        }
        CacheGeometry();
    }
    /// <summary>Keeps composition free of duplicate glyph generation or inherited bounds recalculation.</summary>
    public override void ComposeElements(Context ctx, ImageSurface surface) { }
    #endregion
    #region Rendering and lifetime
    /// <summary>Draws only cached glyph pixels and geometry; failures cannot abort sibling elements.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed && texture != null) State.Render(api.Render, draw);
    }
    /// <summary>Leaves passive content presses available to the existing HUD input owner.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases owned glyphs once even when the composer disposes dynamic elements twice.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var owned = texture; texture = null; context = null; naturalSize = SizeF.Empty;
        owned?.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Centers whole pixels without changing the raster's dimensions.</summary>
    private void CacheGeometry()
    {
        if (aperture.Width <= 0 || aperture.Height <= 0) { State.SetGeometry(RectangleF.Empty, clip); return; }
        float width = texture?.Width ?? 0, height = texture?.Height ?? 0;
        State.SetGeometry(new((float)Math.Round(aperture.X + (aperture.Width - width) / 2),
            (float)Math.Round(aperture.Y + (aperture.Height - height) / 2), width, height),
            RectangleF.Intersect(aperture, clip));
    }
    /// <summary>Submits prepared premultiplied glyphs inside the already-cached clip.</summary>
    private void DrawTexture()
    {
        var target = State.Target;
        api.Render.Render2DTexturePremultipliedAlpha(texture!.TextureId, target.X, target.Y, target.Width, target.Height, depth, State.Tint(true));
    }
    /// <summary>Generates native glyph pixels and applies the shared texture filtering policy.</summary>
    internal static LoadedTexture CreateTexture(HudOverlayPreparationContext context, string value, CairoFont font)
    {
        LoadedTexture texture;
        if (font.Orientation == EnumTextOrientation.Left)
        {
            texture = TextDrawing.CreateTexture(context.Api, value, font);
        }
        else
        {
            var pixels = CairoTextSizing.PixelSize(value, font);
            texture = TextDrawing.CreateTexture(context.Api, value, font, pixels.Width, pixels.Height,
                font.Orientation, paddingY: (int)Math.Ceiling(font.StrokeWidth));
        }
        try { return HudOverlayTextureFiltering.Prepare(texture); }
        catch { texture.Dispose(); throw; }
    }
    #endregion
}
