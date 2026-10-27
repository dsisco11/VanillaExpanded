using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Registers the ammunition count above a native arrow element with independent ownership.</summary>
internal sealed class ArrowView : NativeView
{
    private readonly BowAmmunitionPresentation presentation;
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterMiddle);
    private readonly TextElement reading;
    private readonly ArrowElement arrow;

    #region Public API
    #region Composition and preparation
    /// <summary>Creates native components without inventory sampling or event subscriptions.</summary>
    public ArrowView(ICoreClientAPI api, BowAmmunitionPresentation presentation,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText,
        Func<HudOverlayPreparationContext, float, LoadedTexture>? createBackground) : base(api)
    {
        this.presentation = presentation;
        reading = Own(new TextElement(api, ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterTop), _ => { }, createText));
        try { arrow = Own(new ArrowElement(api, ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterTop), createBackground)); }
        catch { Dispose(); throw; }
    }
    /// <summary>Transfers prepared data and declares component sizes before native layout.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed();
        reading.Prepare(context, presentation.Reading); arrow.Prepare(context, presentation.Icon);
        var text = presentation.ReadingSize;
        bool hasArrow = presentation.Icon != null;
        double iconSize = hasArrow ? HudOverlaySizes.Small : 0;
        double iconY = text.Height + (hasArrow ? HudOverlaySizes.ReadingGap : 0);
        bool changed = root.fixedWidth != presentation.Size.Width || root.fixedHeight != presentation.Size.Height
            || reading.Bounds.fixedWidth != text.Width || reading.Bounds.fixedHeight != text.Height
            || arrow.Bounds.fixedWidth != iconSize || arrow.Bounds.fixedY != iconY;
        root.fixedWidth = presentation.Size.Width; root.fixedHeight = presentation.Size.Height;
        reading.Bounds.fixedWidth = text.Width; reading.Bounds.fixedHeight = text.Height;
        arrow.Bounds.fixedWidth = arrow.Bounds.fixedHeight = iconSize;
        arrow.Bounds.fixedY = iconY;
        if (changed) root.MarkDirtyRecursive();
    }
    /// <summary>Caches fitting and item clipping after resolved native bounds.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        reading.LayoutCompleted(clip); arrow.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering state
    /// <summary>Applies the same host fade to lettering, circle and native item tint.</summary>
    public override void SetOpacity(float opacity)
    {
        reading.State.SetOpacity(opacity); arrow.SetOpacity(opacity);
    }
    /// <summary>The arrow element owns its circle rather than using the procedural backdrop batch.</summary>
    public override void CollectBackdrops(IRenderAPI renderer) { }
    /// <summary>Routes both component failures through the existing feature boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        reading.State.Failure = arrow.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Mounts real text and item elements under their parented native bounds.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.AddInteractiveElement(reading, prefix + ":reading");
            composer.AddInteractiveElement(arrow, prefix + ":arrow");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion
}
