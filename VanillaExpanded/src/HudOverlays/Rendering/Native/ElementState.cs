using System;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Caches a component's framebuffer clip, draw target, tint and failure boundary.</summary>
internal sealed class ElementState
{
    private readonly ElementBounds scissor = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private readonly Vec4f straight = new(1, 1, 1, 1), premultiplied = new(1, 1, 1, 1);
    private bool clipped;

    #region Public API
    /// <summary>Gets the resolved target; rendering never derives component geometry.</summary>
    public RectangleF Target { get; private set; }
    /// <summary>Gets the current host opacity.</summary>
    public float Opacity { get; private set; } = 1;
    /// <summary>Gets whether prepared geometry and opacity allow a draw or backdrop submission.</summary>
    public bool Drawable => clipped && Opacity > 0 && Target.Width > 0 && Target.Height > 0;
    /// <summary>Receives a component failure without interrupting sibling rendering.</summary>
    public Action<Exception> Failure { get; set; }
    /// <summary>Creates an explicit feature or host error boundary.</summary>
    public ElementState(Action<Exception> failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }
    /// <summary>Updates cached framebuffer geometry before either rendering pass.</summary>
    public void SetGeometry(RectangleF target, RectangleF clip)
    {
        if (!Finite(target) || !Finite(clip)) throw new ArgumentOutOfRangeException(nameof(clip));
        Target = target;
        scissor.absFixedX = clip.X; scissor.absFixedY = clip.Y;
        scissor.absInnerWidth = clip.Width; scissor.absInnerHeight = clip.Height;
        scissor.Initialized = true;
        clipped = clip.Width > 0 && clip.Height > 0;
    }
    /// <summary>Updates reusable straight and premultiplied tints independently of cached pixels.</summary>
    public void SetOpacity(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        Opacity = value;
        straight.W = value;
        premultiplied.X = premultiplied.Y = premultiplied.Z = premultiplied.W = value;
    }
    /// <summary>Returns the cached tint appropriate for the texture's alpha convention.</summary>
    public Vec4f? Tint(bool isPremultiplied) => Opacity == 1 ? null : isPremultiplied ? premultiplied : straight;
    /// <summary>Draws through an intersecting, exception-safe scissor without arranging content.</summary>
    public void Render(IRenderAPI renderer, Action draw)
    {
        if (!Drawable) return;
        try
        {
            renderer.PushScissor(scissor, true);
            try { draw(); }
            finally { renderer.PopScissor(); }
        }
        catch (Exception error) { Failure(error); }
    }
    #endregion

    #region Private
    /// <summary>Rejects malformed geometry before it can enter native clipping or drawing.</summary>
    private static bool Finite(RectangleF value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Width) && float.IsFinite(value.Height) && value.Width >= 0 && value.Height >= 0;
    #endregion
}
