using VanillaExpanded.ModSystems;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Datastructures;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.Configuration.ConfigurationTestSupport;

namespace VanillaExpanded.Tests.Unit.Configuration;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class RecipeBookConfigContractTests
{
    [Fact]
    public void RecipeBookToggle_DefaultsToEnabledAndPersistsExplicitDisable()
    {
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<VanillaExpandedConfig>(
            "{\"EnableAutoStash\":false}")!;
        Assert.True(config.EnableRecipeBook);
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config,
            new StringAttribute("{\"EnableRecipeBook\":\"false\"}")));
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<VanillaExpandedConfig>(
            Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        Assert.False(restored.EnableRecipeBook);
        Assert.False(restored.EnableAutoStash);
    }

    [Fact]
    public void RecipeBookToggle_IsClientSideLocalizedAndMatchesTheConfigDefault()
    {
        string assets = Path.Combine(FindRepositoryRoot(), "VanillaExpanded", "assets", "vanillaexpanded");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "config", "configlib-patches.json")));
        using var language = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
        var toggle = document.RootElement.GetProperty("settings").GetProperty("boolean").GetProperty("EnableRecipeBook");
        Assert.Equal(new VanillaExpandedConfig().EnableRecipeBook, toggle.GetProperty("default").GetBoolean());
        Assert.True(toggle.GetProperty("clientSide").GetBoolean());
        var heading = document.RootElement.GetProperty("formatting").EnumerateArray().Single(entry =>
            entry.GetProperty("title").GetString() == "vanillaexpanded:config-section-recipe-book");
        foreach (string key in new[] { toggle.GetProperty("ingui").GetString()!,
            toggle.GetProperty("comment").GetString()!, heading.GetProperty("title").GetString()! })
            Assert.False(string.IsNullOrWhiteSpace(language.RootElement.GetProperty(key.Split(':')[1]).GetString()));
    }
}
