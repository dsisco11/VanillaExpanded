using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Supplies an explicit external-consumer adapter without preparing or owning its controller.</summary>
internal sealed class LegacyView : NativeView
{
    private readonly LegacyElement element;

    #region Public API
    #region Preparation
    /// <summary>Creates a unique passive element borrowing an external overlay's prepared state.</summary>
    public LegacyView(ICoreClientAPI api, IHudOverlay overlay, NativeViewRole role, Action<Exception> failure) : base(api)
    {
        element = Own(new LegacyElement(api, ElementBounds.Fill, overlay, role, failure));
    }
    /// <summary>Leaves preparation to the existing scheduler rather than duplicating legacy lifecycle calls.</summary>
    public override void Prepare(HudOverlayPreparationContext context) { ThrowIfDisposed(); }
    /// <summary>Caches the resolved member clip for the compatibility boundary.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        ThrowIfDisposed(); element.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering state
    /// <summary>Uses the settled host opacity without asking legacy visibility to complete during rendering.</summary>
    public override void SetOpacity(float opacity)
    {
        ThrowIfDisposed(); element.State.SetOpacity(opacity);
    }
    /// <summary>Preserves legacy backdrop submission under the prepared opacity scope.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        ThrowIfDisposed(); element.CollectBackdrops(renderer);
    }
    /// <summary>Binds the borrowed overlay's drawing failures to its registration boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure) => element.State.Failure = failure;
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers the compatibility element distinctly from native component presentations.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.AddInteractiveElement(element, prefix + ":legacy");
    }
    #endregion
}
