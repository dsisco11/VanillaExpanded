using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Borrows droplet artwork and draws a continuously filled wetness icon inside one status footprint.</summary>
internal sealed class PlayerWetnessPresentation : IDisposable
{
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> createTextures;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private readonly ElementBounds fillClip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
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
    /// <summary>Accepts optional headless artwork and backdrop factories.</summary>
    public PlayerWetnessPresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? createTextures = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
    {
        this.createTextures = createTextures ?? PlayerWetnessIconTextures.Create;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Updates only the cached fill, without allocating artwork for each wetness change.</summary>
    public void SetContent(float value) => wetness = value;
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
        Size = new SizeF(24, 24);
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
    /// <summary>Clips the texture fill from the bottom upward and restores scissor state after drawing.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip)
    {
        if (context == null || textures == null) return;
        double pixels = 24 * context.GuiScale, x = bounds.renderX, y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        // The PNG droplet spans approximately y=10 through y=120 on its 128-pixel canvas.
        double top = Math.Max(y + pixels * (120d - 110d * wetness) / 128, clip.Top);
        double left = Math.Max(x, clip.Left), right = Math.Min(x + pixels, clip.Right), bottom = Math.Min(y + pixels, clip.Bottom);
        if (right > left && bottom > top)
        {
            fillClip.absFixedX = left; fillClip.absFixedY = top;
            fillClip.absInnerWidth = right - left; fillClip.absInnerHeight = bottom - top; fillClip.Initialized = true;
            float opacity = HudOverlayDrawOpacity.Current;
            fillTint.Set(.25f, .65f, 1, opacity);
            renderer.PushScissor(fillClip, true);
            try { renderer.Render2DTexture(textures[1].TextureId, (float)x, (float)y, (float)pixels, (float)pixels, 50, fillTint); }
            finally { renderer.PopScissor(); }
        }
        renderer.Render2DTexture(textures[0].TextureId, (float)x, (float)y, (float)pixels, (float)pixels, 50, HudOverlayDrawOpacity.Tint);
    }
    #endregion
    #region Lifecycle
    /// <summary>Retains the last fill while the exit fades.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Releases borrowed texture bindings immediately at world exit or after an animated exit.</summary>
    public void Reset()
    {
        Visibility.Reset();
        textures = null; backdrop = null; context = null; wetness = 0; Size = SizeF.Empty;
    }
    /// <summary>Releases all presentation bindings on final removal.</summary>
    public void Dispose() => Reset();
    #endregion
    #endregion
}



