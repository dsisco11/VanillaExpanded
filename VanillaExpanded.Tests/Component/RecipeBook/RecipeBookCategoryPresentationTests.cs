using System.Reflection;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookCategoryPresentationTests
{
    [Theory]
    [InlineData("flora")]
    [InlineData("clutter")]
    [InlineData("aquatic")]
    [InlineData("liquids")]
    [InlineData("creatures")]
    [InlineData("meta")]
    public void ExcludedCategoryTabsKeepRecipesInAllAndRemainingCategories(string excluded)
    {
        using var f = new MenuFixture(includeRecipes: true);
        var excludedOnly = Assert.Single(f.Lifecycle.Recipes);
        excludedOnly.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = [excluded];
        var overlapping = f.Lifecycle.F.Catalogue.Recipe("overlapping");
        overlapping.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = [excluded, "tools"];
        var custom = f.Lifecycle.F.Catalogue.Recipe("custom");
        custom.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["mod:" + excluded];
        f.Lifecycle.Recipes.AddRange([overlapping, custom]);
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = GetCategoryTabs(menu.SingleComposer);
        Assert.Equal(3, tabs.tabs.Length);
        AssertCategoryPresentation(menu, null, "All");
        AssertCategoryPresentation(menu, "tools", "tools");
        AssertCategoryPresentation(menu, "mod:" + excluded, "mod:" + excluded);
        Assert.Equal(3, session.Rows.Length);
        Assert.Contains(session.Rows, row => row.Recipe.Recipe == excludedOnly);
        tabs.SetValue(GetCategoryIndex(menu, "tools"));
        Assert.Same(overlapping, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(GetCategoryIndex(menu, "mod:" + excluded));
        Assert.Same(custom, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(GetCategoryIndex(menu, null));
        menu.SingleComposer.GetTextInput("search").SetValue(excludedOnly.Output.Code!.ToString());
        Assert.Same(excludedOnly, Assert.Single(session.Rows).Recipe.Recipe);
    }

    [Fact]
    public void SelectedCategoryCodeAndSearchSurviveResizeAndReopen()
    {
        var categoryNames = new Dictionary<string, string>
        {
            ["tabname-mod:a"] = "Shared label",
            ["tabname-mod:z"] = "Shared label",
            ["tabname-mod:b"] = "Selected before"
        };
        using var f = new MenuFixture(includeRecipes: true, categoryNames: categoryNames);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs =
            ["mod:z", "items", "clothing", "tools", "terrain", "mechanics", "decorative", "construction", "mod:b", "mod:a"];
        var other = f.Lifecycle.F.Catalogue.Recipe("other");
        f.Lifecycle.Recipes.Add(other);
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        int baseline = f.TextureCount;
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = GetCategoryTabs(menu.SingleComposer);
        AssertCategoryPresentation(menu, "mod:a", "Shared label");
        AssertCategoryPresentation(menu, "mod:z", "Shared label");
        Assert.Equal(2, tabs.tabs.Count(tab => tab.Name == "Shared label"));
        int firstDuplicateIndex = GetCategoryIndex(menu, "mod:a");
        tabs.SetValue(firstDuplicateIndex);
        Assert.Equal("mod:a", session.Category);
        Assert.Equal(firstDuplicateIndex, tabs.activeElement);
        Assert.Equal("Shared label", menu.SingleComposer.GetDynamicText("category-title").GetText());
        Assert.Same(recipe, Assert.Single(session.Rows).Recipe.Recipe);
        int secondDuplicateIndex = GetCategoryIndex(menu, "mod:z");
        tabs.SetValue(secondDuplicateIndex);
        Assert.Equal("mod:z", session.Category);
        Assert.Equal(secondDuplicateIndex, tabs.activeElement);
        Assert.Equal("Shared label", menu.SingleComposer.GetDynamicText("category-title").GetText());
        Assert.Same(recipe, Assert.Single(session.Rows).Recipe.Recipe);
        int categoryIndex = GetCategoryIndex(menu, "mod:b");
        tabs.SetValue(categoryIndex);
        Assert.Equal("mod:b", session.Category);
        Assert.Equal("Selected before", menu.SingleComposer.GetDynamicText("category-title").GetText());
        Assert.Same(recipe, Assert.Single(session.Rows).Recipe.Recipe);
        menu.SingleComposer.GetTextInput("search").SetValue(" output ");
        var oldComposer = menu.SingleComposer;
        f.ResizePanelSpace(oldComposer.Bounds.fixedWidth + 20, oldComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(oldComposer, menu.SingleComposer);
        Assert.Equal(GetCategoryIndex(menu, "mod:b"), GetCategoryTabs(menu.SingleComposer).activeElement);
        Assert.Equal("mod:b", session.Category);
        AssertCategoryPresentation(menu, "mod:b", "Selected before");
        Assert.Equal("Selected before", menu.SingleComposer.GetDynamicText("category-title").GetText());
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
        categoryNames["tabname-mod:a"] = "Alpha revised";
        categoryNames["tabname-mod:z"] = "Zulu revised";
        categoryNames["tabname-mod:b"] = "Selected after";
        f.Parent.TryOpen();
        var reopened = Assert.Single(f.Menus);
        Assert.NotSame(menu, reopened);
        var restored = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
        Assert.Equal("mod:b", restored.Category);
        AssertCategoryPresentation(reopened, "mod:a", "Alpha revised");
        AssertCategoryPresentation(reopened, "mod:z", "Zulu revised");
        AssertCategoryPresentation(reopened, "mod:b", "Selected after");
        Assert.Equal("Selected after", reopened.SingleComposer.GetDynamicText("category-title").GetText());
        Assert.Equal(GetCategoryIndex(reopened, "mod:b"),
            GetCategoryTabs(reopened.SingleComposer).activeElement);
        Assert.Same(recipe, Assert.Single(restored.Rows).Recipe.Recipe);
        Assert.Equal(" output ", reopened.SingleComposer.GetTextInput("search").GetText());
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
    }

    private static string?[] GetCategories(GuiDialogRecipeBook book) =>
        (string?[])typeof(GuiDialogRecipeBook)
            .GetField("categories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;

    private static int GetCategoryIndex(GuiDialogRecipeBook book, string? code)
    {
        int index = Array.IndexOf(GetCategories(book), code);
        Assert.True(index >= 0);
        return index;
    }

    private static void AssertCategoryPresentation(GuiDialogRecipeBook book, string? code, string label)
    {
        int index = GetCategoryIndex(book, code);
        var tabs = GetCategoryTabs(book.SingleComposer);
        var tab = Assert.Single(tabs.tabs, candidate => candidate.DataInt == index);
        Assert.Equal(label, tab.Name);
        Assert.Equal(label, tabs.Hints[index].GetText());
    }
}
