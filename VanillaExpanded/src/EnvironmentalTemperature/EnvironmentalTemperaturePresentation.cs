using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Composes a radial empty/full thermometer with dynamic calibration and foreground clamps.</summary>
internal sealed class EnvironmentalTemperaturePresentation : HudIndicatorPresentation<EnvironmentalGaugeStyle>
{
    private readonly WorkshopGaugeText text;
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> loadTextures;
    private readonly Func<HudOverlayPreparationContext, LoadedTexture> createTicks;
    private readonly EnvironmentalTemperatureLiquidMask liquid;
    private LoadedTexture[]? textures;
    private LoadedTexture? ticks;
    private float temperature;
    private readonly HudOverlayIconReading compact;

    #region Public API
    /// <summary>Gets the bounded liquid fill; the numeric reading remains unclamped.</summary>
    internal float MarkerPosition => EnvironmentalTemperatureGaugeLayout.Fill(temperature);

    #region Content and rendering
    /// <summary>Accepts optional owned text and tick factories plus a borrowed artwork loader.</summary>
    public EnvironmentalTemperaturePresentation(WorkshopGaugeText? text = null,
        Func<HudOverlayPreparationContext, LoadedTexture[]>? loadTextures = null,
        Func<HudOverlayPreparationContext, LoadedTexture>? createTicks = null,
        EnvironmentalTemperatureLiquidMask? liquid = null, HudOverlayIconReading? compact = null,
        HudIndicatorTextPresentation? textStyle = null)
        : base(EnvironmentalGaugeStyle.Immersive, EnvironmentalGaugeStyle.Text, textStyle)
    {
        this.text = text ?? new();
        this.loadTextures = loadTextures ?? (preparation => WorkshopGaugeArtwork.Load(preparation,
            RadialGaugeLayout.Backing, "thermometer/empty-glass", "thermometer/clamps",
            "thermometer/label-minus40", "thermometer/label-zero", "thermometer/label-plus60", RadialGaugeLayout.Glass));
        this.createTicks = createTicks ?? EnvironmentalTemperatureScaleArtwork.Create;
        this.liquid = liquid ?? new();
        this.compact = compact ?? new();
    }


    /// <summary>Stores the current climate reading independently of clock estimation.</summary>
    public void SetContent(float celsius) => temperature = celsius;

    /// <summary>Prepares cached whole-degree text and runtime tick pixels outside drawing.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        double rounded = Math.Round(temperature, MidpointRounding.AwayFromZero);
        string reading = Lang.GetL(preparation.Locale, "vanillaexpanded:environmental-temperature-reading", rounded == 0 ? 0d : rounded);
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "temperature", reading);
            return;
        }
        if (Style == EnvironmentalGaugeStyle.Icon)
        {
            compact.Prepare(preparation, "textures/hud/environmental-temperature/icon.svg", reading);
            Context = preparation; Size = compact.Size; Visibility.Show();
            return;
        }
        bool apiChanged = !ReferenceEquals(Context?.Api, preparation.Api);
        if (textures == null || apiChanged) textures = loadTextures(preparation);
        if (ticks == null || apiChanged)
        {
            var replacement = createTicks(preparation);
            ticks?.Dispose(); ticks = replacement;
        }
        text.SetContent(reading);
        text.Prepare(preparation);
        liquid.Prepare(preparation, MarkerPosition);
        Context = preparation;
        Size = new SizeF(HudOverlaySizes.EnvironmentalThermometer, HudOverlaySizes.EnvironmentalThermometer);
        Visibility.Show();
    }

    /// <summary>Draws angularly masked liquid, calibrated scale elements, and foreground clamps in sandwich order.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip)
    {
        if (Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (Style == EnvironmentalGaugeStyle.Icon) { compact.Draw(renderer, bounds); return; }
        if (textures == null || ticks == null) return;
        float width = (float)(HudOverlaySizes.EnvironmentalThermometer * Context.GuiScale), height = width;
        var screen = new RectangleF((float)bounds.renderX, (float)(bounds.renderY + (bounds.OuterHeight - height) / 2), width, height);
        var mechanism = EnvironmentalTemperatureGaugeLayout.MechanismBounds(screen);
        renderer.RenderTexture(textures[0].TextureId, screen.X, screen.Y, width, height, 50, DrawOpacity.Tint);
        // All thermometer parts use one inset transform; only the shared housing and glass use the full canvas.
        renderer.RenderTexture(textures[1].TextureId, mechanism.X, mechanism.Y, mechanism.Width, mechanism.Height, 50.5f, DrawOpacity.Tint);
        liquid.Draw(renderer, mechanism);
        renderer.Render2DTexturePremultipliedAlpha(ticks.TextureId, mechanism.X, mechanism.Y, mechanism.Width, mechanism.Height, 52, DrawOpacity.PremultipliedTint);
        for (int index = 0; index < 3; index++)
        {
            float degree = index == 0 ? -40 : index == 1 ? 0 : 60;
            float labelWidth = index == 1 ? 64 : 128;
            var center = EnvironmentalTemperatureGaugeLayout.Position(degree, 128);
            var label = WorkshopGaugeArtwork.Map(mechanism, EnvironmentalTemperatureGaugeLayout.Canvas,
                new RectangleF(center.X - labelWidth / 2, center.Y - 32, labelWidth, 64));
            renderer.RenderTexture(textures[index + 3].TextureId, label.X, label.Y, label.Width, label.Height, 52, DrawOpacity.Tint);
        }
        text.Draw(renderer, WorkshopGaugeArtwork.Map(mechanism, EnvironmentalTemperatureGaugeLayout.Canvas,
            EnvironmentalTemperatureGaugeLayout.Reading), 52);
        renderer.RenderTexture(textures[2].TextureId, mechanism.X, mechanism.Y, mechanism.Width, mechanism.Height, 53, DrawOpacity.Tint);
        renderer.RenderTexture(textures[6].TextureId, screen.X, screen.Y, width, height, 54, DrawOpacity.Tint);
    }

    #endregion
    #region Lifetime
    /// <summary>Ends shared presentation ownership and disposes feature-owned helpers.</summary>
    public override void Dispose()
    {
        base.Dispose();
        compact.Dispose(); text.Dispose();
    }
    #endregion
    #endregion


    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        compact.Reset(); text.Reset(); liquid.Reset(); ticks?.Dispose(); ticks = null; textures = null;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        temperature = 0;
    }
    #endregion
}
