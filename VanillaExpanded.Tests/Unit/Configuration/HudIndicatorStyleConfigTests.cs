using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Checks independently persisted text preferences across registered HUD indicators.</summary>
public sealed class HudIndicatorStyleConfigTests
{
    #region Public API
    /// <summary>Each typed preference retains its previous default and independently accepts text, numeric input, and ConfigLib updates.</summary>
    [Theory]
    [InlineData(nameof(VanillaExpandedConfig.BowAmmunitionIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.BodyTemperatureIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.PlayerWetnessIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.ShelterIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.WeatherConditionsIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.RiftActivityIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.TemporalStormIndicatorStyle), false)]
    [InlineData(nameof(VanillaExpandedConfig.ApproximateTimeGaugeStyle), true)]
    [InlineData(nameof(VanillaExpandedConfig.EnvironmentalTemperatureGaugeStyle), true)]
    [InlineData(nameof(VanillaExpandedConfig.CalendarDateGaugeStyle), true)]
    public void TypedStylePreservesDefaultsAndSupportsText(string propertyName, bool environmental)
    {
        var property = typeof(VanillaExpandedConfig).GetProperty(propertyName)!;
        object defaultValue = environmental ? (object)EnvironmentalGaugeStyle.Immersive : HudIndicatorStyle.Icon;
        object textValue = environmental ? (object)EnvironmentalGaugeStyle.Text : HudIndicatorStyle.Text;
        var config = new VanillaExpandedConfig();
        Assert.Equal(defaultValue, property.GetValue(config));
        foreach (string encoded in new[] { "\"text\"", environmental ? "2" : "1" })
        {
            var restored = JsonConvert.DeserializeObject<VanillaExpandedConfig>("{\"" + propertyName + "\":" + encoded + "}")!;
            Assert.Equal(textValue, property.GetValue(restored));
            Assert.Equal("text", JObject.Parse(JsonConvert.SerializeObject(restored))[propertyName]!.Value<string>());
        }
        foreach (string raw in new[] { "text", environmental ? "2" : "1" })
        {
            var update = new TreeAttribute
            {
                ["MappingKey"] = new StringAttribute(propertyName), ["Value"] = new StringAttribute(raw)
            };
            Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, update));
            Assert.Equal(textValue, property.GetValue(config));
        }
        // Updating one property must not mutate another instrument's independent preference.
        foreach (var other in typeof(VanillaExpandedConfig).GetProperties().Where(value =>
            value.Name != propertyName && (value.PropertyType == typeof(HudIndicatorStyle) || value.PropertyType == typeof(EnvironmentalGaugeStyle))))
            Assert.Equal(other.PropertyType == typeof(EnvironmentalGaugeStyle) ? (object)EnvironmentalGaugeStyle.Immersive : HudIndicatorStyle.Icon,
                other.GetValue(config));
        property.SetValue(config, Enum.ToObject(property.PropertyType, 999));
        Assert.Equal(defaultValue, property.GetValue(config));
    }
    /// <summary>All style selectors use localized HUD-section choices with enum-compatible numeric indices.</summary>
    [Fact]
    public void ConfigLibChoicesStayInHudSectionAndMatchTypedEnums()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        string assets = Path.Combine(root!.FullName, "VanillaExpanded/assets/vanillaexpanded");
        var patch = JObject.Parse(File.ReadAllText(Path.Combine(assets, "config/configlib-patches.json")));
        var language = JObject.Parse(File.ReadAllText(Path.Combine(assets, "lang/en.json")));
        double sectionWeight = patch["formatting"]!.Single(value => value["title"]!.Value<string>() ==
            "vanillaexpanded:config-section-hud-overlays")["weight"]!.Value<double>();
        var properties = typeof(VanillaExpandedConfig).GetProperties().Where(value =>
            value.PropertyType == typeof(HudIndicatorStyle) || value.PropertyType == typeof(EnvironmentalGaugeStyle)).ToArray();
        Assert.Equal(11, properties.Length);
        foreach (var property in properties)
        {
            var setting = patch["settings"]!["integer"]![property.Name]!;
            Assert.InRange(setting["weight"]!.Value<double>(), sectionWeight + .01, sectionWeight + .99);
            Assert.True(setting["clientSide"]!.Value<bool>());
            var mapping = (JObject)setting["mapping"]!;
            Assert.Equal(property.PropertyType == typeof(EnvironmentalGaugeStyle) ? 2 : 1, mapping["text"]!.Value<int>());
            Assert.Equal(0, mapping[setting["default"]!.Value<string>()!]!.Value<int>());
            foreach (string key in mapping.Properties().Select(value => "mappingkey-" + value.Name)
                .Concat(new[] { setting["ingui"]!.Value<string>()!.Split(':')[1], setting["comment"]!.Value<string>()!.Split(':')[1] }))
                Assert.False(string.IsNullOrWhiteSpace(language[key]!.Value<string>()));
        }
    }
    #endregion
}
