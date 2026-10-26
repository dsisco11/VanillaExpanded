using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Adapts an external legacy Draw callback while borrowing its controller lifetime.</summary>
internal sealed class LegacyElement : GuiElement
{
    private readonly IHudOverlay overlay;
    private readonly bool sourceTrace;
    private readonly Action draw;
    private RectangleF clip;
    private float deltaTime;
    private bool disposed;

    #region Public API
    /// <summary>Gets the adapter's prepared clip, opacity and failure boundary.</summary>
    public ElementState State { get; }
    #region Preparation
    /// <summary>Creates an explicitly legacy passive callback without acquiring the controller.</summary>
    public LegacyElement(ICoreClientAPI api, ElementBounds bounds, IHudOverlay overlay,
        NativeViewRole role, Action<Exception> failure) : base(api, bounds)
    {
        this.overlay = overlay; sourceTrace = role == NativeViewRole.SourceTrace;
        State = new(failure); draw = DrawLegacy;
    }
    /// <summary>Captures the effective clip before legacy drawing and backdrop submission.</summary>
    public void LayoutCompleted(RectangleF effectiveClip)
    {
        clip = effectiveClip;
        State.SetGeometry(new((float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight), clip);
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Delegates prepared legacy content under scoped clipping and opacity.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (disposed) return;
        this.deltaTime = sourceTrace ? 0 : deltaTime;
        State.Render(api.Render, draw);
    }
    /// <summary>Submits legacy backdrop geometry with the same host-prepared opacity.</summary>
    public void CollectBackdrops(IRenderAPI renderer)
    {
        if (disposed || !State.Drawable || overlay is not IHudOverlayBackdropProvider provider) return;
        using var opacity = new DrawOpacity(State.Opacity);
        provider.CollectBackdrops(renderer, Bounds);
    }
    /// <summary>Leaves passive artwork presses available to the existing native input owner.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Retires only this callback; registry ownership of the borrowed overlay is unchanged.</summary>
    public override void Dispose() => disposed = true;
    #endregion
    #endregion

    #region Private
    /// <summary>Invokes the legacy drawing contract without reevaluating transition lifetime during drawing.</summary>
    private void DrawLegacy()
    {
        using var opacity = new DrawOpacity(State.Opacity);
        overlay.Draw(api.Render, Bounds, clip, deltaTime);
    }
    #endregion
}
