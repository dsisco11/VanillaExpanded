using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Supplies native instrument compositions with independent tracked glyph, face, scale and mask resources.</summary>
internal sealed class InstrumentIndicatorsFixture : IDisposable
{
    private int nextId = 3000;

    #region Public API
    /// <summary>Gets the native composer and localization fixture.</summary>
    public SimpleIndicatorsFixture Resources { get; }
    /// <summary>Gets the canonical native preparation context.</summary>
    public HudOverlayPreparationContext Context => Resources.Native.Context;
    /// <summary>Gets generated clock-face identities, including in-place refreshes.</summary>
    public List<LoadedTexture> Faces { get; } = new();
    /// <summary>Gets generated calibration resources.</summary>
    public List<LoadedTexture> Ticks { get; } = new();
    /// <summary>Gets observed liquid upload targets.</summary>
    public List<LoadedTexture> Uploads { get; } = new();
    /// <summary>Gets source artwork decode calls.</summary>
    public int Decodes { get; private set; }
    /// <summary>Gets whether injected face creation refreshes the same texture in place.</summary>
    public bool ReuseFace { get; set; }
    /// <summary>Creates isolated scale state and captures native mask upload ownership.</summary>
    public InstrumentIndicatorsFixture(float scale = 1)
    {
        Resources = new(scale);
        Resources.Native.Render.Setup(value => value.LoadOrUpdateTextureFromBgra(It.IsAny<int[]>(), true, 0, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((int[] _, bool _, int _, ref LoadedTexture texture) =>
            {
                if (texture.TextureId == 0) texture.TextureId = nextId++;
                Uploads.Add(texture);
            }));
    }
    /// <summary>Creates real controllers over stable sampled data without native artwork generation in canonical preparation.</summary>
    public (IHudOverlay Overlay, InstrumentPresentation Presentation, Action Prepare, Action Change) Create(string kind, EnvironmentalGaugeStyle style)
    {
        IHudOverlay overlay;
        InstrumentPresentation presentation;
        Action prepare, change;
        switch (kind)
        {
            case "time":
                var time = new ApproximateTimePresentation(Resources.Measure, Resources.CreateText, Resources.Load, CreateFace);
                time.SetContent(ApproximateTimePeriod.Noon, .5, actualHour: 4.5);
                presentation = time; overlay = new ApproximateTimeOverlay(time);
                prepare = () => time.Prepare(Context); change = () => time.SetContent(ApproximateTimePeriod.Afternoon, .6, actualHour: 4.51);
                break;
            case "temperature":
                var temperature = new EnvironmentalTemperaturePresentation(Resources.Measure, Resources.CreateText, Resources.Load, CreateTicks, CreateLiquid);
                temperature.SetContent(12.1f); presentation = temperature; overlay = new EnvironmentalTemperatureOverlay(temperature);
                prepare = () => temperature.Prepare(Context); change = () => temperature.SetContent(12.4f);
                break;
            case "date":
                var date = new CalendarDatePresentation(Resources.Measure, Resources.CreateText, Resources.Load);
                date.SetContent(new(14, EnumMonth.October)); presentation = date; overlay = new CalendarDateOverlay(date);
                prepare = () => date.Prepare(Context); change = () => date.SetContent(new(15, EnumMonth.October));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        presentation.SetStyle(style); prepare();
        return (overlay, presentation, prepare, change);
    }
    /// <summary>Mounts one independent native generation through the production capability factory.</summary>
    public IndicatorComposition Mount(IHudOverlay overlay, InstrumentPresentation presentation, NativeViewRole role = NativeViewRole.Live) =>
        new(Resources.Native.Api.Object, ViewFactory.Create(overlay, Resources.Native.Api.Object, role, Resources.Native.Failures.Add), Context, presentation.Size);
    /// <summary>Releases isolated translation and GUI scale bindings.</summary>
    public void Dispose() => Resources.Dispose();
    #endregion

    #region Private
    /// <summary>Creates an element-owned cached face, optionally refreshing its identity in place.</summary>
    private LoadedTexture CreateFace(HudOverlayPreparationContext context, ApproximateTimeDayPhases phases)
    {
        var texture = ReuseFace && Faces.Count > 0 ? Faces[0] : new LoadedTexture(context.Api, nextId++, 512, 512);
        Faces.Add(texture); return texture;
    }
    /// <summary>Creates an independently owned generated calibration texture.</summary>
    private LoadedTexture CreateTicks(HudOverlayPreparationContext context)
    {
        var texture = new LoadedTexture(context.Api, nextId++, 512, 512); Ticks.Add(texture); return texture;
    }
    /// <summary>Creates a real reveal processor over supplied synthetic artwork without graphics filtering.</summary>
    private EnvironmentalTemperatureLiquidMask CreateLiquid() => new(_ =>
    {
        Decodes++; return Enumerable.Repeat(unchecked((int)0xff123456), 512 * 512).ToArray();
    }, texture => texture);
    /// <summary>Observes the native upload's ref target without replacing its ownership wrapper.</summary>
    private delegate void Upload(int[] pixels, bool linear, int clamp, ref LoadedTexture texture);
    #endregion
}
