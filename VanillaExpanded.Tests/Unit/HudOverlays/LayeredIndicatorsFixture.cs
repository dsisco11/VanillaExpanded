using System.Drawing;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.PlayerWetness;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Provides layered native consumers with deterministic text metrics and tracked borrowed texture bindings.</summary>
internal sealed class LayeredIndicatorsFixture : IDisposable
{
    private int nextId = 1000;

    #region Public API
    /// <summary>Gets native composition services and localized headless resources.</summary>
    public SimpleIndicatorsFixture Resources { get; }
    /// <summary>Gets the active native preparation context.</summary>
    public HudOverlayPreparationContext Context => Resources.Native.Context;
    /// <summary>Gets borrowed asset identities selected by the element loaders.</summary>
    public Dictionary<string, LoadedTexture> Bindings { get; } = new();
    /// <summary>Gets owned glyph resources allocated per native element.</summary>
    public List<LoadedTexture> Glyphs { get; } = new();
    /// <summary>Gets the actual localized glyph values prepared by elements.</summary>
    public List<string> Values { get; } = new();
    /// <summary>Overrides synthetic lettering dimensions for fit and measurement tests.</summary>
    public SizeF? TextSize { get; set; }
    /// <summary>Gets observable CPU measurement calls.</summary>
    public int Measurements { get; private set; }
    /// <summary>Gets whether the complete composite pack is available.</summary>
    public bool CompletePack { get; set; } = true;
    /// <summary>Gets the number of complete-pack checks outside native rendering.</summary>
    public int PackChecks { get; private set; }
    /// <summary>Creates independent native GUI scale and localization state.</summary>
    public LayeredIndicatorsFixture(float scale = 1) => Resources = new(scale);

    #region Feature and resource preparation
    /// <summary>Creates a real feature controller over seeded stable data without gameplay sampling.</summary>
    public (IHudOverlay Overlay, IndicatorPresentation Presentation, Action Prepare, Action Change) Create(string kind, HudIndicatorStyle style)
    {
        IHudOverlay overlay;
        IndicatorPresentation presentation;
        Action prepare, change;
        switch (kind)
        {
            case "wetness":
                var wetness = new PlayerWetnessPresentation(Measure, CreateText, Load, _ => Resources.Backdrop.Object);
                wetness.SetContent(.26f); presentation = wetness; overlay = new PlayerWetnessOverlay(wetness);
                prepare = () => wetness.Prepare(Context); change = () => wetness.SetContent(.27f);
                break;
            case "body":
                var body = new BodyTemperatureThermometerPresentation(Measure, CreateText, Load, _ => Resources.Backdrop.Object);
                body.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 2), true); presentation = body; overlay = new BodyTemperatureOverlay(body);
                prepare = () => body.Prepare(Context); change = () => body.SetContent(new(34.01f, 37, BodyTemperatureRisk.Cold, .35f, 2), true);
                break;
            case "weather":
                var weather = new WeatherConditionsPresentation(Measure, CreateText, Load, _ => Resources.Backdrop.Object, CheckPack);
                weather.SetContent(new(EnumPrecipitationType.Rain, true) { CloudCoverage = 4, PrecipitationIntensity = 2, FogIntensity = 2, ThunderIntensity = 1 });
                presentation = weather; overlay = new WeatherConditionsOverlay(weather); prepare = () => weather.Prepare(Context);
                change = () => weather.SetContent(new(EnumPrecipitationType.Snow, true) { CloudCoverage = 1, PrecipitationIntensity = 3, FogIntensity = 1, ThunderIntensity = 2 });
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        presentation.SetStyle(style); prepare();
        return (overlay, presentation, prepare, change);
    }
    /// <summary>Supplies synthetic metrics in GUI units without allocating duplicate glyph textures.</summary>
    public SizeF Measure(HudOverlayPreparationContext context, string text)
    {
        Measurements++; return string.IsNullOrEmpty(text) ? SizeF.Empty : Dimensions(text);
    }
    /// <summary>Allocates tracked element-owned glyph resources with dimensions matching the CPU metrics.</summary>
    public LoadedTexture CreateText(HudOverlayPreparationContext context, string text, CairoFont font)
    {
        Values.Add(text);
        var size = Dimensions(text);
        var texture = new LoadedTexture(context.Api, nextId++, (int)(size.Width * context.GuiScale), (int)(size.Height * context.GuiScale));
        Glyphs.Add(texture); return texture;
    }
    /// <summary>Borrows engine-like cached PNGs and records the selected asset paths.</summary>
    public TextureBinding Load(HudOverlayPreparationContext context, string path)
    {
        if (!Bindings.TryGetValue(path, out var texture))
        {
            texture = new LoadedTexture(context.Api, nextId++, 97, 103) { IgnoreUndisposed = true };
            Bindings.Add(path, texture);
        }
        return new(texture, false);
    }
    /// <summary>Records asset-pack detection independently of texture loading.</summary>
    public bool CheckPack(HudOverlayPreparationContext context) { PackChecks++; return CompletePack; }
    #endregion

    /// <summary>Restores borrowed global localization and native scale state.</summary>
    public void Dispose() => Resources.Dispose();
    #endregion

    #region Private
    /// <summary>Returns caller-supplied dimensions or a deterministic width derived from text content.</summary>
    private SizeF Dimensions(string value) => TextSize ?? new SizeF(value.Length * 7, 13);
    #endregion
}
