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
public sealed class RecipeBookDialogFeedbackTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(1.5, false)]
    [InlineData(1.5, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void PointerLeavingRecipeHidesTooltipDespiteClickFocusAndKeyboardCanRestoreIt(float scale, bool gap)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        Mock.Get(f.Lifecycle.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var tile = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        menu.SingleComposer.FocusElement(tiles.TabIndex);
        int x = (int)(tile.Bounds.absX + 5);
        int y = (int)(tile.Bounds.absY + 5);
        int outsideX = gap ? (int)(tile.Bounds.absX + 47 * scale) : -1;
        int outsideY = gap ? y : -1;
        Move(x, y);
        AssertShown();
        menu.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.True(tile.Pressed);
        Move(outsideX, outsideY);
        AssertHidden();
        menu.OnMouseUp(new MouseEvent(outsideX, outsideY, EnumMouseButton.Left, 0));
        Assert.False(tile.Pressed);
        AssertHidden();
        Move(x, y);
        AssertShown();
        menu.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.True(tiles.HasFocus);
        Move(outsideX, outsideY);
        AssertHidden();
        menu.NativeGridDirty();
        f.Lifecycle.Drain();
        AssertHidden();
        var key = new KeyEvent { KeyCode = (int)GlKeys.Right };
        menu.OnKeyDown(key);
        Assert.True(key.Handled);
        AssertShown();
        Move(outsideX, outsideY);
        AssertHidden();
        Move(x, y);
        AssertShown();

        void Move(int mouseX, int mouseY)
        {
            f.SetMouse(mouseX, mouseY);
            menu.OnMouseMove(new MouseEvent(mouseX, mouseY, EnumMouseButton.None, 0));
        }

        void AssertShown()
        {
            render.Invocations.Clear();
            tooltip.RenderInteractiveElements(.1f);
            render.Verify(value => value.GlPushMatrix(), Times.Once);
        }

        void AssertHidden()
        {
            render.Invocations.Clear();
            tooltip.RenderInteractiveElements(.1f);
            Assert.Empty(render.Invocations);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void EmptyFeedbackClearsOnlyTheRelevantFilterAndTracksLiveInventory(float scale, bool keyboard)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = GetCategoryTabs(menu.SingleComposer);
        tabs.SetValue(Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));
        var input = menu.SingleComposer.GetTextInput("search");
        input.SetValue("output");
        AssertState("", null);
        f.ToggleCraftable(menu);
        AssertState("Recipes match your search, but none are currently craftable.", "show-uncraftable");
        input.SetValue("no matching recipe");
        AssertState("No recipes match your search in this category.", "clear-search");
        Invoke("clear-search");
        Assert.Equal("", input.GetText());
        Assert.True(input.HasFocus);
        Assert.True(session.CraftableOnly.Enabled);
        Assert.True(menu.SingleComposer.GetSwitch("craftable-only").On);
        AssertState("No recipes in this category are currently craftable.", "show-uncraftable");
        Invoke("show-uncraftable");
        Assert.False(session.CraftableOnly.Enabled);
        Assert.False(menu.SingleComposer.GetSwitch("craftable-only").On);
        Assert.Single(session.Rows);
        Assert.Equal("tools", session.Category);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        AssertState("", null);
        input.SetValue(" output ");
        f.ToggleCraftable(menu);
        Invoke("show-uncraftable");
        Assert.Equal(" output ", input.GetText());
        Assert.Single(session.Rows);
        AssertState("", null);
        f.ToggleCraftable(menu);
        var show = menu.SingleComposer.GetButton("show-uncraftable");
        Assert.True(menu.SingleComposer.FocusElement(show.TabIndex));
        var grid = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        grid[0].Itemstack = recipe.ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        session.Refresh();
        Assert.Single(session.Rows);
        Assert.False(show.HasFocus);
        AssertState("", null);
        grid[0].Itemstack = null;
        session.Refresh();
        AssertState("Recipes match your search, but none are currently craftable.", "show-uncraftable");
        var composer = menu.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 10);
        menu.TryOpen();
        Assert.NotSame(composer, menu.SingleComposer);
        AssertState("Recipes match your search, but none are currently craftable.", "show-uncraftable");
        f.Parent.TryClose();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        AssertState("", null);
        menu.SingleComposer.GetTextInput("search").SetValue("no matching recipe");
        AssertState("No recipes match your search in this category.", "clear-search");
        Assert.Equal("tools", ((RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!).Category);

        void Invoke(string key)
        {
            var button = menu.SingleComposer.GetButton(key);
            if (!keyboard) f.Click(button);
            else
            {
                Assert.True(menu.SingleComposer.FocusElement(button.TabIndex));
                var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
                menu.OnKeyDown(enter);
                Assert.True(enter.Handled);
            }
        }

        void AssertState(string message, string? action)
        {
            Assert.Equal(message, menu.SingleComposer.GetDynamicText("empty").GetText());
            foreach (string key in new[] { "clear-search", "show-uncraftable" })
            {
                var button = menu.SingleComposer.GetButton(key);
                Assert.Equal(key == action, button.Visible);
                Assert.Equal(key == action, button.Enabled);
                Assert.Equal(key == action, button.Focusable);
                if (key == action) continue;
                Assert.False(button.HasFocus);
                var render = Mock.Get(f.Lifecycle.Api.Object.Render);
                render.Invocations.Clear();
                button.RenderInteractiveElements(.1f);
                Assert.Empty(render.Invocations);
                f.Click(button, handled: false);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenPinnedSelectionDisablesFooterAndClearsTooltipWithoutLosingPin(bool craftable)
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        var tile = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        tiles.UpdateHover((int)tile.Bounds.absX + 10, (int)tile.Bounds.absY + 10);
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        Assert.NotNull(tooltip.Popup);
        Assert.True(menu.SingleComposer.FocusElement(menu.SingleComposer.GetButton("handbook").TabIndex));
        using var filter = craftable ? null : session.Filters.Register("test:hidden", new HiddenFilter());
        if (craftable) f.ToggleCraftable(menu);
        else session.Refresh();
        Assert.Empty(session.Rows);
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        tooltip.RenderInteractiveElements(.1f);
        Assert.Empty(render.Invocations);
        Assert.Equal("", menu.SingleComposer.GetDynamicText("alternative").GetText());
        foreach (string key in new[] { "previous", "auto", "next", "handbook" })
        {
            Assert.False(menu.SingleComposer.GetButton(key).Enabled);
            Assert.False(menu.SingleComposer.GetButton(key).Focusable);
            Assert.False(menu.SingleComposer.GetButton(key).HasFocus);
            f.Click(menu.SingleComposer.GetButton(key), handled: false);
        }
        if (craftable) f.ToggleCraftable(menu);
        else
        {
            filter!.Dispose();
            session.Refresh();
        }
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Equal(row.Recipe.Id, Assert.Single(session.Rows).Recipe.Id);
        Assert.NotNull(tooltip.Popup);
        foreach (string key in new[] { "previous", "auto", "next", "handbook" })
            Assert.True(menu.SingleComposer.GetButton(key).Enabled);
        f.Click(menu.SingleComposer.GetButton("auto"));
        Assert.False(Assert.Single(session.Rows).IsExplicit);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void HandbookHintAppearsOverEnabledAndDisabledButtonAndFollowsResize(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        Assert.False(menu.SingleComposer.GetButton("handbook").Enabled);
        AssertHint();
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        session.Select(session.Rows[0].Output.Id);
        Assert.True(menu.SingleComposer.GetButton("handbook").Enabled);
        AssertHint();
        var composer = menu.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth + 40, composer.Bounds.fixedHeight - 10);
        menu.TryOpen();
        Assert.NotSame(composer, menu.SingleComposer);
        AssertHint();

        void AssertHint()
        {
            var composer = menu.SingleComposer;
            var button = composer.GetButton("handbook");
            var hint = composer.GetHoverText("handbook-hint");
            Assert.Equal(button.Bounds.absX, hint.Bounds.absX, 4);
            Assert.Equal(button.Bounds.absY, hint.Bounds.absY, 4);
            Assert.Equal(button.Bounds.OuterWidth, hint.Bounds.OuterWidth, 4);
            Assert.Equal(button.Bounds.OuterHeight, hint.Bounds.OuterHeight, 4);
            f.SetMouse((int)(button.Bounds.absX + button.Bounds.OuterWidth / 2),
                (int)(button.Bounds.absY + button.Bounds.OuterHeight / 2));
            hint.RenderInteractiveElements(0);
            Assert.True(hint.IsNowShown);
            f.SetMouse((int)(hint.Bounds.absX - GuiElement.scaled(5)),
                (int)(hint.Bounds.absY + hint.Bounds.OuterHeight / 2));
            hint.RenderInteractiveElements(0);
            Assert.False(hint.IsNowShown);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void CraftableHintAppearsOverCheckboxAndLabelAndHidesOutside(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        f.Toggle();
        var composer = Assert.Single(f.Menus).SingleComposer;
        var hint = composer.GetHoverText("craftable-hint");
        var input = Mock.Get(f.Lifecycle.Api.Object.Input);
        foreach (var bounds in new[]
        {
            composer.GetSwitch("craftable-only").Bounds,
            composer.GetElement("craftable-label").Bounds
        })
        {
            input.SetupGet(value => value.MouseX).Returns((int)(bounds.absX + bounds.OuterWidth / 2));
            input.SetupGet(value => value.MouseY).Returns((int)(bounds.absY + bounds.OuterHeight / 2));
            hint.RenderInteractiveElements(0);
            Assert.True(hint.IsNowShown);
        }
        input.SetupGet(value => value.MouseX).Returns((int)(hint.Bounds.absX - GuiElement.scaled(5)));
        hint.RenderInteractiveElements(0);
        Assert.False(hint.IsNowShown);
        Assert.False(composer.GetSwitch("craftable-only").On);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SortHintRetainsLocalizedLabelOverDropdownAndHidesOutside(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        f.Toggle();
        var composer = Assert.Single(f.Menus).SingleComposer;
        var hint = composer.GetHoverText("sort-hint");
        var input = Mock.Get(f.Lifecycle.Api.Object.Input);
        Assert.Null(composer.GetElement("sort-label"));
        Assert.StartsWith("Sort by: ", hint.GetText());
        var bounds = composer.GetDropDown("sort").Bounds;
        input.SetupGet(value => value.MouseX).Returns((int)(bounds.absX + bounds.OuterWidth / 2));
        input.SetupGet(value => value.MouseY).Returns((int)(bounds.absY + bounds.OuterHeight / 2));
        hint.RenderInteractiveElements(0);
        Assert.True(hint.IsNowShown);
        input.SetupGet(value => value.MouseX).Returns((int)(hint.Bounds.absX - GuiElement.scaled(5)));
        hint.RenderInteractiveElements(0);
        Assert.False(hint.IsNowShown);
    }

    [Fact]
    public void AlternativeCounterHasNoMarkerWhenRecipeIsManuallySelected()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        Assert.Equal("1/1", menu.SingleComposer.GetDynamicText("alternative").GetText());
        f.Click(menu.SingleComposer.GetElement("next"));
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Equal("1/1", menu.SingleComposer.GetDynamicText("alternative").GetText());
        f.Click(menu.SingleComposer.GetElement("auto"));
        Assert.False(Assert.Single(session.Rows).IsExplicit);
        Assert.Equal("1/1", menu.SingleComposer.GetDynamicText("alternative").GetText());
    }
}
