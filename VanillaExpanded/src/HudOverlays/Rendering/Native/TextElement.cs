using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Owns native contrast glyphs with prepared fitting and per-frame fade tint.</summary>
internal sealed class TextElement : GuiElementTextBase
{
    private readonly Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture> create;
    private readonly Action draw;
    private LoadedTexture? texture;
    private HudOverlayPreparationContext? context;
    private RectangleF aperture, clip;
    private bool hasLayout, disposed;
    private readonly double strokeWidth;

    #region Public API
    /// <summary>Gets pre-render clip, target, tint and failure state.</summary>
    public ElementState State { get; }
    /// <summary>Gets intrinsic glyph size in unscaled GUI units.</summary>
    public SizeF Size => texture == null || context == null ? SizeF.Empty
        : new((float)(texture.Width / context.GuiScale), (float)(texture.Height / context.GuiScale));
    #region Preparation
    /// <summary>Creates a passive native text element with an optional headless glyph factory.</summary>
    public TextElement(ICoreClientAPI api, ElementBounds bounds, Action<Exception> failure,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? create = null, double strokeWidth = 1)
        : base(api, string.Empty, TextMetrics.Font(strokeWidth), bounds)
    {
        this.create = create ?? CreateTexture;
        this.strokeWidth = strokeWidth;
        State = new(failure);
        draw = DrawTexture;
    }
    /// <summary>Replaces glyphs only when characters or preparation context change.</summary>
    public void Prepare(HudOverlayPreparationContext preparation, string? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(preparation);
        if (!ReferenceEquals(api, preparation.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(preparation));
        value ??= string.Empty;
        if (context == preparation && text == value) return;
        CairoFont nextFont = context == preparation ? Font : TextMetrics.Font(strokeWidth);
        // Allocate before replacing the surviving resource so generation failure preserves cleanup ownership.
        LoadedTexture? replacement = value.Length == 0 ? null : create(preparation, value, nextFont)
            ?? throw new InvalidOperationException("Native glyph preparation returned no texture.");
        LoadedTexture? previous = texture;
        texture = replacement; text = value; context = preparation; Font = nextFont;
        if (!ReferenceEquals(previous, replacement)) previous?.Dispose();
        if (hasLayout) CacheFit();
    }
    /// <summary>Caches native bounds and fits glyphs once after changed layout.</summary>
    public void LayoutCompleted(RectangleF effectiveClip)
    {
        aperture = new((float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight);
        clip = effectiveClip; hasLayout = true;
        CacheFit();
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
        var owned = texture; texture = null; context = null;
        owned?.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Prepares fitting without enlarging the normal font or modifying component bounds.</summary>
    private void CacheFit()
    {
        double fit = texture == null ? 0 : Math.Min(1,
            Math.Min(aperture.Width / Math.Max(1, texture.Width), aperture.Height / Math.Max(1, texture.Height)));
        float width = (float)((texture?.Width ?? 0) * fit), height = (float)((texture?.Height ?? 0) * fit);
        State.SetGeometry(new(aperture.X + (aperture.Width - width) / 2, aperture.Y + (aperture.Height - height) / 2, width, height), clip);
    }
    /// <summary>Submits prepared premultiplied glyphs inside the already-cached clip.</summary>
    private void DrawTexture()
    {
        var target = State.Target;
        api.Render.Render2DTexturePremultipliedAlpha(texture!.TextureId, target.X, target.Y, target.Width, target.Height, 51, State.Tint(true));
    }
    /// <summary>Generates native glyph pixels and applies the shared texture filtering policy.</summary>
    private static LoadedTexture CreateTexture(HudOverlayPreparationContext context, string value, CairoFont font)
    {
        var texture = context.Api.Gui.TextTexture.GenTextTexture(value, font);
        try { return HudOverlayTextureFiltering.Prepare(texture); }
        catch { texture.Dispose(); throw; }
    }
    #endregion
}
