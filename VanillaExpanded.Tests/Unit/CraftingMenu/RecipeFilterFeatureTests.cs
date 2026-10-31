using VanillaExpanded.CraftingMenu;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public sealed class RecipeFilterFeatureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void EmptySearchIsNeutral(string? text)
    {
        var fixture = new CatalogueFixture();
        var row = new CraftingCatalogueView(fixture.Build(fixture.Recipe("one"))).GetSelection(0);
        var filter = new SearchFilter(text);
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(row));
    }

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

    [Fact]
    public void CategoryFilterUsesTheSelectedRecipeSnapshotRatherThanMutableNativeTags()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("tool");
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools", "items"];
        var view = new CraftingCatalogueView(fixture.Build(recipe));
        var row = view.GetSelection(0);
        recipe.Output.ResolvedItemStack.Collectible.CreativeInventoryTabs = ["construction"];

        Assert.True(new CategoryFilter("tools").Matches(row));
        Assert.True(new CategoryFilter("items").Matches(row));
        Assert.False(new CategoryFilter("construction").Matches(row));
        Assert.False(new CategoryFilter("Tools").Matches(row));
        Assert.False(new CategoryFilter(CraftingCatalogue.OtherCategory).Matches(row));
        var uncategorized = new CraftingRecipeEntry(row.Recipe.Id, recipe, "other", categoryCodes: []);
        Assert.True(new CategoryFilter(CraftingCatalogue.OtherCategory).Matches(row with { Recipe = uncategorized }));
        Assert.Throws<ArgumentNullException>(() => new CategoryFilter(null!));
    }

    [Fact]
    public void InvalidCategoryRetainsPreviousCriteriaAndDoesNotNotify()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("tool");
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var catalogue = fixture.Build(recipe);
        using var filters = new RecipeFilterSet();
        int changes = 0;
        var category = new RecipeCategory(catalogue, filters, () => changes++);
        category.Select("tools");
        Assert.Throws<ArgumentException>(() => category.Select("unknown"));
        Assert.Equal("tools", category.Selected);
        Assert.Equal(1, changes);
        Assert.Single(new CraftingCatalogueView(catalogue).GetRows(filters));
    }
}
