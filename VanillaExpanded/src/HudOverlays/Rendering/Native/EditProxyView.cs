using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Mounts a host-owned hidden-item proxy without calling the feature's view factory.</summary>
internal sealed class EditProxyView : NativeView
{
    private readonly EditProxyElement element;

    #region Public API
    /// <summary>Allocates distinct proxy resources for each surviving composer.</summary>
    public EditProxyView(ICoreClientAPI api, HudOverlayEditPreviews previews, HudOverlayRegistration registration,
        Action<Exception> failure) : base(api) =>
        element = Own(new EditProxyElement(api, previews.CreateLettering(), () => previews.Caption(registration), failure));
    /// <summary>Updates the proxy identity independently of feature preparation.</summary>
    public override void Prepare(HudOverlayPreparationContext context) => element.Prepare(context);
    /// <summary>Caches the proxy's fitted geometry after native layout.</summary>
    public override void LayoutCompleted(RectangleF clip) => element.LayoutCompleted(clip);
    /// <summary>Applies the same host drag multiplier used by ordinary artwork.</summary>
    public override void SetOpacity(float opacity) => element.State.SetOpacity(opacity);
    /// <summary>Leaves hidden feature backdrops absent.</summary>
    public override void CollectBackdrops(IRenderAPI renderer) { }
    /// <summary>Keeps proxy errors within the host boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure) => element.State.Failure = failure;
    #endregion

    #region Protected API
    /// <summary>Registers the passive proxy with its owning native composer.</summary>
    protected override void AddElements(GuiComposer composer, string prefix) =>
        composer.AddInteractiveElement(element, prefix + ":proxy");
    #endregion
}
