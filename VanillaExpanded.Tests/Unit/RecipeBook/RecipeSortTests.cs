using System.Globalization;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class RecipeSortTests
{
    [Fact]
    public void SortOwnsItsOptionsAndRejectsUnknownIdentitiesWithoutChangingState()
    {
        int changes = 0;
        var sort = new RecipeSort(() => changes++);
        var other = new RecipeSort(() => throw new InvalidOperationException("Independent sort changed."));
        Assert.Equal(new[] { "availability", "name" }, sort.Orders.Select(order => order.Id));
        Assert.Equal("availability", sort.Selected.Id);
        Assert.NotSame(sort.Selected, other.Selected);
        sort.Select("availability");
        Assert.Equal(0, changes);
        sort.Select("name");
        Assert.Equal(1, changes);
        sort.Select("name");
        Assert.Equal(1, changes);
        Assert.Throws<ArgumentException>(() => sort.Select("unknown"));
        Assert.Equal("name", sort.Selected.Id);
        Assert.Equal("availability", other.Selected.Id);
        Assert.Equal(1, changes);
        sort.Select("availability");
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("en-US", "accented", "apple", "zebra")]
    [InlineData("sv-SE", "apple", "zebra", "accented")]
    public void NameOrderUsesCurrentCultureAndIgnoresAvailability(string culture, params string[] expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var f = new CatalogueFixture();
            var apple = f.Recipe("apple");
            apple.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "Apple");
            var zebra = f.Recipe("zebra");
            zebra.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "Zebra");
            var accented = f.Recipe("accented");
            accented.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "\u00C4lg");
            var catalogue = f.Build(zebra, apple, accented);
            var view = new CraftingCatalogueView(catalogue);
            view.ReplaceEvaluations(catalogue.Recipes.ToDictionary(entry => entry.Id, entry =>
                entry.Recipe == zebra ? CatalogueFixture.Full() : CraftingRecipeEvaluation.Unsupported("unsupported")));
            using var filters = new RecipeFilterSet();
            Assert.Equal(expected, view.GetRows(filters, new NameOrder()).Select(row => row.Recipe.Recipe.Name!.Path));
            Assert.Same(zebra, view.GetRows(filters)[0].Recipe.Recipe);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
