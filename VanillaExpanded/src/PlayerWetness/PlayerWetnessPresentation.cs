using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Borrows slim droplet artwork and owns a percentage reading beneath two continuously filled drops.</summary>
internal sealed class PlayerWetnessPresentation : IDisposable
{
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> createTextures;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private readonly ElementBounds fillClip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private const float ReadingHeight = 8;
    private readonly HudOverlayIconTextPresentation text;
    private readonly ElementBounds textBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private float readingWidth;
    private readonly Vec4f fillTint = new(.25f, .65f, 1, 1);
    private LoadedTexture[]? textures;
    private IHudOverlayIconBackdrop? backdrop;
    private HudOverlayPreparationContext? context;
    private float wetness;

    #region Public API
    #region Content and preparation
    /// <summary>Controls entry, exit, and retained artwork lifetime.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Gets the base GUI footprint, independent of the game's scale.</summary>
    public SizeF Size { get; private set; }
    /// <summary>Accepts borrowed artwork loaders and an optional owned text presentation for headless verification.</summary>
    public PlayerWetnessPresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? createTextures = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudOverlayIconTextPresentation? text = null)
    {
        this.text = text ?? new HudOverlayIconTextPresentation(fontSize: 14);
        this.createTextures = createTextures ?? PlayerWetnessIconTextures.Create;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Updates continuous fill and the cached whole-percentage reading without regenerating icon artwork.</summary>
    public void SetContent(float value)
    {
        wetness = value;
        // Quantize only the reading; both drop fills retain the full sampled precision.
        int percentage = Math.Clamp((int)Math.Round(value * 100, MidpointRounding.AwayFromZero), 1, 100);
        text.SetContent(null, "vanillaexpanded:player-wetness-reading", percentage);
    }
    /// <summary>Loads cached artwork at the preparation boundary and reuses it for subsequent fills.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (textures == null || !ReferenceEquals(context?.Api, preparation.Api))
        {
            var replacement = createTextures(preparation);
            textures = replacement;
            backdrop = loadBackdrop(preparation);
        }
        context = preparation;
        text.Prepare(preparation);
        readingWidth = text.Size.Width * Math.Min(1, ReadingHeight / Math.Max(1, text.Size.Height));
        Size = new SizeF(Math.Max(24, readingWidth), 24);
        Visibility.Show();
    }
    #endregion
    #region Rendering
    /// <summary>Queues the shared backdrop separately from the foreground layers.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (context == null || textures == null) return;
        double pixels = 24 * context.GuiScale;
        backdrop?.Queue(renderer, bounds.renderX, bounds.renderY + (bounds.OuterHeight - pixels) / 2, context.GuiScale);
    }
    /// <summary>Draws the twin drops above a centered cached percentage within the shared indicator height.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip)
    {
        if (context == null || textures == null) return;
        double scale = context.GuiScale;
        double x = bounds.renderX + (Size.Width - 24) * scale / 2;
        double y = bounds.renderY + (bounds.OuterHeight - 24 * scale) / 2;
        DrawDrop(renderer, x + 1.5 * scale, y + scale, 15 * scale, clip);
        DrawDrop(renderer, x + 11.5 * scale, y, 9 * scale, clip);
        // Keep the normal-sized reading below the artwork without increasing the HUD row height.
        textBounds.absFixedX = bounds.renderX + (Size.Width - readingWidth) * scale / 2;
        textBounds.absFixedY = y + 16 * scale;
        textBounds.absInnerWidth = readingWidth * scale;
        textBounds.absInnerHeight = ReadingHeight * scale;
        textBounds.Initialized = true;
        text.Draw(renderer, textBounds, clip, 0, maximumTextHeight: ReadingHeight);
    }
    #endregion
    #region Lifecycle
    /// <summary>Retains the last fill while the exit fades.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Releases owned reading pixels and borrowed texture bindings at world exit or after an animated exit.</summary>
    public void Reset()
    {
        Visibility.Reset();
        text.Reset();
        textures = null; backdrop = null; context = null; wetness = 0; readingWidth = 0; Size = SizeF.Empty;
    }
    /// <summary>Releases all presentation bindings on final removal.</summary>
    public void Dispose()
    {
        Reset();
        text.Dispose();
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Reveals the same proportional fill independently in each drop and restores the host scissor.</summary>
    private void DrawDrop(IRenderAPI renderer, double x, double y, double pixels, RectangleF clip)
    {
        // The authored silhouette spans approximately y=10 through y=118 on its 128-pixel canvas.
        double top = Math.Max(y + pixels * (118d - 108d * wetness) / 128, clip.Top);
        double left = Math.Max(x, clip.Left), right = Math.Min(x + pixels, clip.Right), bottom = Math.Min(y + pixels, clip.Bottom);
        if (right > left && bottom > top)
        {
            fillClip.absFixedX = left; fillClip.absFixedY = top;
            fillClip.absInnerWidth = right - left; fillClip.absInnerHeight = bottom - top; fillClip.Initialized = true;
            fillTint.Set(.25f, .65f, 1, HudOverlayDrawOpacity.Current);
            renderer.PushScissor(fillClip, true);
            try { renderer.Render2DTexture(textures![1].TextureId, (float)x, (float)y, (float)pixels, (float)pixels, 50, fillTint); }
            finally { renderer.PopScissor(); }
        }
        renderer.Render2DTexture(textures![0].TextureId, (float)x, (float)y, (float)pixels, (float)pixels, 50, HudOverlayDrawOpacity.Tint);
    }
    #endregion
}
