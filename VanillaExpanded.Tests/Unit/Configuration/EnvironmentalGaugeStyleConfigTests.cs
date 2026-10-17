using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaExpanded.HudOverlays.Configuration;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Checks independent environmental instrument choices and their persisted enum contract.</summary>
[Trait("Category", "Unit")]
public sealed class EnvironmentalGaugeStyleConfigTests
{
    #region Public API
    /// <summary>All three instruments retain the detailed presentation when no setting is supplied.</summary>
    [Fact]
    public void DefaultsAreImmersive()
    {
        var config = new VanillaExpandedConfig();
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.ApproximateTimeGaugeStyle);
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.EnvironmentalTemperatureGaugeStyle);
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.CalendarDateGaugeStyle);
    }

    /// <summary>Selecting one compact instrument does not alter either adjacent setting.</summary>
    [Theory]
    [InlineData(nameof(VanillaExpandedConfig.ApproximateTimeGaugeStyle))]
    [InlineData(nameof(VanillaExpandedConfig.EnvironmentalTemperatureGaugeStyle))]
    [InlineData(nameof(VanillaExpandedConfig.CalendarDateGaugeStyle))]
    public void IconSelectionIsIndependent(string selected)
    {
        var config = new VanillaExpandedConfig();
        typeof(VanillaExpandedConfig).GetProperty(selected)!.SetValue(config, EnvironmentalGaugeStyle.Icon);
        foreach (string property in Properties)
            Assert.Equal(property == selected ? EnvironmentalGaugeStyle.Icon : EnvironmentalGaugeStyle.Immersive,
                typeof(VanillaExpandedConfig).GetProperty(property)!.GetValue(config));
    }

    /// <summary>Invalid numeric values recover the immersive default for every instrument.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void InvalidEnumValuesFallBack(int value)
    {
        var config = new VanillaExpandedConfig
        {
            ApproximateTimeGaugeStyle = (EnvironmentalGaugeStyle)value,
            EnvironmentalTemperatureGaugeStyle = (EnvironmentalGaugeStyle)value,
            CalendarDateGaugeStyle = (EnvironmentalGaugeStyle)value
        };
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.ApproximateTimeGaugeStyle);
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.EnvironmentalTemperatureGaugeStyle);
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, config.CalendarDateGaugeStyle);
    }

    /// <summary>Lowercase named values and legacy numeric values preserve settings through JSON.</summary>
    [Theory]
    [InlineData("\"icon\"", EnvironmentalGaugeStyle.Icon, "icon")]
    [InlineData("\"immersive\"", EnvironmentalGaugeStyle.Immersive, "immersive")]
    [InlineData("1", EnvironmentalGaugeStyle.Icon, "icon")]
    [InlineData("\"text\"", EnvironmentalGaugeStyle.Text, "text")]
    [InlineData("2", EnvironmentalGaugeStyle.Text, "text")]
    [InlineData("0", EnvironmentalGaugeStyle.Immersive, "immersive")]
    [InlineData("99", EnvironmentalGaugeStyle.Immersive, "immersive")]
    public void JsonSettingsRoundTrip(string encoded, EnvironmentalGaugeStyle expected, string canonical)
    {
        string json = "{" + string.Join(",", Properties.Select(property => "\"" + property + "\":" + encoded)) + "}";
        var config = JsonConvert.DeserializeObject<VanillaExpandedConfig>(json)!;
        Assert.Equal(expected, config.ApproximateTimeGaugeStyle);
        Assert.Equal(expected, config.EnvironmentalTemperatureGaugeStyle);
        Assert.Equal(expected, config.CalendarDateGaugeStyle);
        var persisted = JObject.Parse(JsonConvert.SerializeObject(config));
        foreach (string property in Properties) Assert.Equal(canonical, persisted[property]!.Value<string>());
        var restored = JsonConvert.DeserializeObject<VanillaExpandedConfig>(persisted.ToString())!;
        Assert.Equal(expected, restored.ApproximateTimeGaugeStyle);
        Assert.Equal(expected, restored.EnvironmentalTemperatureGaugeStyle);
        Assert.Equal(expected, restored.CalendarDateGaugeStyle);
    }
    #endregion

    #region Private
    private static readonly string[] Properties =
    [
        nameof(VanillaExpandedConfig.ApproximateTimeGaugeStyle),
        nameof(VanillaExpandedConfig.EnvironmentalTemperatureGaugeStyle),
        nameof(VanillaExpandedConfig.CalendarDateGaugeStyle)
    ];
    #endregion
}
