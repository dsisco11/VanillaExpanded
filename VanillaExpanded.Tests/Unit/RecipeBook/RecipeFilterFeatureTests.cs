using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeFilterFeatureTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class RecipeFilterFeatureTests
{
    [Theory]
    [InlineData("full", true)]
    [InlineData("partial", false)]
    [InlineData("missing", false)]
    [InlineData("unsupported", false)]
    [InlineData("pending", false)]
    [InlineData("complete-but-rejected", false)]
    public void CraftableFilterReadsOnlyAuthoritativeCraftability(string state, bool expected)
    {
        var fixture = new CatalogueFixture();
        var view = new CraftingCatalogueView(fixture.Build(fixture.Recipe("one")));
        var row = view.GetSelection(0);
        var evaluation = state switch
        {
            "full" => CatalogueFixture.Full(),
            "partial" => CraftingRecipeEvaluation.Supported(1, 2, false, [new("A", 1)]),
            "missing" => CraftingRecipeEvaluation.Supported(0, 2, false, [new("A", 2)]),
            "unsupported" => CraftingRecipeEvaluation.Unsupported("unsupported"),
            "complete-but-rejected" => CraftingRecipeEvaluation.Supported(2, 2, false, []),
            "pending" => row.Evaluation,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        Assert.Equal(expected, new CraftableFilter().Matches(row with { Evaluation = evaluation }));
    }

    [Fact]
    public void CraftableFeatureOwnsOnlyItsRegistrationAndNotifiesAfterCompletedChanges()
    {
        var fixture = new CatalogueFixture();
        var view = new CraftingCatalogueView(fixture.Build(fixture.Recipe("one")));
        using var filters = new RecipeFilterSet();
        using var search = filters.Register("test:search", new SearchFilter("missing"));
        var notifications = new List<bool>();
        CraftableOnly? feature = null;
        feature = new(filters, () => notifications.Add(feature!.Enabled));
        Assert.False(feature.Enabled);
        feature.SetEnabled(false);
        for (int i = 0; i < 3; i++)
        {
            feature.SetEnabled(true);
            feature.SetEnabled(true);
            Assert.Empty(view.GetRows(filters));
            Assert.Throws<ArgumentException>(() =>
                filters.Register("vanillaexpanded:craftable-only", new CraftableFilter()));
            feature.SetEnabled(false);
            feature.SetEnabled(false);
            Assert.Empty(view.GetRows(filters));
        }
        Assert.Equal(new[] { true, false, true, false, true, false }, notifications);
        using var reserved = filters.Register("vanillaexpanded:craftable-only", new CraftableFilter());
        Assert.Throws<ArgumentException>(() => feature.SetEnabled(true));
        Assert.False(feature.Enabled);
        Assert.Equal(6, notifications.Count);
        reserved.Dispose();
        search.Dispose();
        Assert.Single(view.GetRows(filters));
        feature.SetEnabled(true);
        Assert.Empty(view.GetRows(filters));
        feature.SetEnabled(false);
        Assert.Single(view.GetRows(filters));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCraftableMutationDuringProjectionKeepsStateAndDoesNotNotify(bool initiallyEnabled)
    {
        var fixture = new CatalogueFixture();
        var row = new CraftingCatalogueView(fixture.Build(fixture.Recipe("one"))).GetSelection(0)
            with { Evaluation = CatalogueFixture.Full() };
        using var filters = new RecipeFilterSet();
        int notifications = 0;
        var feature = new CraftableOnly(filters, () => notifications++);
        feature.SetEnabled(initiallyEnabled);
        notifications = 0;
        using var mutation = filters.Register("test:mutation",
            new MutationFilter(() => feature.SetEnabled(!initiallyEnabled)));
        Assert.Throws<InvalidOperationException>(() => filters.Filter([row]));
        Assert.Equal(initiallyEnabled, feature.Enabled);
        Assert.Equal(0, notifications);
        mutation.Dispose();
        feature.SetEnabled(!initiallyEnabled);
        Assert.Equal(!initiallyEnabled, feature.Enabled);
        Assert.Equal(1, notifications);
    }

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
