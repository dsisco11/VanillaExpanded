using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Owns one angular liquid reveal and renders it using prepared native mechanism bounds.</summary>
internal sealed class LiquidElement : GuiElement
{
    private readonly EnvironmentalTemperatureLiquidMask liquid;
    private readonly Action draw;
    private bool disposed;

    #region Public API
    /// <summary>Gets cached mechanism geometry, clipping and opacity.</summary>
    public ElementState State { get; } = new(_ => { });
    /// <summary>Creates an independent generated mask for one composer.</summary>
    public LiquidElement(ICoreClientAPI api, ElementBounds bounds, Func<EnvironmentalTemperatureLiquidMask>? create) : base(api, bounds)
    {
        liquid = create?.Invoke() ?? new(); draw = DrawLiquid;
    }
    /// <summary>Updates reveal pixels only when bounded fill changes.</summary>
    public void Prepare(HudOverlayPreparationContext context, float fill)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(api, context.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(context));
        liquid.Prepare(context, fill);
    }
    /// <summary>Caches the resolved mechanism rectangle before rendering.</summary>
    public void LayoutCompleted(RectangleF clip) => State.SetGeometry(new((float)Bounds.renderX,
        (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight), clip);
    /// <summary>Submits prepared masked pixels without changing geometry or allocating textures.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed) State.Render(api.Render, draw);
    }
    /// <summary>Leaves presses available to the HUD input host.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases mask storage once even when native disposal repeats.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; liquid.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Applies this visual instance's host fade to the prepared reveal.</summary>
    private void DrawLiquid()
    {
        using var opacity = new DrawOpacity(State.Opacity);
        liquid.Draw(api.Render, State.Target);
    }
    #endregion
}
