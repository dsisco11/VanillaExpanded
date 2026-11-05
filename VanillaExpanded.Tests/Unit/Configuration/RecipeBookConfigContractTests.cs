using VanillaExpanded.ModSystems;
using Vintagestory.API.Datastructures;
using VanillaExpanded.Tests;

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

}
