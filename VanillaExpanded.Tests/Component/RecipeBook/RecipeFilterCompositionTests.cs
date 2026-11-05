using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeFilterFeatureTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class RecipeFilterCompositionTests
{
    [Fact]
    public void FeatureUpdatesPublishOnlyTheirCompletedIntersectionAndRemoveNeutralRestrictions()
    {
        var fixture = new CatalogueFixture();
        var hammer = fixture.Recipe("hammer", fixture.Stack(10, "mod:hammer"));
        hammer.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools", "items"];
        var bench = fixture.Recipe("bench", fixture.Stack(11, "mod:bench"));
        bench.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["construction"];
        var other = fixture.Recipe("other");
        var catalogue = fixture.Build(hammer, bench, other);
        var view = new CraftingCatalogueView(catalogue);
        using var filters = new RecipeFilterSet();
        var publications = new List<string[]>();
        void Changed() => publications.Add(view.GetRows(filters)
            .Select(row => row.Recipe.Recipe.Name!.Path).ToArray());
        var search = new RecipeSearch(filters, Changed);
        var category = new RecipeCategory(catalogue, filters, Changed);
        Assert.Null(category.Selected);
        Assert.Equal(3, view.GetRows(filters).Length);

        search.SetText(" \t MOD:HAMMER  ");
        category.Select("tools");
        search.SetText("bench");
        category.Select("construction");
        search.SetText(null);
        category.Select(null);
        category.Select(null);
        search.SetText(" \r\n ");

        Assert.Equal(7, publications.Count);
        Assert.Equal(new[] { "hammer" }, publications[0]);
        Assert.Equal(new[] { "hammer" }, publications[1]);
        Assert.Empty(publications[2]);
        Assert.Equal(new[] { "bench" }, publications[3]);
        Assert.Equal(new[] { "bench" }, publications[4]);
        Assert.Equal(3, publications[5].Length);
        Assert.Equal(3, publications[6].Length);
        Assert.Null(category.Selected);
        using var searchIdentity = filters.Register("vanillaexpanded:search", new SearchFilter("missing"));
        using var categoryIdentity = filters.Register("vanillaexpanded:category", new CategoryFilter("tools"));
        Assert.Empty(view.GetRows(filters));
    }
}
