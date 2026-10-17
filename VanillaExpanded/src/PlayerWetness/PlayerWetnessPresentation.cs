using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using Vintagestory.API.Config;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Borrows slim droplet artwork and owns a percentage reading beneath two fully filled drops.</summary>
internal sealed class PlayerWetnessPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> createTextures;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private const float ReadingHeight = HudOverlaySizes.WetnessReadingHeight;
    private readonly HudOverlayIconTextPresentation text;
    private readonly ElementBounds textBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private float readingWidth;
    private int percentage;
    private readonly Vec4f fillTint = new(.25f, .65f, 1, 1);
    private LoadedTexture[]? textures;
    private IHudOverlayIconBackdrop? backdrop;

    #region Public API
    #region Content and preparation
    /// <summary>Accepts borrowed artwork loaders and an optional owned text presentation for headless verification.</summary>
    public PlayerWetnessPresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? createTextures = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudOverlayIconTextPresentation? text = null, HudIndicatorTextPresentation? textStyle = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, textStyle)
    {
        this.text = text ?? new HudOverlayIconTextPresentation(fontSize: HudOverlaySizes.Text);
        this.createTextures = createTextures ?? PlayerWetnessIconTextures.Create;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Updates the cached whole-percentage reading without regenerating icon artwork.</summary>
    public void SetContent(float value)
    {
        // The percentage carries intensity while the artwork remains fully filled.
        percentage = Math.Clamp((int)Math.Round(value * 100, MidpointRounding.AwayFromZero), 1, 100);
        text.SetContent(null, "vanillaexpanded:player-wetness-reading", percentage);
    }
    /// <summary>Loads cached artwork at the preparation boundary and reuses it for subsequent readings.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "wetness", Lang.GetL(preparation.Locale, "vanillaexpanded:player-wetness-reading", percentage));
            return;
        }
        if (textures == null || !ReferenceEquals(Context?.Api, preparation.Api))
        {
            var replacement = createTextures(preparation);
            textures = replacement;
            backdrop = loadBackdrop(preparation);
        }
        Context = preparation;
        text.SetContent(null, "vanillaexpanded:player-wetness-reading", percentage);
        text.Prepare(preparation);
        readingWidth = text.Size.Width * Math.Min(1, ReadingHeight / Math.Max(1, text.Size.Height));
        Size = new SizeF(Math.Max(HudOverlaySizes.Standard, readingWidth), HudOverlaySizes.Standard);
        Visibility.Show();
    }
    #endregion
    #region Rendering
    /// <summary>Queues the shared backdrop separately from the foreground layers.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (Context == null || textures == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        backdrop?.Queue(renderer, bounds.renderX, bounds.renderY + (bounds.OuterHeight - pixels) / 2, Context.GuiScale);
    }
    /// <summary>Draws the twin drops above a centered cached percentage within the shared indicator height.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip)
    {
        if (Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (textures == null) return;
        double scale = Context.GuiScale;
        double x = bounds.renderX + (Size.Width - HudOverlaySizes.Standard) * scale / 2;
        double y = bounds.renderY + (bounds.OuterHeight - HudOverlaySizes.Standard * scale) / 2;
        DrawDrop(renderer, x + 2.25 * scale, y + 1.5 * scale, 22.5 * scale);
        DrawDrop(renderer, x + 17.25 * scale, y, 13.5 * scale);
        // Keep the normal-sized reading below the artwork without increasing the HUD row height.
        textBounds.absFixedX = bounds.renderX + (Size.Width - readingWidth) * scale / 2;
        textBounds.absFixedY = y + 24 * scale;
        textBounds.absInnerWidth = readingWidth * scale;
        textBounds.absInnerHeight = ReadingHeight * scale;
        textBounds.Initialized = true;
        text.Draw(renderer, textBounds, clip, 0, maximumTextHeight: ReadingHeight);
    }
    #endregion
    #region Lifecycle
    /// <summary>Ends shared presentation ownership and disposes feature-owned helpers.</summary>
    public override void Dispose()
    {
        base.Dispose();
        text.Dispose();
    }
    #endregion
    #endregion
    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        text.Reset(); textures = null; backdrop = null;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        readingWidth = 0; percentage = 0;
    }
    #endregion

    #region Private
    /// <summary>Draws the complete blue silhouette beneath its outline; the host supplies clipping.</summary>
    private void DrawDrop(IRenderAPI renderer, double x, double y, double pixels)
    {
        fillTint.Set(.25f, .65f, 1, HudOverlayDrawOpacity.Current);
        renderer.Render2DTexture(textures![1].TextureId, (float)x, (float)y, (float)pixels, (float)pixels, 50, fillTint);
        renderer.Render2DTexture(textures![0].TextureId, (float)x, (float)y, (float)pixels, (float)pixels, 50, HudOverlayDrawOpacity.Tint);
    }
    #endregion


}
