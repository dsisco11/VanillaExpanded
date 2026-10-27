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



    #endregion
}
