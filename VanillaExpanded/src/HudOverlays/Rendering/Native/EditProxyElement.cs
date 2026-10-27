using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Owns independent edit-only lettering and caches its fitted target before rendering.</summary>
internal sealed class EditProxyElement : GuiElement
{
    private readonly WorkshopGaugeText text;
    private readonly Func<string> caption;
    private readonly Action draw;
    private bool disposed;

    #region Public API
    /// <summary>Gets cached drawing geometry and the host error boundary.</summary>
    public ElementState State { get; }
    /// <summary>Creates passive proxy artwork without borrowing feature resources.</summary>
    public EditProxyElement(ICoreClientAPI api, WorkshopGaugeText text, Func<string> caption,
        Action<Exception> failure) : base(api, ElementBounds.Fill)
    {
        this.text = text; this.caption = caption;
        State = new(failure); draw = DrawText;
    }
    /// <summary>Updates only host-owned proxy characters and resources.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        text.SetContent(caption()); text.Prepare(context);
    }
    /// <summary>Fits lettering once after native bounds resolve.</summary>
    public void LayoutCompleted(RectangleF clip) => State.SetGeometry(text.Fit(new(
        (float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight)), clip);
    /// <summary>Draws cached lettering inside the prepared intersecting clip.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed) State.Render(api.Render, draw);
    }
    /// <summary>Leaves pointer presses to the host editing controls.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases only this composition's owned lettering, tolerating duplicate native disposal.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; text.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Submits prepared proxy geometry under its settled host opacity.</summary>
    private void DrawText() => text.DrawAt(api.Render, State.Target, 50, State.Tint(true));
    #endregion
}
