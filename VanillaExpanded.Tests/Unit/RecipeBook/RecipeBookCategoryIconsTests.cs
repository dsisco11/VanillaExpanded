using VanillaExpanded.RecipeBook;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
public sealed class RecipeBookCategoryIconsTests
{
    [Theory]
    [InlineData(null, "all")]
    [InlineData("", "other")]
    [InlineData("construction", "construction")]
    [InlineData("decorative", "decorative")]
    [InlineData("mechanics", "mechanics")]
    [InlineData("terrain", "terrain")]
    [InlineData("tools", "tools")]
    [InlineData("clothing", "clothing")]
    [InlineData("items", "items")]
    [InlineData("mod:tools", "mod-category")]
    [InlineData("unknown", "mod-category")]
    public void CategoriesResolveExactIconsAndModCategoriesUseTheTag(string? category, string icon)
    {
        Assert.Equal(RecipeBookCategoryIcons.Id(icon), RecipeBookCategoryIcons.ForCategory(category));
    }
}
