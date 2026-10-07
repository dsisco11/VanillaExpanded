using System.Text.Json;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Checks freshness presentation defaults, ConfigLib events, and localized mapped choices.</summary>
[Trait("Category", "Unit")]
public sealed class FreshnessIndicatorStyleConfigTests
{
    #region Public API
    /// <summary>All indicator settings are sorted into their dedicated localized ConfigLib section.</summary>
    [Fact]
    public void IndicatorSettings_HaveDedicatedSection()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "VanillaExpanded", "assets",
            "vanillaexpanded", "config", "configlib-patches.json")));
        var heading = document.RootElement.GetProperty("formatting").EnumerateArray().Single(entry =>
            entry.GetProperty("title").GetString() == "vanillaexpanded:config-section-item-slot-indicators");
        float sectionWeight = heading.GetProperty("weight").GetSingle();
        var indicatorSettings = document.RootElement.GetProperty("settings").EnumerateObject()
            .SelectMany(category => category.Value.EnumerateObject()).Where(setting =>
                setting.Name.Contains("Indicator") || setting.Name is "EnableLiquidSloshEffect" or "EnableCrucibleEffect");
        Assert.Equal(12, indicatorSettings.Count());
        Assert.All(indicatorSettings, setting => Assert.InRange(setting.Value.GetProperty("weight").GetSingle(),
            sectionWeight + 0.01f, sectionWeight + 0.99f));
    }
    /// <summary>ConfigLib's saved mapping keys load without discarding other settings; numeric legacy values also load.</summary>
    [Theory]
    [InlineData("\"freshness-background\"", "freshness-background", 0)]
    [InlineData("\"freshness-outline\"", "freshness-outline", 1)]
    [InlineData("\"freshness-bar\"", "freshness-bar", 2)]
    [InlineData("2", "2", 2)]
    public void PersistedMappingKey_LoadsWithoutResettingConfig(string jsonValue, string expected, int style)
    {
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<VanillaExpandedConfig>(
            "{\"EnableAutoStash\":false,\"PerishableItemFreshnessIndicatorStyle\":" + jsonValue + "}")!;
        Assert.Equal(expected, config.PerishableItemFreshnessIndicatorStyle);
        Assert.False(config.EnableAutoStash);
        Assert.Equal((ItemSlotIndicatorRenderingStyle)style, config.FreshnessRenderingStyle);
    }
    /// <summary>Mapped numeric event values update the presentation while invalid selections retain background fallback.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 0)]
    public void ConfigLibEvent_UpdatesLiveSelector(int value, int expected)
    {
        var config = new VanillaExpandedConfig();
        Assert.Equal("freshness-background", config.PerishableItemFreshnessIndicatorStyle);
        var registration = new ItemSlotIndicatorRegistration(new Moq.Mock<IItemSlotIndicatorProvider>().Object, 0, 1000, null,
            supportedStyles: [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.SlotOutline,
                ItemSlotIndicatorRenderingStyle.HorizontalBar],
            styleSelector: () => config.FreshnessRenderingStyle);
        var update = new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.PerishableItemFreshnessIndicatorStyle)),
            ["Value"] = new IntAttribute(value)
        };
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, update));
        Assert.Equal(value.ToString(), config.PerishableItemFreshnessIndicatorStyle);
        Assert.Equal((ItemSlotIndicatorRenderingStyle)expected, registration.ResolveStyle());
    }

    /// <summary>Live mapping-key events resolve through the same configuration accessor used by the renderer registration.</summary>
    [Theory]
    [InlineData("freshness-background", 0)]
    [InlineData("freshness-outline", 1)]
    [InlineData("freshness-bar", 2)]
    public void ConfigLibKeyEvent_ResolvesProductionStyle(string key, int style)
    {
        var config = new VanillaExpandedConfig();
        var update = new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.PerishableItemFreshnessIndicatorStyle)),
            ["Value"] = new StringAttribute(key)
        };
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, update));
        Assert.Equal((ItemSlotIndicatorRenderingStyle)style, config.FreshnessRenderingStyle);
    }

    /// <summary>All mapped options and their default carry localized labels alongside a localized behavioral description.</summary>
    [Fact]
    public void Asset_DeclaresLocalizedMappedChoices()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string assets = Path.Combine(root.FullName, "VanillaExpanded", "assets", "vanillaexpanded");
        using var patches = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "config", "configlib-patches.json")));
        using var language = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
        var setting = patches.RootElement.GetProperty("settings").GetProperty("integer")
            .GetProperty(nameof(VanillaExpandedConfig.PerishableItemFreshnessIndicatorStyle));
        var mapping = setting.GetProperty("mapping");
        Assert.Equal(new[] { 0, 1, 2 }, mapping.EnumerateObject().Select(option => option.Value.GetInt32()).Order());
        Assert.Equal(0, mapping.GetProperty(setting.GetProperty("default").GetString()!).GetInt32());
        Assert.True(setting.GetProperty("clientSide").GetBoolean());
        foreach (string key in mapping.EnumerateObject().Select(option => "vanillaexpanded:mappingkey-" + option.Name)
            .Concat(new[] { setting.GetProperty("ingui").GetString()!, setting.GetProperty("comment").GetString()! }))
            Assert.False(string.IsNullOrWhiteSpace(language.RootElement.GetProperty(key.Split(':')[1]).GetString()));
    }
    #endregion
}
