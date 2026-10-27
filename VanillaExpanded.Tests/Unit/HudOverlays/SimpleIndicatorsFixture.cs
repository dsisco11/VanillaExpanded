using System.Drawing;
using Moq;
using VanillaExpanded.BowAmmunition;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.RiftActivity;
using VanillaExpanded.TemporalStorm;
using VanillaExpanded.WindSpeed;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Supplies the four native consumers with tracked resource boundaries and deterministic localization.</summary>
internal sealed class SimpleIndicatorsFixture : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    private int nextId = 100;
    private readonly Dictionary<string, LoadedTexture> borrowed = new();

    #region Public API
    /// <summary>Gets real native composer services with mocked graphics uploads.</summary>
    public HudNativeComponentFixture Native { get; }
    /// <summary>Gets every element-owned glyph and arrow background texture.</summary>
    public List<LoadedTexture> Owned { get; } = new();
    /// <summary>Gets every borrowed or generated artwork texture.</summary>
    public List<LoadedTexture> Artwork { get; } = new();
    /// <summary>Gets the glyph values actually rasterized by the native elements.</summary>
    public List<string> Values { get; } = new();
    /// <summary>Gets the procedural circle submission boundary.</summary>
    public Mock<VanillaExpanded.HudOverlays.Rendering.IHudOverlayIconBackdrop> Backdrop { get; } = new();
    /// <summary>Gets observable CPU measurement calls.</summary>
    public int Measurements { get; private set; }
    /// <summary>Gets asset keys requested by native texture elements.</summary>
    public List<string> Paths { get; } = new();
    /// <summary>Creates isolated localization and native GUI scale state.</summary>
    public SimpleIndicatorsFixture(float scale = 1)
    {
        Native = new(scale);
        var translation = new Mock<ITranslationService>();
        translation.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] args) =>
            key == "vanillaexpanded:wind-speed-reading" ? args[0] + "%" :
            key == "vanillaexpanded:bow-ammunition-count" ? args[0].ToString()! :
            key == "vanillaexpanded:bow-ammunition-empty" ? "No arrows / 0" :
            key.StartsWith("vanillaexpanded:hud-short-") ? key[26..].ToUpperInvariant() :
            key.Split('-').Last().ToUpperInvariant());
        Lang.AvailableLanguages["en"] = translation.Object;
    }
    /// <summary>Creates stable sampled data and a production controller with its native capability.</summary>
    public (IHudOverlay Overlay, IndicatorPresentation Presentation, Action Prepare, Action Change) Create(string kind, HudIndicatorStyle style)
    {
        IndicatorPresentation presentation;
        IHudOverlay overlay;
        Action prepare, change;
        switch (kind)
        {
            case "wind":
                var wind = new WindSpeedPresentation(Measure, CreateText, Load);
                wind.SetContent(125); presentation = wind; overlay = new WindSpeedOverlay(wind);
                prepare = () => wind.Prepare(Native.Context); change = () => wind.SetContent(126);
                break;
            case "rift":
                var rift = new RiftActivityPresentation(Measure, CreateText, Load, _ => Backdrop.Object);
                rift.SetContent(RiftActivityLevel.High); presentation = rift; overlay = new RiftActivityOverlay(rift);
                prepare = () => rift.Prepare(Native.Context); change = () => rift.SetContent(RiftActivityLevel.Low);
                break;
            case "storm":
                var storm = new TemporalStormPresentation(Measure, CreateText, Load, _ => Backdrop.Object);
                storm.SetContent(TemporalStormStage.Imminent, .02); presentation = storm; overlay = new TemporalStormOverlay(storm);
                prepare = () => storm.Prepare(Native.Context); change = () => storm.SetContent(TemporalStormStage.Active, .5);
                break;
            case "bow":
                var bow = new BowAmmunitionPresentation(Measure, CreateText, CreateBackground);
                var arrow = new Item { Code = new AssetLocation("game:test-arrow") };
                bow.SetContent(new(new ItemStack(arrow, 12), 12)); presentation = bow;
                overlay = new BowAmmunitionOverlay(() => { }, bow);
                prepare = () => bow.Prepare(Native.Context); change = () => bow.SetContent(new(new ItemStack(arrow, 8), 8));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        presentation.SetStyle(style); prepare();
        return (overlay, presentation, prepare, change);
    }
    /// <summary>Measures supplied synthetic lettering independently of glyph resource allocation.</summary>
    public SizeF Measure(HudOverlayPreparationContext context, string text)
    {
        Measurements++;
        return string.IsNullOrEmpty(text) ? SizeF.Empty : new(text.Length * 7, 13);
    }
    /// <summary>Creates tracked owned glyphs whose dimensions match the supplied canonical metrics.</summary>
    public LoadedTexture CreateText(HudOverlayPreparationContext context, string value, CairoFont font)
    {
        Values.Add(value);
        var texture = new LoadedTexture(context.Api, nextId++, (int)(value.Length * 7 * context.GuiScale), (int)(13 * context.GuiScale));
        Owned.Add(texture); return texture;
    }
    /// <summary>Creates the arrow element's owned background independently for each visual instance.</summary>
    public LoadedTexture CreateBackground(HudOverlayPreparationContext context, float diameter)
    {
        var texture = new LoadedTexture(context.Api, nextId++, (int)(diameter * context.GuiScale), (int)(diameter * context.GuiScale));
        Owned.Add(texture); return texture;
    }
    /// <summary>Models owned SVG rasterization and engine-cached borrowed PNG resources.</summary>
    public TextureBinding Load(HudOverlayPreparationContext context, string path)
    {
        Paths.Add(path);
        bool owned = path.EndsWith(".svg");
        if (!owned && borrowed.TryGetValue(path, out var cached)) return new(cached, false);
        var texture = new LoadedTexture(context.Api, nextId++, 43, 47) { IgnoreUndisposed = !owned };
        Artwork.Add(texture);
        if (!owned) borrowed[path] = texture;
        return new(texture, owned);
    }
    /// <summary>Restores borrowed global localization and scale state.</summary>
    public void Dispose()
    {
        Native.Dispose();
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }
    #endregion
}
