using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Registers the calibrated thermometer mechanism, reading and foreground within native housing bounds.</summary>
internal sealed class ClimateView : NativeView
{
    private readonly EnvironmentalTemperaturePresentation content;
    private readonly ElementBounds root, mechanism;
    private readonly TextureElement housing, empty, ticks, clamps, glass;
    private readonly TextureElement[] labels = new TextureElement[3];
    private readonly LiquidElement liquid;
    private readonly TextElement reading;

    #region Public API
    #region Preparation
    /// <summary>Creates independent generated ticks, liquid and glyphs while borrowing the authored PNG layers.</summary>
    public ClimateView(ICoreClientAPI api, EnvironmentalTemperaturePresentation content,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText,
        Func<HudOverlayPreparationContext, LoadedTexture>? createTicks,
        Func<EnvironmentalTemperatureLiquidMask>? createLiquid) : base(api)
    {
        this.content = content;
        root = ElementBounds.Fixed(0, 0, content.Size.Width, content.Size.Height).WithAlignment(EnumDialogArea.LeftMiddle);
        // All calibrated regions share one inset native parent, including the reveal, labels and reading.
        float inset = EnvironmentalTemperatureGaugeLayout.MechanismScale;
        mechanism = ElementBounds.Percentual(RadialGaugeLayout.CenterX / RadialGaugeLayout.Canvas - inset / 2,
            RadialGaugeLayout.CenterY / RadialGaugeLayout.Canvas - inset / 2, inset, inset);
        housing = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load));
        try
        {
            empty = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load, depth: 50.5f));
            liquid = Own(new LiquidElement(api, ElementBounds.Fill, createLiquid));
            ticks = Own(new TextureElement(api, ElementBounds.Fill, _ => { },
                (context, _) => new((createTicks ?? EnvironmentalTemperatureScaleArtwork.Create)(context), true), depth: 52, premultiplied: true));
            for (int index = 0; index < labels.Length; index++)
            {
                float degree = index == 0 ? -40 : index == 1 ? 0 : 60;
                float width = index == 1 ? 64 : 128;
                var center = EnvironmentalTemperatureGaugeLayout.Position(degree, 128);
                labels[index] = Own(new TextureElement(api, Region(new(center.X - width / 2, center.Y - 32, width, 64)), _ => { }, load, depth: 52));
            }
            reading = Own(new TextElement(api, Region(EnvironmentalTemperatureGaugeLayout.Reading), _ => { }, createText, depth: 52));
            clamps = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load, depth: 53));
            glass = Own(new TextureElement(api, ElementBounds.Fill, _ => { }, load, depth: 54));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates resource content without altering calibrated component geometry.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed();
        const string prefix = "textures/hud/workshop-gauges/";
        housing.Prepare(context, prefix + "radial/layer-0.png");
        empty.Prepare(context, prefix + "thermometer/empty-glass.png");
        liquid.Prepare(context, content.MarkerPosition);
        ticks.Prepare(context, "generated:thermometer-ticks");
        labels[0].Prepare(context, prefix + "thermometer/label-minus40.png");
        labels[1].Prepare(context, prefix + "thermometer/label-zero.png");
        labels[2].Prepare(context, prefix + "thermometer/label-plus60.png");
        reading.Prepare(context, content.Reading);
        clamps.Prepare(context, prefix + "thermometer/clamps.png");
        glass.Prepare(context, prefix + "radial/layer-2.png");
    }
    /// <summary>Caches every resolved canvas, aperture and clipping target before rendering.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        housing.LayoutCompleted(clip); empty.LayoutCompleted(clip); liquid.LayoutCompleted(clip); ticks.LayoutCompleted(clip);
        foreach (var label in labels) label.LayoutCompleted(clip);
        reading.LayoutCompleted(clip); clamps.LayoutCompleted(clip); glass.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Applies host opacity to each mechanical layer without regenerating pixels.</summary>
    public override void SetOpacity(float opacity)
    {
        housing.State.SetOpacity(opacity); empty.State.SetOpacity(opacity); liquid.State.SetOpacity(opacity); ticks.State.SetOpacity(opacity);
        foreach (var label in labels) label.State.SetOpacity(opacity);
        reading.State.SetOpacity(opacity); clamps.State.SetOpacity(opacity); glass.State.SetOpacity(opacity);
    }
    /// <summary>The authored radial housing needs no procedural backdrop.</summary>
    public override void CollectBackdrops(IRenderAPI renderer) { }
    /// <summary>Routes each independent component through the feature error boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        housing.State.Failure = empty.State.Failure = liquid.State.Failure = ticks.State.Failure = failure;
        foreach (var label in labels) label.State.Failure = failure;
        reading.State.Failure = clamps.State.Failure = glass.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers native component bounds in the established mechanical layer order.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.AddInteractiveElement(housing, prefix + ":housing");
            composer.BeginChildElements(mechanism);
            try
            {
                composer.AddInteractiveElement(empty, prefix + ":empty");
                composer.AddInteractiveElement(liquid, prefix + ":liquid");
                composer.AddInteractiveElement(ticks, prefix + ":ticks");
                for (int index = 0; index < labels.Length; index++) composer.AddInteractiveElement(labels[index], prefix + ":label" + index);
                composer.AddInteractiveElement(reading, prefix + ":reading");
                composer.AddInteractiveElement(clamps, prefix + ":clamps");
            }
            finally { composer.EndChildElements(); }
            composer.AddInteractiveElement(glass, prefix + ":glass");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion

    #region Private
    /// <summary>Expresses authored calibration regions relative to their native mechanism parent.</summary>
    private static ElementBounds Region(RectangleF region) => ElementBounds.Percentual(region.X / EnvironmentalTemperatureGaugeLayout.Canvas.Width,
        region.Y / EnvironmentalTemperatureGaugeLayout.Canvas.Height, region.Width / EnvironmentalTemperatureGaugeLayout.Canvas.Width,
        region.Height / EnvironmentalTemperatureGaugeLayout.Canvas.Height);
    #endregion
}
