using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Dispatches built-in presentations that still supply the manual drawing contract.</summary>
internal sealed class DispatchView : NativeView
{
    private readonly HudOverlayGroupElement element;
    private readonly HudOverlayMemberLayout member;
    private float opacity;

    #region Public API
    /// <summary>Retains the existing manual dispatcher for presentations without native components.</summary>
    public DispatchView(ICoreClientAPI api, HudOverlayMemberLayout member, Action<float> draw) : base(api)
    {
        this.member = member;
        element = Own(new HudOverlayGroupElement(api, ElementBounds.Fill, dt =>
        {
            if (opacity > 0) draw(dt);
        }));
    }
    /// <summary>Leaves built-in preparation with the scheduler.</summary>
    public override void Prepare(HudOverlayPreparationContext context) { ThrowIfDisposed(); }
    /// <summary>Leaves manual presentation geometry with the existing dispatcher.</summary>
    public override void LayoutCompleted(RectangleF clip) { }
    /// <summary>Gates dispatch using the host's settled frame opacity.</summary>
    public override void SetOpacity(float opacity) => this.opacity = opacity;
    /// <summary>Preserves the existing built-in backdrop callback and opacity contract.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        if (opacity <= 0 || member.Registration.Overlay is not IHudOverlayBackdropProvider provider) return;
        using var scope = new DrawOpacity(opacity);
        provider.CollectBackdrops(renderer, member.Bounds);
    }
    /// <summary>Leaves dispatch errors with the host's existing per-member boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure) { }
    #endregion

    #region Protected API
    /// <summary>Registers the existing dispatch element distinctly from native component views.</summary>
    protected override void AddElements(GuiComposer composer, string prefix) =>
        composer.AddInteractiveElement(element, prefix + ":dispatch");
    #endregion
}
