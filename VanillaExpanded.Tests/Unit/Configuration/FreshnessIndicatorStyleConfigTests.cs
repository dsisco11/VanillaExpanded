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
    /// <summary>Mapped numeric event values update the presentation while invalid selections retain background fallback.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 0)]
    public void ConfigLibEvent_UpdatesLiveSelector(int value, int expected)
    {
        var config = new VanillaExpandedConfig();
        Assert.Equal(0, config.PerishableItemFreshnessIndicatorStyle);
        var registration = new ItemSlotIndicatorRegistration(new Moq.Mock<IItemSlotIndicatorProvider>().Object, 0, 1000, null,
            supportedStyles: [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.SlotOutline,
                ItemSlotIndicatorRenderingStyle.HorizontalBar],
            styleSelector: () => (ItemSlotIndicatorRenderingStyle)config.PerishableItemFreshnessIndicatorStyle);
        var update = new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.PerishableItemFreshnessIndicatorStyle)),
            ["Value"] = new IntAttribute(value)
        };
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, update));
        Assert.Equal(value, config.PerishableItemFreshnessIndicatorStyle);
        Assert.Equal((ItemSlotIndicatorRenderingStyle)expected, registration.ResolveStyle());
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
