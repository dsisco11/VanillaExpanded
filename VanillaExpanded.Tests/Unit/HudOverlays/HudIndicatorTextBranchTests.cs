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



    /// <summary>Unknown cloud coverage remains explicit while all other weather channels retain their intensities.</summary>
    [Fact]
    public void UnknownCloudCoverageDoesNotEraseOtherWeatherChannels()
    {
        Assert.Equal("CLD? SNOW3 FOG1 THR3", WeatherConditionsText.Format(new(EnumPrecipitationType.Snow, true)
            { PrecipitationIntensity = 3, FogIntensity = 1, ThunderIntensity = 3 }));
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
        Assert.Equal(11, labels.Length);
        Assert.All(labels, value => Assert.InRange(value.Value.Value<string>()!.Length, 3, 5));
    }
    #endregion

}
