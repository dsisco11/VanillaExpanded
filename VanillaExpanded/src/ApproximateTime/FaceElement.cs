using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Owns the cached solar face and its shader subscription for one native composition.</summary>
internal sealed class FaceElement : GuiElement
{
    private readonly ApproximateTimeFaceArtwork? artwork;
    private readonly Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture> create;
    private readonly Action draw;
    private ApproximateTimeDayPhases? phases;
    private LoadedTexture? texture;
    private bool disposed;

    #region Public API
    /// <summary>Gets prepared target, clipping and opacity.</summary>
    public ElementState State { get; } = new(_ => { });
    #region Preparation
    /// <summary>Creates an independent GPU face owner or an injected texture owner.</summary>
    public FaceElement(ICoreClientAPI api, ElementBounds bounds,
        Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture>? create = null) : base(api, bounds)
    {
        if (create == null) artwork = new();
        this.create = create ?? artwork!.Create;
        draw = DrawTexture;
    }
    /// <summary>Updates face pixels only for changed solar metadata or shader invalidation.</summary>
    public void Prepare(HudOverlayPreparationContext context, ApproximateTimeDayPhases next)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(api, context.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(context));
        if (texture != null && next.Equals(phases) && artwork?.Dirty != true) return;
        var replacement = create(context, next);
        var previous = texture; texture = replacement; phases = next;
        // The production face helper owns its framebuffer texture; injected factories transfer ownership here.
        if (artwork == null && !ReferenceEquals(previous, replacement)) previous?.Dispose();
    }
    /// <summary>Caches the native full-canvas target and host clip before rendering.</summary>
    public void LayoutCompleted(RectangleF clip) => State.SetGeometry(new((float)Bounds.renderX,
        (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight), clip);
    #endregion
    #region Rendering and lifetime
    /// <summary>Submits cached pixels without running GPU composition or layout.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed && texture != null) State.Render(api.Render, draw);
    }
    /// <summary>Leaves mouse presses available to the HUD input host.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases owned face storage and shader subscriptions exactly once.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (artwork != null) artwork.Dispose(); else texture?.Dispose();
        texture = null; phases = null;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Draws the premultiplied solar face at its prepared canvas target.</summary>
    private void DrawTexture()
    {
        var target = State.Target;
        api.Render.Render2DTexturePremultipliedAlpha(texture!.TextureId, target.X, target.Y,
            target.Width, target.Height, 51, State.Tint(true));
    }
    #endregion
}
