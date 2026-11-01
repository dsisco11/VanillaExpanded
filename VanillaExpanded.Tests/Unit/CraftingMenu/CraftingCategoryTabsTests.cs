using VanillaExpanded.CraftingMenu;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public sealed class CraftingCategoryTabsTests
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
        Assert.Equal(CraftingCategoryIcons.Id(icon), CraftingCategoryIcons.ForCategory(category));
    }
}
