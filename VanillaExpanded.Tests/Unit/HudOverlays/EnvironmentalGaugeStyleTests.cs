using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using EnumMonth = Vintagestory.API.Common.EnumMonth;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks typed live style selection, compact reading preservation, and native texture ownership.</summary>
[Collection("HudOverlayGeometry")]
public sealed class EnvironmentalGaugeStyleTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Provides normal Celsius and native month content without native language initialization.</summary>
    public EnvironmentalGaugeStyleTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => args.Length == 1 ? $"{args[0]}°C" : key == "month-October" ? "October" : key);
        Lang.AvailableLanguages["en"] = service.Object;
    }

    /// <summary>Restores global translation state for other HUD tests.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Both named JSON preferences and ConfigLib numeric events reach enum-valued config properties.</summary>
    [Theory]
    [InlineData("icon", EnvironmentalGaugeStyle.Icon)]
    [InlineData("1", EnvironmentalGaugeStyle.Icon)]
    [InlineData("immersive", EnvironmentalGaugeStyle.Immersive)]
    [InlineData("0", EnvironmentalGaugeStyle.Immersive)]
    public void ConfigLibAcceptsTypedStyles(string raw, EnvironmentalGaugeStyle expected)
    {
        var config = new VanillaExpandedConfig();
        var update = new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.CalendarDateGaugeStyle)),
            ["Value"] = new StringAttribute(raw)
        };
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, update));
        Assert.Equal(expected, config.CalendarDateGaugeStyle);
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.ApproximateTimeGaugeStyle);
    }

    /// <summary>Switching the clock preserves its last estimate and releases the owned face without deleting cached icons.</summary>
    [Fact]
    public void ClockSwitchesBothDirectionsWithoutResampling()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        var faces = new List<LoadedTexture>();
        int loads = 0;
        var compactPaths = new List<string>();
        var borrowed = new LoadedTexture(api.Object) { TextureId = 2, IgnoreUndisposed = true };
        using var compact = new HudOverlayIconReading(load: (_, path) => { compactPaths.Add(path); return borrowed; });
        using var presentation = new ApproximateTimePresentation(_ => { loads++; return [borrowed, borrowed, borrowed]; },
            createFace: (preparation, _) =>
            {
                var texture = new LoadedTexture(preparation.Api) { TextureId = 5 };
                faces.Add(texture); return texture;
            }, compact: compact);
        presentation.SetContent(ApproximateTimePeriod.Dusk, .75);
        presentation.Prepare(context);
        Assert.NotEqual(SizeF.Empty, presentation.Size);
        Assert.True(presentation.SetStyle(EnvironmentalGaugeStyle.Icon));
        Assert.True(faces[0].Disposed);
        presentation.Prepare(context); presentation.Prepare(context);
        Assert.NotEqual(SizeF.Empty, presentation.Size);
        Assert.Equal(new[] { "textures/hud/approximate-time/dusk.png" }, compactPaths);
        Assert.False(presentation.SetStyle(EnvironmentalGaugeStyle.Icon));
        presentation.SetStyle(EnvironmentalGaugeStyle.Immersive);
        presentation.Prepare(context);
        Assert.Equal(2, loads);
        Assert.Equal(2, faces.Count);
        Assert.False(borrowed.Disposed);
    }

    /// <summary>The compact thermometer retains an unclamped ordinary-size Celsius reading and does not load gauge layers.</summary>
    [Fact]
    public void TemperatureIconUsesCurrentReadingOnly()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var values = new List<string>();
        var paths = new List<string>();
        using var compact = CreateCompact(api.Object, values, paths);
        using var presentation = new EnvironmentalTemperaturePresentation(
            loadTextures: _ => throw new InvalidOperationException("Icon mode must not load instrument layers."), compact: compact);
        presentation.SetContent(-42.6f);
        presentation.SetStyle(EnvironmentalGaugeStyle.Icon);
        presentation.Prepare(new(api.Object, 1, "en"));
        Assert.NotEqual(SizeF.Empty, presentation.Size);
        Assert.Equal(new[] { "-43°C" }, values);
        Assert.Equal(new[] { "textures/hud/environmental-temperature/icon.svg" }, paths);
    }

    /// <summary>The compact calendar retains the three-letter localized month and native day without loading copper layers.</summary>
    [Fact]
    public void CalendarIconUsesMonthAndDayOnly()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var values = new List<string>();
        var paths = new List<string>();
        using var compact = CreateCompact(api.Object, values, paths);
        using var presentation = new CalendarDatePresentation(
            _ => throw new InvalidOperationException("Icon mode must not load instrument layers."), compact: compact);
        presentation.SetContent(new(14, EnumMonth.October));
        presentation.SetStyle(EnvironmentalGaugeStyle.Icon);
        presentation.Prepare(new(api.Object, 1, "en"));
        Assert.NotEqual(SizeF.Empty, presentation.Size);
        Assert.Equal(new[] { "OCT 14" }, values);
        Assert.Equal(new[] { "textures/hud/calendar-date/icon.svg" }, paths);
    }

    /// <summary>Compact resources remain cached and normal text dimensions track native GUI scaling.</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void CompactRenderingScalesAndDisposesOwnedSvg(double scale)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var owned = new LoadedTexture(api.Object) { TextureId = 5 };
        int loads = 0;
        using var compact = new HudOverlayIconReading(new WorkshopGaugeText((ctx, _) => new LoadedTexture(ctx.Api)
            { TextureId = 6, Width = (int)(35 * scale), Height = (int)(14 * scale) }),
            (_, _) => { loads++; return owned; });
        var context = new HudOverlayPreparationContext(api.Object, scale, "en");
        compact.Prepare(context, "icon.svg", "25°C"); compact.Prepare(context, "icon.svg", "25°C");
        var bounds = ElementBounds.Fixed(0, 0, 96, 36).WithEmptyParent();
        bounds.absInnerWidth = 96 * scale; bounds.absInnerHeight = 36 * scale; bounds.Initialized = true;
        var renderer = new Mock<IRenderAPI>();
        compact.Draw(renderer.Object, bounds);
        Assert.Equal(1, loads);
        var icon = Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture");
        Assert.True((double)icon.Arguments[1] < (double)renderer.Invocations.Last().Arguments[1]);
        var text = Assert.Single(renderer.Invocations, call => call.Method.Name == "Render2DTexturePremultipliedAlpha");
        Assert.True((double)text.Arguments[3] > 0);
        compact.Reset();
        Assert.True(owned.Disposed);
    }
    #endregion

    #region Private
    /// <summary>Captures prepared text and artwork paths while avoiding native graphics allocation.</summary>
    private static HudOverlayIconReading CreateCompact(ICoreClientAPI api, List<string> values, List<string> paths) =>
        new(new WorkshopGaugeText((context, content) =>
        {
            values.Add(content);
            return new LoadedTexture(context.Api) { TextureId = 9, Width = content.Length * 7, Height = 14 };
        }), (_, path) =>
        {
            paths.Add(path);
            return new LoadedTexture(api) { TextureId = 8 };
        });
    #endregion
}
