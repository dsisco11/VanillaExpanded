using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Rotates the clock hand and its shadow around a prepared spindle without arranging components.</summary>
internal sealed class PointerElement : GuiElement
{
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding> load;
    private readonly Action draw;
    private readonly Vec4f tint = new(1, 1, 1, 1), shadow = new(0, 0, 0, 1);
    private TextureBinding? binding;
    private double pivotX, pivotY, shadowX, shadowY;
    private float degrees;
    private bool disposed;

    #region Public API
    /// <summary>Gets the cached canvas, clipping and host opacity.</summary>
    public ElementState State { get; } = new(_ => { });
    #region Preparation
    /// <summary>Creates a passive pointer with borrowed artwork and independent visual state.</summary>
    public PointerElement(ICoreClientAPI api, ElementBounds bounds,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load) : base(api, bounds)
    {
        this.load = load ?? ((context, _) => new(WorkshopGaugeArtwork.Load(context, "clock/layer-1")[0], false));
        draw = DrawPointer;
    }
    /// <summary>Loads artwork once and updates only the estimated rotation for new readings.</summary>
    public void Prepare(HudOverlayPreparationContext context, float rotation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(api, context.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(context));
        binding ??= load(context, "textures/hud/workshop-gauges/clock/layer-1.png");
        degrees = rotation * 180 / MathF.PI;
    }
    /// <summary>Caches full-canvas bounds, spindle and fixed lighting offset after native layout.</summary>
    public void LayoutCompleted(RectangleF clip)
    {
        State.SetGeometry(new((float)Bounds.renderX, (float)Bounds.renderY,
            (float)Bounds.OuterWidth, (float)Bounds.OuterHeight), clip);
        pivotX = State.Target.Width * RadialGaugeLayout.CenterX / RadialGaugeLayout.Canvas;
        pivotY = State.Target.Height * RadialGaugeLayout.CenterY / RadialGaugeLayout.Canvas;
        shadowX = State.Target.Width * 6 / RadialGaugeLayout.Canvas;
        shadowY = State.Target.Height * 8 / RadialGaugeLayout.Canvas;
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Applies the visual rotation using cached geometry and restores render state on failure.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed && binding != null) State.Render(api.Render, draw);
    }
    /// <summary>Leaves passive mouse presses untouched.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases only explicitly owned injected artwork and tolerates repeated native disposal.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (binding is { Owned: true } owned) owned.Texture.Dispose();
        binding = null;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Draws the screen-lit shadow before the hand with the same estimated angle.</summary>
    private void DrawPointer()
    {
        tint.W = State.Opacity; shadow.W = State.Opacity * .35f;
        DrawLayer(shadowX, shadowY, shadow, 51.5f);
        DrawLayer(0, 0, tint, 52);
    }
    /// <summary>Rotates about the cached spindle and restores the caller's matrix even if submission fails.</summary>
    private void DrawLayer(double offsetX, double offsetY, Vec4f color, float depth)
    {
        api.Render.GlPushMatrix();
        try
        {
            api.Render.GlTranslate(State.Target.X + pivotX + offsetX, State.Target.Y + pivotY + offsetY, 0d);
            api.Render.GlRotate(degrees, 0, 0, 1);
            api.Render.RenderTexture(binding!.Value.Texture.TextureId, -pivotX, -pivotY,
                State.Target.Width, State.Target.Height, depth, color);
        }
        finally { api.Render.GlPopMatrix(); }
    }
    #endregion
}
