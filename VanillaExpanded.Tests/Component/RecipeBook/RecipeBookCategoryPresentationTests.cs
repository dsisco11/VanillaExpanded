using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
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
using Vintagestory.GameContent;
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
        Assert.Equal(new[] { "All", "tools", "mod:" + excluded }, tabs.tabs.Select(tab => tab.Name));
        Assert.Equal(3, session.Rows.Length);
        Assert.Contains(session.Rows, row => row.Recipe.Recipe == excludedOnly);
        tabs.SetValue(1);
        Assert.Same(overlapping, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(2);
        Assert.Same(custom, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(0);
        menu.SingleComposer.GetTextInput("search").SetValue(excludedOnly.Output.Code!.ToString());
        Assert.Same(excludedOnly, Assert.Single(session.Rows).Recipe.Recipe);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void CategoryPresentationUsesStandardOrderThenLocalizedAdditionsAndRestoresCode(float scale)
    {
        var names = new Dictionary<string, string>
        {
            ["tabname-construction"] = "Zulu construction",
            ["tabname-decorative"] = "Yankee decorative",
            ["tabname-mechanics"] = "Xray mechanics",
            ["tabname-terrain"] = "Whiskey terrain",
            ["tabname-tools"] = "Victor tools",
            ["tabname-clothing"] = "Uniform clothing",
            ["tabname-items"] = "Tango items",
            ["tabname-mod:z"] = "Aardvark addition",
            ["tabname-mod:a"] = "Shared name",
            ["tabname-mod:b"] = "shared name"
        };
        using var f = new MenuFixture(scale: scale, includeRecipes: true, categoryNames: names);
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
        var tabs = GetCategoryTabs(menu.SingleComposer);
        Assert.Equal(new[] { "All", "Zulu construction", "Yankee decorative", "Xray mechanics",
            "Whiskey terrain", "Victor tools", "Uniform clothing", "Tango items",
            "Aardvark addition", "Shared name", "shared name", "Other" }, tabs.tabs.Select(tab => tab.Name));
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        Assert.Equal(2, session.Rows.Length);
        for (int index = 1; index <= 10; index++)
        {
            tabs.SetValue(index);
            Assert.Same(recipe, Assert.Single(session.Rows).Recipe.Recipe);
        }
        Assert.Equal("mod:b", session.Category);
        Assert.Equal("shared name", menu.SingleComposer.GetDynamicText("category-title").GetText());
        Assert.Equal("shared name", tabs.Hints[10].GetText());
        tabs.SetValue(11);
        Assert.Same(other, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(10);
        menu.SingleComposer.GetTextInput("search").SetValue(" output ");
        var oldComposer = menu.SingleComposer;
        f.ResizePanelSpace(oldComposer.Bounds.fixedWidth + 20, oldComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(oldComposer, menu.SingleComposer);
        Assert.Equal(10, GetCategoryTabs(menu.SingleComposer).activeElement);
        Assert.Equal("mod:b", session.Category);
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
        names["tabname-mod:b"] = "Aardvark custom";
        names["tabname-mod:z"] = "Zulu custom";
        f.Parent.TryOpen();
        var reopened = Assert.Single(f.Menus);
        Assert.NotSame(menu, reopened);
        tabs = GetCategoryTabs(reopened.SingleComposer);
        Assert.Equal(new[] { "Aardvark custom", "Shared name", "Zulu custom", "Other" },
            tabs.tabs.Skip(8).Select(tab => tab.Name));
        Assert.Equal(8, tabs.activeElement);
        var restored = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
        Assert.Equal("mod:b", restored.Category);
        Assert.Same(recipe, Assert.Single(restored.Rows).Recipe.Recipe);
        Assert.Equal(" output ", reopened.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal("Aardvark custom", reopened.SingleComposer.GetDynamicText("category-title").GetText());
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
    }
}
