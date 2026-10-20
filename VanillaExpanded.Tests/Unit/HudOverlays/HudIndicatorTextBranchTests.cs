using Newtonsoft.Json.Linq;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.BowAmmunition;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.PlayerWetness;
using VanillaExpanded.RiftActivity;
using VanillaExpanded.TemporalStorm;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using EnumMonth = Vintagestory.API.Common.EnumMonth;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks every registered indicator's artwork-free text presentation and preserved sampled content.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudIndicatorTextBranchTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Supplies deterministic short labels and formatted readings without client language initialization.</summary>
    public HudIndicatorTextBranchTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] args) =>
            key.StartsWith("vanillaexpanded:hud-short-") ? key[26..] : key == "month-October" ? "October" :
            args.Length == 1 ? args[0] + (key.Contains("wetness") ? "%" : "°C") : key);
        Lang.AvailableLanguages["en"] = language.Object;
    }

    /// <summary>Restores shared language state after every text-branch fixture.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>All eight presentation classes emit a short label and sampled reading while artwork loaders remain unused.</summary>
    [Theory]
    [InlineData("time", "18:00")]
    [InlineData("date", "OCT 14")]
    [InlineData("environmental-temperature", "-43°C")]
    [InlineData("body-temperature", "37°C COLD +2")]
    [InlineData("wetness", "43%")]
    [InlineData("rift", "HIGH")]
    [InlineData("storm", "IMMINENT")]
    [InlineData("weather", "CLD4 RAIN2 WND3 FOG2 THR1")]
    public void TextBranchSkipsArtworkAndRetainsReading(string label, string expected)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        var values = new List<string>();
        using var text = CaptureText(values);
        switch (label)
        {
            case "time":
                using (var presentation = new ApproximateTimePresentation(load: FailArtwork, textStyle: text))
                { presentation.SetContent(ApproximateTimePeriod.Dusk, .75); presentation.SetStyle(EnvironmentalGaugeStyle.Text); presentation.Prepare(context); }
                break;
            case "date":
                using (var presentation = new CalendarDatePresentation(load: FailArtwork, textStyle: text))
                { presentation.SetContent(new(14, EnumMonth.October)); presentation.SetStyle(EnvironmentalGaugeStyle.Text); presentation.Prepare(context); }
                break;
            case "environmental-temperature":
                using (var presentation = new EnvironmentalTemperaturePresentation(loadTextures: FailArtwork, textStyle: text))
                { presentation.SetContent(-42.6f); presentation.SetStyle(EnvironmentalGaugeStyle.Text); presentation.Prepare(context); }
                break;
            case "body-temperature":
                using (var presentation = new BodyTemperatureThermometerPresentation(new(), createIcons: FailArtwork, textStyle: text))
                { presentation.SetContent(new(37, 37, BodyTemperatureRisk.Cold, Trend: 2), true); presentation.SetStyle(HudIndicatorStyle.Text); presentation.Prepare(context); }
                break;
            case "wetness":
                using (var presentation = new PlayerWetnessPresentation(createTextures: FailArtwork, textStyle: text))
                { presentation.SetContent(.426f); presentation.SetStyle(HudIndicatorStyle.Text); presentation.Prepare(context); }
                break;
            case "rift":
                using (var presentation = new RiftActivityPresentation(load: FailArtwork, textStyle: text))
                { presentation.SetContent(RiftActivityLevel.High); presentation.SetStyle(HudIndicatorStyle.Text); presentation.Prepare(context); }
                break;
            case "storm":
                using (var presentation = new TemporalStormPresentation(load: FailArtwork, textStyle: text))
                { presentation.SetContent(TemporalStormStage.Imminent, .01); presentation.SetStyle(HudIndicatorStyle.Text); presentation.Prepare(context); }
                break;
            case "weather":
                using (var presentation = new WeatherConditionsPresentation(load: FailArtwork, textStyle: text))
                {
                    presentation.SetContent(new(EnumPrecipitationType.Rain, true)
                        { CloudCoverage = 4, PrecipitationIntensity = 2, WindIntensity = 3, FogIntensity = 2, ThunderIntensity = 1 });
                    presentation.SetStyle(HudIndicatorStyle.Text); presentation.Prepare(context);
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(label));
        }
        Assert.Equal(new[] { label == "environmental-temperature" ? "temperature" : label, expected }, values);
    }

    /// <summary>The ninth indicator shows its synchronized ammunition quantity without preparing either icon presentation.</summary>
    [Fact]
    public void AmmunitionTextShowsSampledQuantityWithoutArtwork()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); fixture.Entity.Alive = true;
        var values = new List<string>();
        using var text = CaptureText(values);
        using var overlay = new BowAmmunitionOverlay(() => { }, textStyle: text);
        overlay.SetStyle(HudIndicatorStyle.Text);
        overlay.BeginSession(fixture.Api.Object); overlay.Refresh();
        Assert.NotNull(overlay.Sample);
        overlay.Prepare(new(fixture.Api.Object, 1, "en"));
        Assert.Equal(new[] { "ammunition", overlay.Sample.Quantity.ToString() }, values);
    }
    /// <summary>Unknown cloud coverage remains explicit while all other weather channels retain their intensities.</summary>
    [Fact]
    public void UnknownCloudCoverageDoesNotEraseOtherWeatherChannels()
    {
        Assert.Equal("CLD? SNOW3 WND2 FOG1 THR3", WeatherConditionsText.Format(new(EnumPrecipitationType.Snow, true)
            { PrecipitationIntensity = 3, WindIntensity = 2, FogIntensity = 1, ThunderIntensity = 3 }));
    }

    /// <summary>Every production short label is intentionally three to five characters rather than a full indicator name.</summary>
    [Fact]
    public void ProductionShortLabelsAreCompact()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        var language = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(root!.FullName,
            "VanillaExpanded/assets/vanillaexpanded/lang/en.json")));
        var labels = language.Properties().Where(value => value.Name.StartsWith("hud-short-")).ToArray();
        Assert.Equal(9, labels.Length);
        Assert.All(labels, value => Assert.InRange(value.Value.Value<string>()!.Length, 3, 5));
    }
    #endregion

    #region Private
    /// <summary>Rejects accidental icon or immersive artwork preparation from a text-only branch.</summary>
    private static LoadedTexture[] FailArtwork(HudOverlayPreparationContext _) => throw new InvalidOperationException("Text mode loaded artwork.");

    /// <summary>Captures both prepared strings as owned headless text textures.</summary>
    private static HudIndicatorTextPresentation CaptureText(List<string> values)
    {
        LoadedTexture Create(HudOverlayPreparationContext context, string value)
        {
            values.Add(value);
            return new LoadedTexture(context.Api) { TextureId = 1, Width = value.Length * 7, Height = 14 };
        }
        return new(new WorkshopGaugeText(Create), new WorkshopGaugeText(Create));
    }
    #endregion
}


