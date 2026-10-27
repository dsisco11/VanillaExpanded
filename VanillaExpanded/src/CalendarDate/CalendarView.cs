using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.CalendarDate;

/// <summary>Registers roller lettering between independently owned native housing and glass elements.</summary>
internal sealed class CalendarView : NativeView
{
    private readonly CalendarDatePresentation content;
    private readonly ElementBounds root;
    private readonly TextureElement housing, glass;
    private readonly TextElement month, day;

    #region Public API
    #region Preparation
    /// <summary>Creates passive components using the complete authored canvas and its registered windows.</summary>
    public CalendarView(ICoreClientAPI api, CalendarDatePresentation content,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText) : base(api)
    {
        this.content = content;
        root = ElementBounds.Fixed(0, 0, content.Size.Width, content.Size.Height).WithAlignment(EnumDialogArea.LeftMiddle);
        housing = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load));
        try
        {
            month = Own(new TextElement(api, Window(0), _ => { }, createText));
            day = Own(new TextElement(api, Window(1), _ => { }, createText));
            glass = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load, depth: 52));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates changed characters and borrowed assets without modifying window geometry.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed();
        housing.Prepare(context, "textures/hud/workshop-gauges/calendar/layer-0.png");
        month.Prepare(context, content.Month); day.Prepare(context, content.Day);
        glass.Prepare(context, "textures/hud/workshop-gauges/calendar/layer-2.png");
    }
    /// <summary>Caches fitted lettering and canvas geometry after native resolution.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        housing.LayoutCompleted(clip); month.LayoutCompleted(clip); day.LayoutCompleted(clip); glass.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Applies one host fade to every layer without regenerating pixels.</summary>
    public override void SetOpacity(float opacity)
    {
        housing.State.SetOpacity(opacity); month.State.SetOpacity(opacity); day.State.SetOpacity(opacity); glass.State.SetOpacity(opacity);
    }
    /// <summary>The authored housing needs no procedural backdrop.</summary>
    public override void CollectBackdrops(IRenderAPI renderer) { }
    /// <summary>Routes component failures through the owning feature boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        housing.State.Failure = month.State.Failure = day.State.Failure = glass.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers housing, native windows and glass in mechanical sandwich order.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.AddInteractiveElement(housing, prefix + ":housing");
            composer.AddInteractiveElement(month, prefix + ":month");
            composer.AddInteractiveElement(day, prefix + ":day");
            composer.AddInteractiveElement(glass, prefix + ":glass");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion

    #region Private
    /// <summary>Declares an aperture relative to the authored canvas; native bounds apply scaling and position.</summary>
    private static ElementBounds Window(int index)
    {
        var window = CalendarDatePresentation.Windows[index];
        return ElementBounds.Percentual(window.X / CalendarDatePresentation.Canvas.Width,
            window.Y / CalendarDatePresentation.Canvas.Height, window.Width / CalendarDatePresentation.Canvas.Width,
            window.Height / CalendarDatePresentation.Canvas.Height);
    }
    #endregion
}
