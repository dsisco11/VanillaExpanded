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
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookBrowsingRestorationTests
{
    [Fact]
    public void ParentReopeningRestoresTheOpenMenuWithoutDuplicateDialogs()
    {
        using var f = new MenuFixture();
        f.Parent.Opening = f.Recompose;
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
        f.Toggle();
        var first = Assert.Single(f.Menus);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            f.Parent.TryClose();
            Assert.Empty(f.Menus);
            f.Parent.TryOpen();
            var restored = Assert.Single(f.Menus);
            Assert.NotSame(first, restored);
            Assert.True(restored.IsOpened());
            f.AssertToggleOpen();
            f.Lifecycle.Drain();
            Assert.Same(restored, Assert.Single(f.Menus));
            first = restored;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void GridPriorityRefreshKeepsInspectedRecipeConsistentAndReopeningUsesCurrentInputs(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var one = Assert.Single(f.Lifecycle.Recipes);
        one.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "A one");
        var wood = one.ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        wood.StackSize = 1;
        var stick = f.Lifecycle.F.Stack(2, "stick", 1);
        var both = f.Lifecycle.F.Recipe(f.Lifecycle.F.Ingredient(wood, 4), f.Lifecycle.F.Ingredient(stick, 4));
        both.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "B both");
        var unrelated = f.Lifecycle.F.Recipe(f.Lifecycle.F.Ingredient(f.Lifecycle.F.Stack(3, "stone", 1)));
        unrelated.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "C unrelated");
        f.Lifecycle.Recipes.AddRange([both, unrelated]);
        var inventory = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        inventory[8].Itemstack = wood;
        inventory[6].Itemstack = stick;
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        int baseline = f.TextureCount;
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        Assert.Equal(new[] { both, one, unrelated }, session.Rows.Select(row => row.Recipe.Recipe));
        Assert.Equal(new[] { 2, 1, 0 }, session.Rows.Select(row => row.GridMatches));
        Assert.False(session.Rows[0].Evaluation.CanCraft);
        Assert.True(session.Rows[1].Evaluation.CanCraft);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        var bounds = tiles.Elements[0].Bounds;
        tiles.UpdateHover((int)(bounds.absX + 5), (int)(bounds.absY + 5));
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        AssertPreview(both);
        var popup = tooltip.Popup;
        inventory[6].Itemstack = null;
        menu.NativeGridDirty();
        f.Lifecycle.Drain();
        Assert.Equal(new[] { both, one, unrelated }, session.Rows.Select(row => row.Recipe.Recipe));
        Assert.Equal(1, session.Rows[0].GridMatches);
        AssertPreview(both);
        Assert.NotSame(popup, tooltip.Popup);
        Assert.Contains(tooltip.Popup!.GetRichtext("recipe").Components.OfType<RichTextComponent>(),
            component => component.DisplayText == "0/4");
        tiles.UpdateHover(-1, -1);
        Assert.Equal(new[] { one, both, unrelated }, session.Rows.Select(row => row.Recipe.Recipe));
        var sort = menu.SingleComposer.GetDropDown("sort");
        menu.SingleComposer.FocusElement(sort.TabIndex);
        menu.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.End });
        inventory[6].Itemstack = stick;
        menu.NativeGridDirty();
        f.Lifecycle.Drain();
        Assert.Equal("name", sort.SelectedValue);
        Assert.Equal(new[] { one, both, unrelated }, session.Rows.Select(row => row.Recipe.Recipe));
        menu.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Home });
        Assert.Equal(new[] { both, one, unrelated }, session.Rows.Select(row => row.Recipe.Recipe));
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
        inventory[6].Itemstack = null;
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        Assert.Equal(new[] { one, both, unrelated }, session.Rows.Select(row => row.Recipe.Recipe));
        Assert.Equal(1, session.Rows.Single(row => row.Recipe.Recipe == both).GridMatches);
        Assert.Equal(1, wood.StackSize);
        Assert.Equal(1, stick.StackSize);
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);

        void AssertPreview(GridRecipe expected)
        {
            var grid = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components.OfType<SlideshowGridRecipeTextComponent>());
            Assert.Equal(expected.Name, Assert.Single(grid.GridRecipesAndUnnamedIngredients).Recipe!.Name);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void BrowsingStateSurvivesInventoryReopeningAndComposerReplacement(float scale, bool recomposeParent)
    {
        // Arrange a populated dialog and establish non-default search, category, sort, selection, and scroll.
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        foreach (var recipe in f.Lifecycle.Recipes)
            recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        for (int i = 0; i < 80; i++)
        {
            var recipe = f.Lifecycle.F.Catalogue.Recipe("other-" + i);
            recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
            f.Lifecycle.Recipes.Add(recipe);
        }
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        if (recomposeParent) f.Parent.Opening = f.Recompose;
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        Assert.Equal("", menu.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(0, GetCategoryTabs(menu.SingleComposer).activeElement);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        Assert.Equal("availability", menu.SingleComposer.GetDropDown("sort").SelectedValue);
        var sort = menu.SingleComposer.GetDropDown("sort");
        sort.OnFocusGained();
        sort.OnKeyDown(f.Lifecycle.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.End });
        sort.OnFocusLost();
        menu.SingleComposer.GetTextInput("search").SetValue(" output ");
        var tabs = GetCategoryTabs(menu.SingleComposer);
        int category = Array.FindIndex(tabs.tabs, tab => tab.Name == "tools");
        tabs.SetValue(category);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(73 * scale);
        Assert.Equal(73, tiles.ScrollPosition);
        int? firstOutput = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]).Selection?.Output.Id;
        Assert.NotNull(firstOutput);

        // Act: resize and replace this dialog's composer without replacing its session.
        var composer = menu.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 10);
        menu.TryOpen();
        Assert.NotSame(composer, menu.SingleComposer);

        // Assert recomposition keeps the current dialog's browsing state and session.
        AssertBrowsingState(menu);

        // Act: close and reopen the parent inventory; each reopening creates a new dialog.
        for (int i = 0; i < 3; i++)
        {
            f.Parent.TryClose();
            Assert.Empty(f.Menus);
            f.Parent.TryOpen();
            var reopened = Assert.Single(f.Menus);
            Assert.NotSame(menu, reopened);

            // Assert each replacement dialog restores the same browsing state.
            AssertBrowsingState(reopened);
            f.Lifecycle.Drain();
            Assert.Same(reopened, Assert.Single(f.Menus));
            menu = reopened;
        }

        // Act: explicitly close the book, then reopen the inventory before toggling it back on.
        f.ClickCloseButton(menu);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        f.Lifecycle.Drain();
        Assert.Empty(f.Menus);
        f.Toggle();
        menu = Assert.Single(f.Menus);

        // Assert explicit-close state and browsing-state restoration remain independent.
        AssertBrowsingState(menu);

        // Act: update browsing state once more, then reopen the inventory to verify persistence.
        menu.SingleComposer.GetTextInput("search").SetValue("no matching recipe");
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        Assert.Equal("no matching recipe", menu.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(category, GetCategoryTabs(menu.SingleComposer).activeElement);
        Assert.Equal("No recipes match your search in this category.", menu.SingleComposer.GetDynamicText("empty").GetText());
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);

        void AssertBrowsingState(GuiDialogRecipeBook current)
        {
            Assert.Equal(" output ", current.SingleComposer.GetTextInput("search").GetText());
            Assert.Equal("name", current.SingleComposer.GetDropDown("sort").SelectedValue);
            Assert.Equal(category, GetCategoryTabs(current.SingleComposer).activeElement);
            var currentTiles = Assert.IsType<GuiElementRecipeBookTiles>(current.SingleComposer.GetElement("tiles"));
            Assert.Equal(73, currentTiles.ScrollPosition);
            Assert.Equal(73 * scale, current.SingleComposer.GetScrollbar("scroll").CurrentYPosition, 4);
            Assert.Equal(firstOutput, Assert.IsType<GuiElementRecipeBookTile>(currentTiles.Elements[0]).Selection?.Output.Id);
        }
    }

    [Fact]
    public void RestoredScrollClampsToEvaluatedResultsAndWorldExitClearsBrowsingState()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        using (var menu = new GuiDialogRecipeBook(f.Lifecycle.Api.Object, f.Parent,
            f.Parent.Composers["maininventory"], f.Lifecycle.System.Catalogue!, f.Lifecycle.F.Allocator,
            f.Lifecycle.System.IconCache!, new(null, "output", 500)))
        {
            Assert.True(menu.TryOpen());
            Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());
            Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
            Assert.NotNull(Assert.IsType<GuiElementRecipeBookTile>(
                Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles")).Elements[0]).Selection);
            menu.TryClose();
        }
        f.Toggle();
        var current = Assert.Single(f.Menus);
        current.SingleComposer.GetTextInput("search").SetValue("not found");
        current.SingleComposer.GetDropDown("sort").OnFocusGained();
        current.SingleComposer.GetDropDown("sort").OnKeyDown(f.Lifecycle.Api.Object,
            new KeyEvent { KeyCode = (int)GlKeys.End });
        f.Lifecycle.Events.Raise(value => value.LeaveWorld += null);
        Assert.Empty(f.Menus);
        f.Parent.TryClose();
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        if (!f.Menus.Any()) f.Toggle();
        current = Assert.Single(f.Menus);
        Assert.Equal("", current.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal("availability", current.SingleComposer.GetDropDown("sort").SelectedValue);
        Assert.Equal(0, GetCategoryTabs(current.SingleComposer).activeElement);
        Assert.Equal(0, current.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CraftingCatalogue.OtherCategory)]
    public void FailedReopenDoesNotReplaceBrowsingStateAndClearedSearchIsRemembered(string? category)
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var tabs = GetCategoryTabs(menu.SingleComposer);
        int index = category == null ? 0 : tabs.tabs.Length - 1;
        tabs.SetValue(index);
        menu.SingleComposer.GetDropDown("sort").OnFocusGained();
        menu.SingleComposer.GetDropDown("sort").OnKeyDown(f.Lifecycle.Api.Object,
            new KeyEvent { KeyCode = (int)GlKeys.End });
        menu.SingleComposer.GetTextInput("search").SetValue("not found");
        f.Parent.TryClose();
        f.ResizePanelSpace(0, 0);
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
        f.Parent.TryClose();
        f.ResizePanelSpace(320, 420);
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        Assert.Equal("not found", menu.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(index, GetCategoryTabs(menu.SingleComposer).activeElement);
        Assert.Equal("name", menu.SingleComposer.GetDropDown("sort").SelectedValue);
        menu.SingleComposer.GetTextInput("search").SetValue("");
        f.Parent.TryClose();
        var grid = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        grid[0].Itemstack = Assert.Single(f.Lifecycle.Recipes).ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        Assert.Equal("", menu.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(index, GetCategoryTabs(menu.SingleComposer).activeElement);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        Assert.True(Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]).Selection!.Evaluation.CanCraft);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AutomaticPanelCleanupDoesNotForgetTheOpenPreference(int closeMode)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        if (closeMode == 0) menu.CloseWithParent();
        else if (closeMode == 1) menu.TryClose();
        else menu.Dispose();
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.AssertToggleOpen();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitlyClosingTheMenuKeepsItClosedWhenTheParentReopens(bool toggleOff)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        menu.SingleComposer.GetTextInput("search").SetValue("remembered search");
        if (toggleOff) f.Toggle();
        else f.ClickCloseButton(menu);
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        Assert.False(f.ToggleButton.On);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
        f.Toggle();
        var reopened = Assert.Single(f.Menus);
        Assert.True(reopened.IsOpened());
        Assert.Equal("remembered search", reopened.SingleComposer.GetTextInput("search").GetText());
        f.AssertToggleOpen();
    }

    [Fact]
    public void RecompositionRestoresOnlyTheLatestComposerAndPreservesOpenPreference()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var first = Assert.Single(f.Menus);
        f.Recompose();
        f.Recompose();
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        f.Lifecycle.Drain();
        var restored = Assert.Single(f.Menus);
        Assert.NotSame(first, restored);
        f.AssertToggleOpen();
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.True(Assert.Single(f.Menus).IsOpened());
    }
}
