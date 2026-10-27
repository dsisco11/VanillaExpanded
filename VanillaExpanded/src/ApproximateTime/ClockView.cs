using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Registers the complete solar face, independently rotating hand and foreground glass.</summary>
internal sealed class ClockView : NativeView
{
    private readonly ApproximateTimePresentation content;
    private readonly ElementBounds root;
    private readonly FaceElement face;
    private readonly PointerElement pointer;
    private readonly TextureElement glass;

    #region Public API
    #region Preparation
    /// <summary>Creates separate composer-owned resources over one controller's sampled solar data.</summary>
    public ClockView(ICoreClientAPI api, ApproximateTimePresentation content,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load,
        Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture>? createFace) : base(api)
    {
        this.content = content;
        root = ElementBounds.Fixed(0, 0, content.Size.Width, content.Size.Height).WithAlignment(EnumDialogArea.LeftMiddle);
        face = Own(new FaceElement(api, ElementBounds.Fill, createFace));
        try
        {
            pointer = Own(new PointerElement(api, ElementBounds.Fill, load));
            glass = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load, depth: 53));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Prepares changed solar pixels and rotation without resampling the calendar.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed();
        face.Prepare(context, content.Phases); pointer.Prepare(context, content.Rotation);
        glass.Prepare(context, "textures/hud/workshop-gauges/radial/layer-2.png");
    }
    /// <summary>Caches native canvas targets and the pointer spindle before either render pass.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        face.LayoutCompleted(clip); pointer.LayoutCompleted(clip); glass.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Uses the same host fade for face, shadow, hand and glass.</summary>
    public override void SetOpacity(float opacity)
    {
        face.State.SetOpacity(opacity); pointer.State.SetOpacity(opacity); glass.State.SetOpacity(opacity);
    }
    /// <summary>The authored radial housing needs no procedural backdrop.</summary>
    public override void CollectBackdrops(IRenderAPI renderer) { }
    /// <summary>Isolates drawing failures to this feature.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        face.State.Failure = pointer.State.Failure = glass.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers individual native layers in face, pointer and glass order.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.AddInteractiveElement(face, prefix + ":face");
            composer.AddInteractiveElement(pointer, prefix + ":pointer");
            composer.AddInteractiveElement(glass, prefix + ":glass");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion
}
