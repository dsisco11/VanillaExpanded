using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Composes a radial empty/full thermometer with dynamic calibration and foreground clamps.</summary>
internal sealed class EnvironmentalTemperaturePresentation : IDisposable
{
    private readonly WorkshopGaugeText text;
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> loadTextures;
    private readonly Func<HudOverlayPreparationContext, LoadedTexture> createTicks;
    private readonly EnvironmentalTemperatureLiquidMask liquid;
    private LoadedTexture[]? textures;
    private LoadedTexture? ticks;
    private HudOverlayPreparationContext? context;
    private float temperature;
    private readonly HudOverlayIconReading compact;
    private EnvironmentalGaugeStyle style = EnvironmentalGaugeStyle.Immersive;

    #region Public API
    /// <summary>Gets the shared fade transition and retained presentation lifetime.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Gets the bounded liquid fill; the numeric reading remains unclamped.</summary>
    internal float MarkerPosition => EnvironmentalTemperatureGaugeLayout.Fill(temperature);
    /// <summary>Gets the complete registered canvas footprint in GUI units.</summary>
    public SizeF Size { get; private set; }

    #region Content and rendering
    /// <summary>Accepts optional owned text and tick factories plus a borrowed artwork loader.</summary>
    public EnvironmentalTemperaturePresentation(WorkshopGaugeText? text = null,
        Func<HudOverlayPreparationContext, LoadedTexture[]>? loadTextures = null,
        Func<HudOverlayPreparationContext, LoadedTexture>? createTicks = null,
        EnvironmentalTemperatureLiquidMask? liquid = null, HudOverlayIconReading? compact = null)
    {
        this.text = text ?? new();
        this.loadTextures = loadTextures ?? (preparation => WorkshopGaugeArtwork.Load(preparation,
            RadialGaugeLayout.Backing, "thermometer/empty-glass", "thermometer/clamps",
            "thermometer/label-minus40", "thermometer/label-zero", "thermometer/label-plus60", RadialGaugeLayout.Glass));
        this.createTicks = createTicks ?? EnvironmentalTemperatureScaleArtwork.Create;
        this.liquid = liquid ?? new();
        this.compact = compact ?? new();
    }

    /// <summary>Releases the previous style's resources while preserving the sampled climate reading.</summary>
    public bool SetStyle(EnvironmentalGaugeStyle value)
    {
        EnvironmentalGaugeStyle next = value == EnvironmentalGaugeStyle.Icon ? value : EnvironmentalGaugeStyle.Immersive;
        if (next == style) return false;
        compact.Reset(); text.Reset(); liquid.Reset(); ticks?.Dispose(); ticks = null;
        textures = null; context = null; Size = SizeF.Empty; style = next;
        return true;
    }

    /// <summary>Stores the current climate reading independently of clock estimation.</summary>
    public void SetContent(float celsius) => temperature = celsius;

    /// <summary>Prepares cached whole-degree text and runtime tick pixels outside drawing.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        double rounded = Math.Round(temperature, MidpointRounding.AwayFromZero);
        string reading = Lang.GetL(preparation.Locale, "vanillaexpanded:environmental-temperature-reading", rounded == 0 ? 0d : rounded);
        if (style == EnvironmentalGaugeStyle.Icon)
        {
            compact.Prepare(preparation, "textures/hud/environmental-temperature/icon.svg", reading);
            context = preparation; Size = compact.Size; Visibility.Show();
            return;
        }
        bool apiChanged = !ReferenceEquals(context?.Api, preparation.Api);
        if (textures == null || apiChanged) textures = loadTextures(preparation);
        if (ticks == null || apiChanged)
        {
            var replacement = createTicks(preparation);
            ticks?.Dispose(); ticks = replacement;
        }
        text.SetContent(reading);
        text.Prepare(preparation);
        liquid.Prepare(preparation, MarkerPosition);
        context = preparation;
        Size = new SizeF(HudOverlaySizes.EnvironmentalThermometer, HudOverlaySizes.EnvironmentalThermometer);
        Visibility.Show();
    }

    /// <summary>Draws angularly masked liquid, calibrated scale elements, and foreground clamps in sandwich order.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip)
    {
        if (context == null) return;
        if (style == EnvironmentalGaugeStyle.Icon) { compact.Draw(renderer, bounds); return; }
        if (textures == null || ticks == null) return;
        float width = (float)(HudOverlaySizes.EnvironmentalThermometer * context.GuiScale), height = width;
        var screen = new RectangleF((float)bounds.renderX, (float)(bounds.renderY + (bounds.OuterHeight - height) / 2), width, height);
        var mechanism = EnvironmentalTemperatureGaugeLayout.MechanismBounds(screen);
        renderer.RenderTexture(textures[0].TextureId, screen.X, screen.Y, width, height, 50, HudOverlayDrawOpacity.Tint);
        // All thermometer parts use one inset transform; only the shared housing and glass use the full canvas.
        renderer.RenderTexture(textures[1].TextureId, mechanism.X, mechanism.Y, mechanism.Width, mechanism.Height, 50.5f, HudOverlayDrawOpacity.Tint);
        liquid.Draw(renderer, mechanism);
        renderer.Render2DTexturePremultipliedAlpha(ticks.TextureId, mechanism.X, mechanism.Y, mechanism.Width, mechanism.Height, 52, HudOverlayDrawOpacity.PremultipliedTint);
        for (int index = 0; index < 3; index++)
        {
            float degree = index == 0 ? -40 : index == 1 ? 0 : 60;
            float labelWidth = index == 1 ? 64 : 128;
            var center = EnvironmentalTemperatureGaugeLayout.Position(degree, 128);
            var label = WorkshopGaugeArtwork.Map(mechanism, EnvironmentalTemperatureGaugeLayout.Canvas,
                new RectangleF(center.X - labelWidth / 2, center.Y - 32, labelWidth, 64));
            renderer.RenderTexture(textures[index + 3].TextureId, label.X, label.Y, label.Width, label.Height, 52, HudOverlayDrawOpacity.Tint);
        }
        text.Draw(renderer, WorkshopGaugeArtwork.Map(mechanism, EnvironmentalTemperatureGaugeLayout.Canvas,
            EnvironmentalTemperatureGaugeLayout.Reading), 52);
        renderer.RenderTexture(textures[2].TextureId, mechanism.X, mechanism.Y, mechanism.Width, mechanism.Height, 53, HudOverlayDrawOpacity.Tint);
        renderer.RenderTexture(textures[6].TextureId, screen.X, screen.Y, width, height, 54, HudOverlayDrawOpacity.Tint);
    }

    #endregion
    #region Lifetime
    /// <summary>Retains the reading until the shared exit transition completes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Releases owned text and ticks while dropping engine-owned artwork bindings.</summary>
    public void Reset()
    {
        Visibility.Reset();
        compact.Reset();
        text.Reset();
        liquid.Reset();
        ticks?.Dispose(); ticks = null;
        textures = null;
        context = null;
        temperature = 0;
        Size = SizeF.Empty;
    }
    /// <summary>Releases the presentation's owned resources at final removal.</summary>
    public void Dispose()
    {
        Reset();
        compact.Dispose();
        text.Dispose();
    }
    #endregion
    #endregion
}
