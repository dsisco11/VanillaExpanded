using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingCatalogueViewTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class CraftingCategoryProjectionTests
{
    [Fact]
    public void CategoriesUseImmutableNativeTagsAndSearchWithinOverlappingGroups()
    {
        var fixture = new CatalogueFixture();
        var tool = fixture.Stack(10, "mod:hammer");
        tool.Collectible.CreativeInventoryTabs = ["general", "items", "tools", "tools"];
        var custom = fixture.Stack(11, "mod:bench");
        custom.Collectible.CreativeInventoryTabs = ["general", "mod:workshop"];
        var generic = fixture.Stack(12, "mod:uncategorized");
        generic.Collectible.CreativeInventoryTabs = ["general"];
        var catalogue = fixture.Build(fixture.Recipe("hammer", tool), fixture.Recipe("bench", custom),
            fixture.Recipe("uncategorized", generic), fixture.Recipe("no-tags"));
        tool.Collectible.CreativeInventoryTabs[2] = "changed";
        var view = new CraftingCatalogueView(catalogue);

        Assert.Equal(new[] { "", "items", "mod:workshop", "tools" }, catalogue.CategoryCodes);
        Assert.Equal(new[] { "items", "tools" }, catalogue.Recipes[0].CategoryCodes);
        Assert.Equal(4, Query(view).Length);
        Assert.Equal("hammer", Assert.Single(Query(view, "", "tools")).Recipe.Recipe.Name!.Path);
        Assert.Single(Query(view, "HAMMER", "items"));
        Assert.Empty(Query(view, "bench", "tools"));
        Assert.Single(Query(view, "bench", "mod:workshop"));
        Assert.Equal(2, Query(view, "", CraftingCatalogue.OtherCategory).Length);
        Assert.Empty(Query(view, "", "general"));
    }

    [Fact]
    public void CreativeStackCategoriesMatchOutputAttributesWithoutIncludingOtherVariants()
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "mod:container");
        output.Attributes.SetString("variant", "tool");
        var matching = output.Clone();
        var other = output.Clone();
        other.Attributes.SetString("variant", "decorative");
        output.Collectible.CreativeInventoryStacks = [
            new() { Tabs = ["general", "tools"], Stacks = [new JsonItemStack { ResolvedItemstack = matching }] },
            new() { Tabs = ["decorative"], Stacks = [new JsonItemStack { ResolvedItemstack = other }] }
        ];
        var catalogue = fixture.Build(fixture.Recipe("container", output));

        Assert.Equal(new[] { "tools" }, catalogue.CategoryCodes);
        Assert.Equal(new[] { "tools" }, Assert.Single(catalogue.Recipes).CategoryCodes);
        Assert.Empty(Query(new CraftingCatalogueView(catalogue), "", "decorative"));
    }

    [Fact]
    public void OnlyIncludedRecipeOutputsContributeCategories()
    {
        var fixture = new CatalogueFixture();
        var hidden = fixture.Recipe("hidden");
        hidden.Enabled = false;
        hidden.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var unresolved = fixture.Recipe("unresolved");
        unresolved.Output!.ResolvedItemStack = null;
        var catalogue = fixture.Build(hidden, unresolved);

        Assert.Equal(new[] { CraftingCatalogue.OtherCategory }, catalogue.CategoryCodes);
        Assert.Single(Query(new CraftingCatalogueView(catalogue), "", CraftingCatalogue.OtherCategory));
        Assert.Empty(fixture.Build().CategoryCodes);
    }
}
