using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.CraftingMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
[Collection("Crafting native GUI")]
public sealed class CraftingMenuLifecycleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void InlineSearchClearUsesNativeEditingAndPreservesOtherCriteriaAcrossResizeAndReopen(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        Assert.Single(f.Lifecycle.Recipes).Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var input = Assert.IsType<GuiElementCraftingSearch>(menu.SingleComposer.GetTextInput("search"));
        Assert.False(input.ClearVisible);
        AssertIcon(false);
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        tabs.SetValue(Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        input.SetValue(new string('x', 400));
        Assert.Empty(session.Rows);
        Assert.True(input.ClearVisible);
        AssertIcon(true);
        Assert.True(input.Bounds.absX + input.Bounds.OuterWidth + scale <= input.ClearBounds.absX);
        Assert.True(input.ClearBounds.absX + input.ClearBounds.OuterWidth < input.FieldBounds.absX + input.FieldBounds.OuterWidth);
        Assert.Equal(input.FieldBounds.absY + input.FieldBounds.OuterHeight / 2,
            input.ClearBounds.absY + input.ClearBounds.OuterHeight / 2, 4);
        Assert.True(menu.SingleComposer.FocusElement(tabs.TabIndex));
        ClickClear();
        Assert.Equal("", input.GetText());
        Assert.False(input.ClearVisible);
        Assert.True(input.HasFocus);
        Assert.True(menu.CaptureAllInputs());
        Assert.Equal("tools", session.Category);
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(row.Allocation, Assert.Single(session.Rows).Allocation);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        AssertIcon(false);
        f.ToggleCraftable(menu);
        input.SetValue("no matching recipe");
        ClickClear();
        Assert.True(session.CraftableOnly.Enabled);
        Assert.True(menu.SingleComposer.GetToggleButton("craftable-only").On);
        Assert.Equal(CraftingMenuEmptyState.NoCraftableRecipes, session.EmptyState);
        input.SetValue(" \t ");
        Assert.True(input.ClearVisible);
        ClickClear();
        Assert.Equal("", input.GetText());
        input.SetValue("output");
        for (int i = 0; i < 3; i++)
        {
            int textures = f.TextureCount;
            var composer = menu.SingleComposer;
            f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 5);
            menu.TryOpen();
            input = Assert.IsType<GuiElementCraftingSearch>(menu.SingleComposer.GetTextInput("search"));
            Assert.NotSame(composer, menu.SingleComposer);
            Assert.Equal(textures, f.TextureCount);
            Assert.Equal("output", input.GetText());
            Assert.True(input.ClearVisible);
            AssertIcon(true);
        }
        f.Parent.TryClose();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        input = Assert.IsType<GuiElementCraftingSearch>(menu.SingleComposer.GetTextInput("search"));
        Assert.Equal("output", input.GetText());
        Assert.True(input.ClearVisible);
        ClickClear();
        Assert.False(input.ClearVisible);
        Assert.Equal("", input.GetText());

        void ClickClear()
        {
            int x = (int)(input.ClearBounds.absX + input.ClearBounds.OuterWidth / 2);
            int y = (int)(input.ClearBounds.absY + input.ClearBounds.OuterHeight / 2);
            var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            menu.OnMouseDown(down);
            Assert.True(down.Handled);
            var up = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            menu.OnMouseUp(up);
            Assert.True(up.Handled);
        }

        void AssertIcon(bool visible)
        {
            var render = Mock.Get(f.Lifecycle.Api.Object.Render);
            render.Invocations.Clear();
            input.RenderInteractiveElements(.1f);
            Assert.Equal(visible ? 1 : 0, render.Invocations.Count(call =>
                call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
                && call.Arguments.Count > 1 && ReferenceEquals(call.Arguments[1], input.ClearBounds)));
            var scissor = Assert.Single(render.Invocations, call => call.Method.Name == nameof(IRenderAPI.GlScissor));
            Assert.True((int)scissor.Arguments[0] + (int)scissor.Arguments[2] <= input.ClearBounds.absX);
            if (visible) return;
            Assert.False(input.ClearHint.IsVisible);
            Assert.False(input.ClearHint.IsNowShown);
        }
    }

    [Theory]
    [InlineData("release-only")]
    [InlineData("right-button")]
    [InlineData("outside-release")]
    [InlineData("changed-text")]
    [InlineData("disabled")]
    [InlineData("focus-lost")]
    public void InlineSearchClearRequiresAnUninterruptedLeftClick(string action)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var input = Assert.IsType<GuiElementCraftingSearch>(menu.SingleComposer.GetTextInput("search"));
        input.SetValue("original");
        Assert.True(menu.SingleComposer.FocusElement(input.TabIndex));
        int x = (int)(input.ClearBounds.absX + input.ClearBounds.OuterWidth / 2);
        int y = (int)(input.ClearBounds.absY + input.ClearBounds.OuterHeight / 2);
        var button = action == "right-button" ? EnumMouseButton.Right : EnumMouseButton.Left;
        if (action != "release-only")
            input.OnMouseDown(f.Lifecycle.Api.Object, new MouseEvent(x, y, button, 0));
        switch (action)
        {
            case "outside-release": x = (int)input.FieldBounds.absX - 10; break;
            case "changed-text": input.SetValue("updated"); break;
            case "disabled": input.Enabled = false; input.Enabled = true; break;
            case "focus-lost": input.OnFocusLost(); break;
        }
        input.OnMouseUp(f.Lifecycle.Api.Object, new MouseEvent(x, y, button, 0));
        Assert.Equal(action == "changed-text" ? "updated" : "original", input.GetText());
    }

    private static void AssertEmptyStateCentered(GuiDialogCraftingMenu menu)
    {
        var composer = menu.SingleComposer;
        var empty = composer.GetDynamicText("empty");
        Assert.Equal(EnumTextOrientation.Center, empty.Font.Orientation);
        if (empty.GetText().Length == 0) return;
        var viewport = composer.GetElement("tiles").InsideClipBounds;
        double centerX = viewport.absX + viewport.InnerWidth / 2;
        Assert.Equal(centerX, empty.Bounds.absX + empty.Bounds.InnerWidth / 2, 4);
        double bottom = empty.Bounds.absY + empty.Bounds.OuterHeight;
        foreach (string key in new[] { "clear-search", "show-uncraftable" })
        {
            var button = composer.GetButton(key);
            if (!button.Visible) continue;
            Assert.Equal(centerX, button.Bounds.absX + button.Bounds.OuterWidth / 2, 4);
            Assert.Equal(bottom + GuiElement.scaled(8), button.Bounds.absY, 4);
            bottom = button.Bounds.absY + button.Bounds.OuterHeight;
        }
        Assert.Equal(viewport.absY + viewport.InnerHeight / 2, (empty.Bounds.absY + bottom) / 2, 4);
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
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
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
        Assert.True(menu.SingleComposer.GetToggleButton("craftable-only").On);
        AssertState("No recipes in this category are currently craftable.", "show-uncraftable");
        Invoke("show-uncraftable");
        Assert.False(session.CraftableOnly.Enabled);
        Assert.False(menu.SingleComposer.GetToggleButton("craftable-only").On);
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
        Assert.Equal("tools", ((CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
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
            AssertEmptyStateCentered(menu);
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HandbookButtonOpensSelectedOutputUsingNativePageCodesWithoutPlacing(bool customPage, bool keyboard)
    {
        using var f = new MenuFixture(includeRecipes: true);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        var output = recipe.Output!.ResolvedItemStack!;
        output.Attributes.SetString("wood", "oak");
        var provider = new HandbookPageBehavior(output.Collectible);
        if (customPage) output.Collectible.CollectibleBehaviors = [provider];
        f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-output"));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        var links = new List<string>();
        using var handbook = new HandbookDialog(f.Lifecycle.Api.Object);
        f.Lifecycle.Api.SetupGet(api => api.LinkProtocols).Returns(
            new Dictionary<string, Action<LinkTextComponent>> { ["handbook"] = link =>
            {
                links.Add(link.Href);
                if (!handbook.IsOpened()) handbook.TryOpen();
            } });
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var button = menu.SingleComposer.GetButton("handbook");
        Assert.False(button.Enabled);
        f.Click(button, handled: false);
        Assert.Empty(links);
        var row = Assert.Single(session.Rows, row => row.Recipe.Recipe == recipe);
        session.Select(row.Output.Id);
        Assert.True(button.Enabled);
        var network = Mock.Get(f.Lifecycle.Api.Object.Network);
        network.Invocations.Clear();
        if (keyboard)
        {
            Assert.True(menu.SingleComposer.FocusElement(button.TabIndex));
            var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
            menu.OnKeyDown(enter);
            Assert.True(enter.Handled);
        }
        else f.Click(button);
        Assert.Equal(customPage ? "handbook://custom-output-oak"
            : "handbook://" + GuiHandbookItemStackPage.PageCodeForStack(row.Recipe.GetPreview()!), Assert.Single(links));
        Assert.Equal(handbook.DrawOrder, menu.DrawOrder);
        Assert.True(handbook.IsOpened());
        var gui = Mock.Get(f.Lifecycle.Api.Object.Gui);
        gui.Verify(api => api.RequestFocus(handbook), Times.Exactly(2));
        Assert.True(f.Parent.IsOpened());
        Assert.True(menu.IsOpened());
        Assert.Equal(row.Output.Id, session.SelectedOutput);
        Assert.Equal("oak", row.Recipe.GetPreview()!.Attributes.GetString("wood"));
        Assert.False(row.Recipe.GetPreview()!.Attributes.HasAttribute("provider-mutation"));
        Assert.False(output.Attributes.HasAttribute("provider-mutation"));
        Assert.Empty(network.Invocations);
        if (customPage) Assert.NotSame(output, provider.Stack);
        var composer = menu.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 10);
        menu.TryOpen();
        Assert.NotSame(composer, menu.SingleComposer);
        button = menu.SingleComposer.GetButton("handbook");
        Assert.True(button.Enabled);
        f.Click(button);
        Assert.Equal(2, links.Count);
        gui.Verify(api => api.RequestFocus(handbook), Times.Exactly(3));
        session.Select(session.Rows.Single(row => row.Recipe.Recipe != recipe).Output.Id);
        f.Click(button);
        Assert.Equal("handbook://" + GuiHandbookItemStackPage.PageCodeForStack(
            session.Rows.Single(row => row.Output.Id == session.SelectedOutput).Recipe.GetPreview()!), links[^1]);
    }

    private sealed class HandbookDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => "handbook";
        public override double DrawOrder => 0.2;
    }

    private sealed class HandbookPageBehavior(CollectibleObject collectible)
        : CollectibleBehavior(collectible), IHandBookPageCodeProvider
    {
        internal ItemStack? Stack;
        public string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
        {
            Stack = stack;
            stack.Attributes.SetBool("provider-mutation", true);
            return "custom-output-" + stack.Attributes.GetString("wood");
        }
    }

    [Fact]
    public void HandbookButtonReportsUnavailableNativeHandler()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        session.Select(Assert.Single(session.Rows).Output.Id);
        f.Lifecycle.Api.SetupGet(api => api.LinkProtocols).Returns(new Dictionary<string, Action<LinkTextComponent>>());
        f.Click(menu.SingleComposer.GetButton("handbook"));
        f.Lifecycle.Api.Verify(api => api.TriggerIngameError(menu, "craftingmenu",
            "vanillaexpanded:craftingmenu-handbook-unavailable"), Times.Once);
    }

    [Fact]
    public void HandbookButtonIsDisabledForSelectedOutputWithoutResolvedPreview()
    {
        using var f = new MenuFixture(includeRecipes: true);
        Assert.Single(f.Lifecycle.Recipes).Output!.ResolvedItemStack = null;
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        session.Select(Assert.Single(session.Rows).Output.Id);
        Assert.False(menu.SingleComposer.GetButton("handbook").Enabled);
        Assert.False(menu.SingleComposer.GetButton("handbook").Focusable);
        f.Click(menu.SingleComposer.GetButton("handbook"), handled: false);
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
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        var tiles = Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles"));
        var tile = Assert.IsType<GuiElementCraftingTile>(tiles.Elements[0]);
        tiles.UpdateHover((int)tile.Bounds.absX + 10, (int)tile.Bounds.absY + 10);
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
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

    private sealed class HiddenFilter : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => false;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NativeCraftableTogglePreservesIndependentCriteriaAcrossInputResizeAndSessionReset(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var first = Assert.Single(f.Lifecycle.Recipes);
        first.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        for (int i = 0; i < 41; i++)
        {
            var recipe = f.Lifecycle.F.Catalogue.Recipe("other-" + i);
            recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = [i == 40 ? "construction" : "tools"];
            f.Lifecycle.Recipes.Add(recipe);
        }
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        var inventories = ((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager).Inventories;
        var grid = Assert.Single(inventories.Values.OfType<InventoryCraftingGrid>());
        grid[0].Itemstack = first.ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var toggle = menu.SingleComposer.GetToggleButton("craftable-only");
        Assert.False(toggle.On);
        Assert.False(session.CraftableOnly.Enabled);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        tabs.SetValue(Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));
        Assert.Equal(41, session.Rows.Length);
        var tiles = Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(48 * scale);
        Assert.True(menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);
        f.ToggleCraftable(menu);
        Assert.True(toggle.On);
        Assert.True(session.CraftableOnly.Enabled);
        Assert.Single(session.Rows);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        AssertCriteria();
        Assert.True(menu.SingleComposer.FocusElement(toggle.TabIndex));
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        menu.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.False(toggle.On);
        Assert.Equal(41, session.Rows.Length);
        AssertCriteria();
        enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        menu.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.True(toggle.On);
        Assert.Single(session.Rows);
        for (int i = 0; i < 3; i++)
        {
            var composer = menu.SingleComposer;
            f.ResizePanelSpace(composer.Bounds.fixedWidth + 20, composer.Bounds.fixedHeight - 10);
            menu.TryOpen();
            Assert.NotSame(composer, menu.SingleComposer);
            Assert.True(menu.SingleComposer.GetToggleButton("craftable-only").On);
            Assert.True(session.CraftableOnly.Enabled);
            Assert.Single(session.Rows);
            AssertCriteria();
            Assert.Throws<ArgumentException>(() =>
                session.Filters.Register("vanillaexpanded:craftable-only", new CraftableFilter()));
        }
        for (int i = 0; i < 3; i++)
        {
            f.Parent.TryClose();
            Assert.Empty(f.Menus);
            Assert.Throws<ObjectDisposedException>(() => session.Filters.Filter([]));
            f.Parent.TryOpen();
            var reopened = Assert.Single(f.Menus);
            var newSession = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
            Assert.NotSame(session, newSession);
            Assert.False(newSession.CraftableOnly.Enabled);
            Assert.False(reopened.SingleComposer.GetToggleButton("craftable-only").On);
            Assert.Equal("output", reopened.SingleComposer.GetTextInput("search").GetText());
            Assert.Equal("tools", newSession.Category);
            Assert.Equal(41, newSession.Rows.Length);
            session = newSession;
            if (i < 2)
            {
                f.ToggleCraftable(reopened);
                Assert.True(session.CraftableOnly.Enabled);
                Assert.Single(session.Rows);
            }
        }
        using var identity = session.Filters.Register("vanillaexpanded:craftable-only", new CraftableFilter());

        void AssertCriteria()
        {
            Assert.Equal("tools", session.Category);
            Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RecipeTooltipUsesNativeSelectedGridWithTitleAndReleasesReplacedPreviews(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var first = Assert.Single(f.Lifecycle.Recipes);
        first.Output!.Quantity = 4;
        var second = f.Lifecycle.F.Recipe(f.Lifecycle.F.Ingredient(f.Lifecycle.F.Stack(2, "stone", 1)));
        second.Output = first.Output.Clone();
        f.Lifecycle.Recipes.Add(second);
        f.Lifecycle.F.Register(first, second);
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        int texturesBefore = f.TextureCount;
        tooltip.Inspect(row);
        var popup = tooltip.Popup!;
        var content = popup.GetRichtext("recipe");
        var titleElement = popup.GetRichtext("title");
        var title = Assert.IsType<RichTextComponent>(Assert.Single(titleElement.Components));
        Assert.Equal(row.Recipe.DisplayName + " (x4)", title.DisplayText);
        Assert.Equal(FontWeight.Bold, title.Font.FontWeight);
        Assert.Equal(CairoFont.WhiteSmallText().UnscaledFontsize, title.Font.UnscaledFontsize);
        Assert.Equal(EnumTextOrientation.Center, title.Font.Orientation);
        Assert.All(title.Lines, line => Assert.Equal(content.Bounds.InnerWidth / 2,
            line.Bounds.X + (line.LeftSpace + line.RightSpace + line.Bounds.Width) / 2, precision: 6));
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.Equal(content.Bounds.InnerWidth / 2, grid.BoundsPerLine[0].X + 126 * scale / 2, precision: 6);
        var preview = Assert.Single(grid.GridRecipesAndUnnamedIngredients).Recipe!;
        Assert.NotSame(row.Recipe.Recipe, preview);
        Assert.Equal(row.Recipe.Recipe.Name, preview.Name);
        Assert.NotSame(row.Recipe.Recipe.ResolvedIngredients![0], preview.ResolvedIngredients![0]);
        Assert.True(titleElement.Bounds.absY + title.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height) <=
            content.Bounds.absY + grid.BoundsPerLine[0].Y);
        Assert.IsType<GuiElementInset>(popup.GetElement("divider"));
        var background = Assert.IsType<GuiElementCustomDraw>(popup.GetElement("background"));
        Assert.Equal(popup.Bounds.absX, background.Bounds.absX, precision: 6);
        Assert.Equal(popup.Bounds.absY, background.Bounds.absY, precision: 6);
        Assert.Equal(popup.Bounds.OuterWidth, background.Bounds.OuterWidth, precision: 6);
        Assert.Equal(popup.Bounds.OuterHeight, background.Bounds.OuterHeight, precision: 6);
        using (var surface = new ImageSurface(Format.Argb32, popup.Bounds.OuterWidthInt, popup.Bounds.OuterHeightInt))
        using (var ctx = new Context(surface))
        using (var expected = new ImageSurface(Format.Argb32, 1, 1))
        using (var expectedCtx = new Context(expected))
        {
            background.ComposeElements(ctx, surface);
            expectedCtx.SetSourceRGBA(GuiStyle.DialogStrongBgColor);
            expectedCtx.Paint();
            surface.Flush();
            expected.Flush();
            var pixels = surface.Data;
            var expectedPixel = expected.Data;
            foreach (int x in new[] { surface.Width / 3, surface.Width * 2 / 3 })
            {
                int offset = surface.Height / 2 * surface.Stride + x * 4;
                for (int channel = 0; channel < 4; channel++)
                    Assert.InRange((int)pixels[offset + channel], expectedPixel[channel] - 1, expectedPixel[channel] + 1);
            }
            int border = surface.Height / 2 * surface.Stride;
            Assert.False(pixels.Skip(border).Take(4).SequenceEqual(expectedPixel));
        }
        int texturesShown = f.TextureCount;
        tooltip.Inspect(row);
        Assert.Same(popup, tooltip.Popup);
        Assert.Equal(texturesShown, f.TextureCount);
        tooltip.Inspect(row with { Evaluation = row.Evaluation with { }, Allocation = row.Allocation! with { } });
        Assert.Same(popup, tooltip.Popup);
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        f.SetMouse((int)(window.absX + window.InnerWidth - 2), (int)(window.absY + window.InnerHeight - 2));
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        tooltip.RenderInteractiveElements(.1f);
        render.Verify(value => value.GlPushMatrix(), Times.Once);
        render.Verify(value => value.GlTranslate(0, 0, 1000), Times.Once);
        render.Verify(value => value.GlPopMatrix(), Times.Once);
        Assert.True(popup.Bounds.absX >= window.absX);
        Assert.True(popup.Bounds.absY >= window.absY);
        Assert.True(popup.Bounds.absX + popup.Bounds.OuterWidth <= window.absX + window.InnerWidth + .001);
        Assert.True(popup.Bounds.absY + popup.Bounds.OuterHeight <= window.absY + window.InnerHeight + .001);
        Assert.Empty(f.Lifecycle.Api.Object.Render.ScissorStack);
        session.Select(row.Output.Id);
        f.Click(menu.SingleComposer.GetElement("next"));
        var selected = Assert.Single(session.Rows);
        Assert.NotSame(row.Recipe, selected.Recipe);
        tooltip.Inspect(selected);
        Assert.NotSame(popup, tooltip.Popup);
        Assert.Equal(texturesShown, f.TextureCount);
        var selectedGrid = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components
            .OfType<SlideshowGridRecipeTextComponent>());
        Assert.Equal(selected.Recipe.Recipe.Name, Assert.Single(selectedGrid.GridRecipesAndUnnamedIngredients).Recipe!.Name);
        tooltip.Inspect(null);
        render.Invocations.Clear();
        tooltip.RenderInteractiveElements(.1f);
        Assert.Empty(render.Invocations);
        tooltip.Inspect(selected);
        render.Setup(value => value.GlTranslate(0, 0, 1000)).Throws<InvalidOperationException>();
        Assert.Throws<InvalidOperationException>(() => tooltip.RenderInteractiveElements(.1f));
        render.Verify(value => value.GlPopMatrix(), Times.Once);
        tooltip.Dispose();
        Assert.Equal(texturesBefore, f.TextureCount);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(1, 204)]
    [InlineData(1.5, 300)]
    [InlineData(1.5, 204)]
    [InlineData(2, 300)]
    [InlineData(2, 204)]
    public void IngredientSummaryWrapsBelowNativeGridAndRefreshesOnlyWhenCoverageChanges(float scale, double width)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var board = f.Lifecycle.F.Stack(1, "board", 3);
        var stick = f.Lifecycle.F.Stack(2, "stick", 2);
        const string name = "Oak planks with a long localized name <font color=\"red\">literal</font>";
        var item = new Mock<MockItem>(board.Id, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        item.Object.Code = board.Item.Code;
        item.Setup(value => value.GetHeldItemName(It.IsAny<ItemStack>())).Returns(name);
        board = new ItemStack(item.Object, board.StackSize);
        var stickItem = new Mock<MockItem>(stick.Id, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        stickItem.Object.Code = stick.Item.Code;
        stickItem.Setup(value => value.GetHeldItemName(It.IsAny<ItemStack>())).Returns("Sticks");
        stick = new ItemStack(stickItem.Object, stick.StackSize);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        recipe.Width = 2;
        recipe.Height = 1;
        recipe.ResolvedIngredients = [f.Lifecycle.F.Ingredient(board, 4), f.Lifecycle.F.Ingredient(stick, 2)];
        var inventory = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        inventory[0].Itemstack = board;
        inventory[1].Itemstack = stick;
        f.Lifecycle.F.Register(recipe);
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = width + 16;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        int baseline = f.TextureCount;
        var tiles = Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles"));
        var tileBounds = tiles.Elements[0].Bounds;
        tiles.UpdateHover((int)(tileBounds.absX + 5), (int)(tileBounds.absY + 5));
        var popup = tooltip.Popup!;
        int shown = f.TextureCount;
        var content = popup.GetRichtext("recipe");
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        var text = content.Components.OfType<RichTextComponent>()
            .Where(component => component is not ClearFloatTextComponent).ToArray();
        Assert.Equal(new[] { name, "3/4", stick.GetName(), "2/2" }, text.Select(component => component.DisplayText));
        Assert.Equal(GuiStyle.ErrorTextColor, text[1].Font.Color);
        Assert.Equal(CairoFont.WhiteSmallText().Color, text[3].Font.Color);
        Assert.True(text[0].Lines.Length > 1);
        Assert.True(text[0].BoundsPerLine.Min(bounds => bounds.Y) >=
            grid.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height));
        Assert.True(text[2].BoundsPerLine.Min(bounds => bounds.Y) >=
            text.Take(2).SelectMany(component => component.BoundsPerLine).Max(bounds => bounds.Y + bounds.Height));
        Assert.All(text.SelectMany(component => component.BoundsPerLine), bounds =>
        {
            Assert.True(bounds.X >= 0);
            Assert.True(bounds.X + bounds.Width <= content.Bounds.InnerWidth + .001);
            Assert.True(content.Bounds.absY + bounds.Y + bounds.Height <=
                popup.Bounds.absY + popup.Bounds.InnerHeight + popup.Bounds.fixedPaddingY * scale + .001);
        });

        board.StackSize = 4;
        session.Refresh();
        Assert.NotSame(popup, tooltip.Popup);
        Assert.Equal(shown, f.TextureCount);
        popup = tooltip.Popup!;
        var fullCount = Assert.Single(popup.GetRichtext("recipe").Components.OfType<RichTextComponent>(),
            component => component.DisplayText == "4/4");
        Assert.Equal(CairoFont.WhiteSmallText().Color, fullCount.Font.Color);

        board.StackSize = 64;
        session.Refresh();
        Assert.Same(popup, tooltip.Popup);
        Assert.Equal(shown, f.TextureCount);

        inventory[0].Itemstack = null;
        session.Refresh();
        Assert.NotSame(popup, tooltip.Popup);
        var emptyCount = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components.OfType<RichTextComponent>(),
            component => component.DisplayText == "0/4");
        Assert.Equal(GuiStyle.ErrorTextColor, emptyCount.Font.Color);
        Assert.Equal(shown, f.TextureCount);
        tooltip.Dispose();
        Assert.Equal(baseline, f.TextureCount);
    }

    [Fact]
    public void UnresolvedRecipeTooltipShowsItsWarningWithoutConstructingAnInvalidNativeGrid()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var unresolved = row.Recipe.Recipe.Clone();
        unresolved.ResolvedIngredients = null;
        var entry = new CraftingRecipeEntry(1000, unresolved, row.Recipe.DisplayName);
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(row with
        {
            Recipe = entry,
            Evaluation = CraftingRecipeEvaluation.Unsupported(entry.UnresolvedReason!)
        });
        var components = tooltip.Popup!.GetRichtext("recipe").Components;
        Assert.Empty(components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.Contains(entry.UnresolvedReason!, string.Join("\n",
            components.OfType<RichTextComponent>().Select(component => component.DisplayText)));
    }

    [Theory]
    [InlineData(1, 220)]
    [InlineData(1.5, 330)]
    [InlineData(2, 440)]
    public void RecipeTooltipCentersWrappedTitlesAndVisibleGridCellsInNarrowWindows(float scale, double windowWidth)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var entry = new CraftingRecipeEntry(1000, row.Recipe.Recipe,
            "A long localized crafting output name that must wrap across several centered title lines");
        f.Lifecycle.Api.Object.Gui.WindowBounds.fixedWidth = windowWidth / scale;
        f.Lifecycle.Api.Object.Gui.WindowBounds.CalcWorldBounds();
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(row with { Recipe = entry });
        var popup = tooltip.Popup!;
        var content = popup.GetRichtext("recipe");
        var titleElement = popup.GetRichtext("title");
        var title = Assert.IsType<RichTextComponent>(Assert.Single(titleElement.Components));
        Assert.True(title.Lines.Length > 1);
        Assert.Equal(EnumTextOrientation.Center, title.Font.Orientation);
        Assert.All(title.Lines, line => Assert.Equal(content.Bounds.InnerWidth / 2,
            line.Bounds.X + (line.LeftSpace + line.RightSpace + line.Bounds.Width) / 2, precision: 6));
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.Equal(content.Bounds.InnerWidth / 2, grid.BoundsPerLine[0].X + 126 * scale / 2, precision: 6);
        Assert.True(titleElement.Bounds.absY + title.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height) <=
            content.Bounds.absY + grid.BoundsPerLine[0].Y);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(1.5, 300)]
    [InlineData(2, 300)]
    [InlineData(1, 204)]
    [InlineData(1.5, 204)]
    [InlineData(2, 204)]
    public void RecipeTooltipShowsNativeStatsAndFormattedBehaviorDetailsAboveDividerAndGrid(float scale, double width)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        var output = recipe.Output!.ResolvedItemStack!;
        output.Collectible.Durability = 50;
        output.Attributes.SetInt("durability", 35);
        ItemSlot? describedSlot = null;
        var behavior = new Mock<CollectibleBehavior>(output.Collectible);
        behavior.Setup(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
                f.Lifecycle.World.Object, false))
            .Callback<ItemSlot, StringBuilder, IWorldAccessor, bool>((slot, text, _, _) =>
            {
                describedSlot = slot;
                text.AppendLine("<font color=\"#ff0000\">Custom stat: 12</font>");
                text.AppendLine("These native item details wrap across multiple lines inside the padded description panel.");
                Assert.IsType<ItemStack>(slot.Itemstack).Attributes.SetBool("tooltip-mutated", true);
            });
        output.Collectible.CollectibleBehaviors = [behavior.Object];
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        int texturesBefore = f.TextureCount;
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = width + 16;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        tooltip.Inspect(row);
        var popup = tooltip.Popup!;
        var content = popup.GetRichtext("recipe");
        var titleElement = popup.GetRichtext("title");
        var title = Assert.IsType<RichTextComponent>(Assert.Single(titleElement.Components));
        var description = popup.GetRichtext("description");
        var panel = description.Bounds.ParentBounds;
        Assert.NotSame(popup.Bounds, panel);
        Assert.Same(popup.Bounds, panel.ParentBounds);
        Assert.Equal(3 * scale, description.Bounds.absX - panel.absX, precision: 6);
        Assert.Equal(3 * scale, description.Bounds.absY - panel.absY, precision: 6);
        Assert.Equal(3 * scale, panel.absX + panel.OuterWidth -
            description.Bounds.absX - description.Bounds.OuterWidth, precision: 6);
        Assert.Equal(3 * scale, panel.absY + panel.OuterHeight -
            description.Bounds.absY - description.Bounds.OuterHeight, precision: 6);
        Assert.Equal(content.Bounds.InnerWidth, panel.OuterWidth, precision: 6);
        var text = description.Components.OfType<RichTextComponent>()
            .Where(component => component is not ClearFloatTextComponent).ToArray();
        Assert.Contains(text, component => component.Lines.Length > 1);
        Assert.Contains(text, component => component.DisplayText.Contains("Durability:"));
        var custom = Assert.Single(text, component => component.DisplayText.Contains("Custom stat: 12"));
        Assert.Equal(new double[] { 1, 0, 0 }, custom.Font.Color.Take(3));
        Assert.DoesNotContain(text, component => component.DisplayText.Contains("<font"));
        Assert.All(text, component => Assert.Equal(EnumTextOrientation.Left, component.Font.Orientation));
        var divider = Assert.IsType<GuiElementInset>(popup.GetElement("divider"));
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.True(titleElement.Bounds.absY + title.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height) <= panel.absY);
        Assert.True(description.Bounds.absY + text.SelectMany(component => component.BoundsPerLine)
            .Max(bounds => bounds.Y + bounds.Height) < divider.Bounds.absY);
        Assert.True(divider.Bounds.absY + divider.Bounds.OuterHeight <
            content.Bounds.absY + grid.BoundsPerLine[0].Y);
        Assert.Equal(content.Bounds.absX, divider.Bounds.absX, precision: 6);
        Assert.Equal(content.Bounds.InnerWidth, divider.Bounds.OuterWidth, precision: 6);
        Assert.NotNull(describedSlot);
        var describedStack = Assert.IsType<ItemStack>(describedSlot.Itemstack);
        Assert.NotSame(output, describedStack);
        Assert.IsType<DummyInventory>(describedSlot.Inventory);
        Assert.Equal(0, describedSlot.Inventory.GetTransitionSpeedMul(EnumTransitionType.Perish, describedStack));
        Assert.Equal(35, describedStack.Collectible.GetRemainingDurability(describedStack));
        Assert.False(output.Attributes.HasAttribute("tooltip-mutated"));
        Assert.False(row.Recipe.GetPreview()!.Attributes.HasAttribute("tooltip-mutated"));
        tooltip.Inspect(row);
        behavior.Verify(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
            f.Lifecycle.World.Object, false), Times.Once);
        tooltip.Dispose();
        Assert.Equal(texturesBefore, f.TextureCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecipeTooltipOmitsEmptyDescriptionAndDividerWithoutLosingPreview(bool hasOutput)
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var recipe = row.Recipe.Recipe.Clone();
        var item = new Mock<MockItem>(555, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        item.Object.Code = new AssetLocation("game:empty-description");
        item.Setup(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
                It.IsAny<IWorldAccessor>(), false))
            .Callback<ItemSlot, StringBuilder, IWorldAccessor, bool>((_, text, _, _) => text.Append(" \r\n"));
        recipe.Output!.ResolvedItemStack = hasOutput ? new ItemStack(item.Object) : null;
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(row with { Recipe = new CraftingRecipeEntry(1000, recipe, row.Recipe.DisplayName) });
        var popup = Assert.IsType<GuiComposer>(tooltip.Popup);
        var content = popup.GetRichtext("recipe");
        Assert.Single(popup.GetRichtext("title").Components);
        Assert.Null(popup.GetElement("description"));
        Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.Null(popup.GetElement("divider"));
        item.Verify(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
            It.IsAny<IWorldAccessor>(), false), hasOutput ? Times.Once() : Times.Never());
    }

    [Fact]
    public void NativeTooltipGridResolvesAllowedWildcardIngredientChoices()
    {
        using var f = new MenuFixture(includeRecipes: true);
        var ingredient = f.Lifecycle.F.Wildcard("board-*", 2);
        ingredient.AllowedVariants = ["oak", "pine"];
        ingredient.SkipVariants = ["rotten"];
        var recipe = f.Lifecycle.F.Recipe(ingredient);
        f.Lifecycle.Recipes.Clear();
        f.Lifecycle.Recipes.Add(recipe);
        f.Lifecycle.F.Register(recipe);
        var stacks = new[] { "oak", "pine", "rotten", "birch" }
            .Select((wood, index) => f.Lifecycle.F.Stack(20 + index, "board-" + wood, 1)).ToArray();
        f.Lifecycle.World.SetupGet(value => value.Collectibles).Returns(stacks.Select(stack => stack.Collectible).ToList());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tooltip = Assert.IsType<GuiElementCraftingRecipeTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(Assert.Single(session.Rows));
        var grid = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components
            .OfType<SlideshowGridRecipeTextComponent>());
        var choices = grid.GridRecipesAndUnnamedIngredients.SelectMany(entry =>
            entry.UnnamedIngredients != null ? entry.UnnamedIngredients.Values.SelectMany(value => value)
                : entry.Recipe!.ResolvedIngredients!.Where(value => value != null)
                    .Select(value => value!.ResolvedItemStack!)).Select(stack => stack.Collectible.Code.Path).ToHashSet();
        Assert.Equal(new[] { "board-oak", "board-pine" }, choices.Order().ToArray());
        Assert.Equal(EnumRecipeMatchType.Wildcard, ingredient.MatchingType);
        Assert.Equal("board-*", ingredient.Code!.Path);
    }

    [Fact]
    public void AlternativeCounterHasNoMarkerWhenRecipeIsManuallySelected()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
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

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void BookButtonUsesSvgAndNativeMouseAndKeyboardActivation(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        var button = f.ToggleButton;
        Assert.Empty(button.GetText());
        Assert.Equal("vanillaexpanded-crafting-book", button.icon);
        Assert.False(button.Toggleable);
        Assert.Equal(28 * scale, button.Bounds.InnerWidth, 4);
        Assert.Equal(button.Bounds.InnerWidth, button.Bounds.InnerHeight, 4);
        AssertBookTabPosition(f);
        Assert.Equal("Recipes", button.Hint.GetText());
        var svg = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(f.BookAsset.Data));
        Assert.Equal("http://www.w3.org/2000/svg", svg.Root!.Name.NamespaceName);
        Assert.Equal("0 0 24 24", svg.Root.Attribute("viewBox")!.Value);
        Assert.Equal(3, f.BookDraws.Count);
        Assert.All(f.BookDraws, draw =>
        {
            Assert.Same(f.BookAsset, draw.Asset);
            Assert.InRange(draw.Width, 18 * scale, 21 * scale);
            Assert.InRange(draw.Height, 18 * scale, 21 * scale);
            Assert.NotNull(draw.Color);
        });
        f.Parent.TryOpen();
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.AssertOpenerInvisible();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.ClickCloseButton(Assert.Single(f.Menus));
        Assert.Empty(f.Menus);
        Assert.True(button.Visible);
        button.OnFocusGained();
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        button.OnKeyDown(f.Lifecycle.Api.Object, enter);
        Assert.True(enter.Handled);
        Assert.True(Assert.Single(f.Menus).IsOpened());
        Assert.False(button.HasFocus);
        f.AssertOpenerInvisible();
        f.ClickCloseButton(Assert.Single(f.Menus));
        Assert.Empty(f.Menus);
        Assert.True(button.Visible);
        Assert.False(button.On);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void BookTabStaysAttachedToLeftEdgeNearTopWhenParentMovesResizesAndRecomposes(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        AssertBookTabPosition(f);
        var composer = f.Parent.Composers["maininventory"];
        double before = f.ToggleButton.Bounds.absY;
        composer.Bounds.fixedHeight += 120;
        composer.Bounds.MarkDirtyRecursive();
        composer.ReCompose();
        Assert.Equal(before, f.ToggleButton.Bounds.absY, 4);
        AssertBookTabPosition(f);
        composer.Bounds.WithFixedPadding(20, 30);
        composer.Bounds.MarkDirtyRecursive();
        composer.ReCompose();
        AssertBookTabPosition(f);
        composer.Bounds.fixedX += 60;
        composer.Bounds.fixedY += 40;
        composer.Bounds.MarkDirtyRecursive();
        composer.Bounds.CalcWorldBounds();
        AssertBookTabPosition(f);
        f.Recompose();
        AssertBookTabPosition(f);
        f.Parent.TryOpen();
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
    }

    private static void AssertBookTabPosition(MenuFixture f)
    {
        var composer = f.Parent.Composers["maininventory"];
        var button = f.ToggleButton.Bounds;
        var hint = f.ToggleButton.Hint.Bounds;
        Assert.Equal(composer.Bounds.absX - GuiElement.scaled(1), button.absX + button.OuterWidth, 4);
        Assert.Equal(composer.Bounds.absY + GuiElement.scaled(8), button.absY, 4);
        Assert.True(button.absX < composer.Bounds.absX);
        Assert.Equal(button.absX, hint.absX, 4);
        Assert.Equal(button.absY, hint.absY, 4);
        Assert.Equal(button.OuterWidth, hint.OuterWidth, 4);
        Assert.Equal(button.OuterHeight, hint.OuterHeight, 4);
    }

    [Fact]
    public void FailedMenuOpeningLeavesBookTabVisibleAndUsable()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.ResizePanelSpace(320, 120);
        f.Toggle();
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        Assert.True(f.ToggleButton.Focusable);
        f.ResizePanelSpace(320, 420);
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.AssertOpenerInvisible();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void OpeningReleaseOverNewCloseButtonDoesNotImmediatelyCloseMenu(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        f.ResizePanelSpace(150, 1080);
        int x = (int)(f.ToggleButton.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(f.ToggleButton.Bounds.absY + GuiElement.scaled(5));
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        f.Parent.OnMouseDown(down);
        Assert.True(down.Handled);
        var menu = Assert.Single(f.Menus);
        var titlebar = menu.SingleComposer.GetTitleBar("titlebar");
        Assert.InRange(x - titlebar.Bounds.absX,
            titlebar.Bounds.OuterWidth - GuiElement.scaled(GuiElementDialogTitleBar.unscaledCloseIconSize + 12),
            titlebar.Bounds.OuterWidth - GuiElement.scaled(10));
        Assert.InRange(y - titlebar.Bounds.absY, GuiElement.scaled(5), GuiElement.scaled(22));
        var up = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        menu.OnMouseUp(up);
        f.Parent.OnMouseUp(up);
        Assert.Same(menu, Assert.Single(f.Menus));
        Assert.True(menu.IsOpened());
        f.AssertOpenerInvisible();
        menu.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
    }

    [Fact]
    public void SessionFilterRegistrationsSurviveRecompositionAndAreReleasedWithTheDialog()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var manager = session.Filters;
        using var registration = manager.Register("test:lifetime", new LifecycleFilterProvider());
        var previousComposer = menu.SingleComposer;
        f.ResizePanelSpace(previousComposer.Bounds.fixedWidth, previousComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(previousComposer, menu.SingleComposer);
        Assert.Same(manager, session.Filters);
        Assert.Single(manager.Filter(session.Rows));
        Assert.Throws<ArgumentException>(() => manager.Register("test:lifetime", new LifecycleFilterProvider()));

        f.Parent.TryClose();
        Assert.Empty(f.Menus);
        Assert.Throws<ObjectDisposedException>(() => manager.Filter(session.Rows));
        registration.Dispose();
        f.Parent.TryOpen();
        var reopened = Assert.Single(f.Menus);
        var newSession = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
        Assert.NotSame(manager, newSession.Filters);
        using var newRegistration = newSession.Filters.Register("test:lifetime", new LifecycleFilterProvider());
        Assert.Single(newSession.Filters.Filter(newSession.Rows));
    }

    private sealed class LifecycleFilterProvider : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => true;
    }

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
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        Assert.Equal(new[] { "All", "mod:" + excluded, "tools" }, tabs.tabs.Select(tab => tab.Name));
        Assert.Equal(3, session.Rows.Length);
        Assert.Contains(session.Rows, row => row.Recipe.Recipe == excludedOnly);
        tabs.SetValue(2);
        Assert.Same(overlapping, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(1);
        Assert.Same(custom, Assert.Single(session.Rows).Recipe.Recipe);
        tabs.SetValue(0);
        menu.SingleComposer.GetTextInput("search").SetValue(excludedOnly.Output.Code!.ToString());
        Assert.Same(excludedOnly, Assert.Single(session.Rows).Recipe.Recipe);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NativeCategoryTabsFilterSearchResetScrollingAndSurviveResize(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var tool = Assert.Single(f.Lifecycle.Recipes);
        tool.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs =
            ["general", "items", "tools", .. Enumerable.Range(0, 8).Select(index => "mod:long-category-" + index)];
        for (int i = 0; i < 40; i++)
        {
            var recipe = f.Lifecycle.F.Catalogue.Recipe("construction-" + i);
            recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["general", "construction"];
            f.Lifecycle.Recipes.Add(recipe);
        }
        f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("uncategorized"));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        Assert.Equal("All", tabs.tabs[0].Name);
        Assert.Equal("Other", tabs.tabs[^1].Name);
        Assert.DoesNotContain(tabs.tabs, tab => tab.Name is "general" or "flora");
        Assert.Equal(42, session.Rows.Length);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        var tiles = Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(48 * scale);
        Assert.True(menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);
        var pressed = Assert.IsType<GuiElementCraftingTile>(tiles.Elements[0]);
        int pressX = (int)pressed.Bounds.absX + 10;
        int pressY = (int)pressed.Bounds.absY + 10;
        tiles.OnMouseDown(f.Lifecycle.Api.Object, new MouseEvent(pressX, pressY, EnumMouseButton.Left, 0));
        Assert.True(pressed.Pressed);
        menu.SingleComposer.GetTextInput("search").SetValue(" output ");
        Assert.False(pressed.Pressed);
        Assert.Equal(42, session.Rows.Length);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        tiles.Scroll(48 * scale);
        Assert.True(menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);

        int toolIndex = Array.FindIndex(tabs.tabs, tab => tab.Name == "tools");
        tabs.SetValue(toolIndex);
        Assert.Equal("tools", session.Category);
        Assert.Single(session.Rows);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        Assert.NotNull(Assert.IsType<GuiElementCraftingTile>(tiles.Elements[0]).Selection);
        Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());

        var previousComposer = menu.SingleComposer;
        f.ResizePanelSpace(previousComposer.Bounds.fixedWidth + 50, previousComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(previousComposer, menu.SingleComposer);
        tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        Assert.Equal(toolIndex, tabs.activeElement);
        Assert.Equal("tools", session.Category);
        Assert.Single(session.Rows);
        Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());
        tabs.SetValue(tabs.tabs.Length - 1);
        Assert.Equal(CraftingCatalogue.OtherCategory, session.Category);
        Assert.Single(session.Rows);
        menu.SingleComposer.GetTextInput("search").SetValue("no matching output");
        Assert.Empty(session.Rows);
        Assert.Equal("No recipes match your search in this category.", menu.SingleComposer.GetDynamicText("empty").GetText());
        Assert.Equal(toolIndex, Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));

        tabs.SetValue(0);
        tabs.OnFocusGained();
        var right = new KeyEvent { KeyCode = (int)GlKeys.Right };
        tabs.OnKeyDown(f.Lifecycle.Api.Object, right);
        Assert.True(right.Handled);
        Assert.Equal("construction", session.Category);
        Assert.Empty(session.Rows);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        Assert.Equal(40, session.Rows.Length);
        tabs.SetValue(0, false);
        int x = (int)(tabs.Bounds.absX + GuiElement.scaled(12));
        int y = (int)(tabs.Bounds.absY + tabs.Bounds.InnerHeight / 2);
        f.SetMouse(x, y);
        tabs.OnMouseDownOnElement(f.Lifecycle.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.Null(session.Category);
        Assert.Equal(42, session.Rows.Length);
        Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void ComposedControlsStaySeparatedAtMinimumSizeAndAfterResize(float scale, bool longLabels)
    {
        using var f = new MenuFixture(scale: scale, longLabels: longLabels);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        AssertLayout(menu);
        var originalComposer = menu.SingleComposer;
        var title = originalComposer.GetTitleBar("titlebar");
        var search = originalComposer.GetTextInput("search");
        double counterWidth = Math.Ceiling(CairoFont.WhiteSmallText().GetTextExtents("1/1").Width / scale + 1);
        double footerWidth = new[] { "previous", "auto", "next" }
            .Sum(key => originalComposer.GetElement(key).Bounds.OuterWidth / scale) + 6 + 4 + counterWidth
            + 4 + originalComposer.GetElement("handbook").Bounds.OuterWidth / scale + 16;
        double titleWidth = GuiStyle.ElementToDialogPadding + title.Font.GetTextExtents(title.GetText()).Width / scale
            + 8 + 2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20;
        var craftable = originalComposer.GetToggleButton("craftable-only");
        double craftableWidth = 32 + craftable.Font.GetTextExtents(craftable.GetText()).Width / scale;
        double actionWidth = 32 + new[] { "clear-search", "show-uncraftable" }
            .Max(key => originalComposer.GetButton(key).Bounds.OuterWidth / scale);
        double minimumWidth = Math.Ceiling(Math.Max(actionWidth, Math.Max(craftableWidth, Math.Max(footerWidth, titleWidth))));
        double textHeight = Math.Ceiling(new[] { "empty", "empty-search", "empty-craftable", "empty-craftable-search" }
            .Max(key => new TextDrawUtil().GetMultilineTextHeight(CairoFont.WhiteSmallText(),
                Lang.Get("vanillaexpanded:craftingmenu-" + key), (minimumWidth - 32) * scale)) / scale);
        double emptyHeight = textHeight + 8
            + originalComposer.GetElement("auto").Bounds.OuterHeight / scale;
        double minimumHeight = Math.Ceiling(GuiStyle.TitleBarHeight + 8 + search.Bounds.OuterHeight / scale
            + 8 + originalComposer.GetHorizontalTabs("categories").Bounds.OuterHeight / scale
            + 8 + craftable.Bounds.OuterHeight / scale
            + 8 + Math.Max(GuiElementCraftingTiles.TileSize, emptyHeight) + 10
            + originalComposer.GetElement("auto").Bounds.OuterHeight / scale + 8);
        search.SetValue("preserved search");
        f.ResizePanelSpace(minimumWidth, minimumHeight);
        menu.TryOpen();
        Assert.NotSame(originalComposer, menu.SingleComposer);
        Assert.Equal(minimumWidth, menu.SingleComposer.Bounds.fixedWidth, 4);
        Assert.Equal(minimumHeight, menu.SingleComposer.Bounds.fixedHeight, 4);
        Assert.Equal("preserved search", menu.SingleComposer.GetTextInput("search").GetText());
        AssertLayout(menu);
        foreach (string key in new[] { "previous", "auto", "next", "handbook" })
        {
            Assert.False(menu.SingleComposer.GetButton(key).Enabled);
            f.Click(menu.SingleComposer.GetElement(key), handled: false);
        }
        f.ResizePanelSpace(Math.Max(320, minimumWidth), 420);
        menu.TryOpen();
        Assert.Equal("preserved search", menu.SingleComposer.GetTextInput("search").GetText());
        AssertLayout(menu);

        static void AssertLayout(GuiDialogCraftingMenu menu)
        {
            AssertEmptyStateCentered(menu);
            var composer = menu.SingleComposer;
            var panel = composer.Bounds;
            var title = composer.GetTitleBar("titlebar");
            var search = composer.GetTextInput("search").Bounds;
            var tabs = composer.GetHorizontalTabs("categories").Bounds;
            var craftable = composer.GetToggleButton("craftable-only");
            var viewport = composer.GetElement("tiles").InsideClipBounds;
            var scroll = composer.GetScrollbar("scroll").Bounds;
            var empty = composer.GetDynamicText("empty").Bounds;
            var previous = composer.GetElement("previous").Bounds;
            var auto = composer.GetElement("auto").Bounds;
            var next = composer.GetElement("next").Bounds;
            var counter = composer.GetDynamicText("alternative").Bounds;
            var handbook = composer.GetButton("handbook");
            double scale = GuiElement.scaled(1);
            Assert.True(title.Bounds.absY + title.Bounds.OuterHeight + 8 * scale <= tabs.absY + .001);
            Assert.True(tabs.absY + tabs.OuterHeight + 8 * scale <= search.absY + .001);
            Assert.True(search.absY + search.OuterHeight + 8 * scale <= craftable.Bounds.absY + .001);
            Assert.True(craftable.Bounds.absY + craftable.Bounds.OuterHeight + 8 * scale <= viewport.absY + .001);
            Assert.True(craftable.Font.GetTextExtents(craftable.GetText()).Width + 16 * scale <=
                craftable.Bounds.InnerWidth + .001);
            Assert.True(viewport.InnerHeight + .001 >= GuiElementCraftingTiles.TileSize * scale);
            Assert.True(viewport.absX + viewport.OuterWidth + 4 * scale <= scroll.absX + .001);
            Assert.Equal(viewport.absY, scroll.absY, 4);
            Assert.Equal(viewport.OuterHeight, scroll.OuterHeight, 4);
            Assert.True(viewport.absY + viewport.OuterHeight + 10 * scale <= previous.absY + .001);
            Assert.True(previous.absX + previous.OuterWidth + 3 * scale <= auto.absX + .001);
            Assert.True(auto.absX + auto.OuterWidth + 3 * scale <= next.absX + .001);
            Assert.True(next.absX + next.OuterWidth + 4 * scale <= counter.absX + .001);
            Assert.True(counter.absX + counter.OuterWidth + 4 * scale <= handbook.Bounds.absX + .001);
            Assert.True(CairoFont.SmallButtonText().GetTextExtents(Lang.Get("vanillaexpanded:craftingmenu-handbook")).Width
                <= handbook.Bounds.InnerWidth + .001);
            double titleTextRight = title.Bounds.absX + GuiElement.scaled(GuiStyle.ElementToDialogPadding)
                + title.Font.GetTextExtents(title.GetText()).Width;
            double titleMenuLeft = title.Bounds.absX + title.Bounds.OuterWidth
                - GuiElement.scaled(2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20);
            Assert.True(titleTextRight + 8 * scale <= titleMenuLeft + .001);
            foreach (var bounds in new[] { tabs, search, craftable.Bounds, viewport, scroll, previous, auto, next, counter,
                handbook.Bounds })
            {
                Assert.True(bounds.absX >= panel.absX + 8 * scale - .001);
                Assert.True(bounds.absX + bounds.OuterWidth <= panel.absX + panel.InnerWidth - 8 * scale + .001);
                Assert.True(bounds.absY + bounds.OuterHeight <= panel.absY + panel.InnerHeight - 8 * scale + .001);
            }
            Assert.True(empty.absX >= viewport.absX);
            Assert.True(empty.absY >= viewport.absY);
            Assert.True(empty.absX + empty.OuterWidth <= viewport.absX + viewport.InnerWidth + .001);
            Assert.True(empty.absY + empty.OuterHeight <= viewport.absY + viewport.InnerHeight + .001);
            foreach (string key in new[] { "clear-search", "show-uncraftable" })
            {
                var button = composer.GetButton(key);
                if (button.Visible)
                    Assert.True(empty.absY + empty.OuterHeight + 8 * scale <= button.Bounds.absY + .001);
                Assert.True(button.Bounds.absX >= viewport.absX);
                Assert.True(button.Bounds.absX + button.Bounds.OuterWidth <= viewport.absX + viewport.InnerWidth + .001);
                Assert.True(button.Bounds.absY + button.Bounds.OuterHeight <= viewport.absY + viewport.InnerHeight + .001);
                Assert.True(CairoFont.SmallButtonText().GetTextExtents(button.Text).Width <= button.Bounds.InnerWidth + .001);
            }
            var textUtil = new TextDrawUtil();
            foreach (string key in new[] { "empty", "empty-search", "empty-craftable", "empty-craftable-search" })
                Assert.True(textUtil.GetMultilineTextHeight(CairoFont.WhiteSmallText(),
                    Lang.Get("vanillaexpanded:craftingmenu-" + key), empty.InnerWidth)
                    + 8 * scale + auto.OuterHeight <= viewport.InnerHeight + .001);
        }
    }

    [Theory]
    [InlineData(true, EnumAppSide.Client, true)]
    [InlineData(false, EnumAppSide.Client, false)]
    [InlineData(true, EnumAppSide.Server, false)]
    [InlineData(false, EnumAppSide.Server, false)]
    public void ShouldLoadReadsPersistedEnablementBeforeStartup(bool enabled, EnumAppSide side, bool expected)
    {
        var original = VanillaExpandedModSystem.Config;
        var loadedField = typeof(VanillaExpandedModSystem).GetField("configLoaded",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        bool loaded = (bool)loadedField.GetValue(null)!;
        var api = new Mock<ICoreAPI>();
        api.SetupGet(value => value.Side).Returns(side);
        api.Setup(value => value.LoadModConfig<VanillaExpandedConfig>(Constants.ConfigFileName))
            .Returns(new VanillaExpandedConfig { EnableCraftingMenu = enabled });
        try
        {
            loadedField.SetValue(null, false);
            var system = new CraftingMenuSystem();
            Assert.Equal(expected, system.ShouldLoad(api.Object));
            Assert.Equal(expected, system.ShouldLoad(side));
            api.Verify(value => value.LoadModConfig<VanillaExpandedConfig>(Constants.ConfigFileName), Times.Once);
        }
        finally
        {
            typeof(VanillaExpandedModSystem).GetProperty(nameof(VanillaExpandedModSystem.Config))!
                .SetValue(null, original);
            loadedField.SetValue(null, loaded);
        }
    }

    [Fact]
    public void DisabledAtWorldLoadDoesNotLoadSystemAndEnablingWaitsForReload()
    {
        using var f = new MenuFixture(false);
        Assert.False(f.Lifecycle.Loaded);
        Assert.False(f.Lifecycle.System.ShouldLoad(EnumAppSide.Server));
        Assert.Null(f.Lifecycle.System.Catalogue);
        Assert.Null(f.Lifecycle.System.IconCache);
        Assert.Equal(0, f.Lifecycle.Builds);
        Assert.Null(f.Parent.Composers["maininventory"].GetElement("vanillaexpanded-crafting-toggle"));
        f.Lifecycle.Events.VerifyAdd(value => value.LevelFinalize += It.IsAny<Action>(), Times.Never);
        f.Lifecycle.Events.VerifyAdd(value => value.LeaveWorld += It.IsAny<Action>(), Times.Never);
        f.Parent.TryOpen();
        f.SetEnabled(true);
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        Assert.Equal(0, f.Lifecycle.Builds);
        Assert.Null(f.Lifecycle.System.Catalogue);
        Assert.Null(f.Parent.Composers["maininventory"].GetElement("vanillaexpanded-crafting-toggle"));
        Assert.Empty(f.Menus);
        using var reloaded = new LifecycleFixture();
        Assert.True(reloaded.Loaded);
        Assert.False(reloaded.System.ShouldLoad(EnumAppSide.Server));
        reloaded.FinalizeWorld();
        Assert.Equal(1, reloaded.Builds);
        Assert.NotNull(reloaded.System.Catalogue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingLiveDisposesSystemAndBlocksReactivationUntilReload(bool queuedRestore)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        if (queuedRestore) f.Recompose();
        f.SetEnabled(false);
        f.Lifecycle.Drain();
        Assert.False(menu.IsOpened());
        Assert.Empty(f.Menus);
        Assert.Null(f.Lifecycle.System.Catalogue);
        Assert.Null(f.Lifecycle.System.IconCache);
        f.AssertToggleHidden();
        f.SetEnabled(true);
        f.Lifecycle.FinalizeWorld();
        f.Parent.TryClose();
        f.Parent.TryOpen();
        f.AssertToggleHidden();
        Assert.Equal(1, f.Lifecycle.Builds);
        Assert.Empty(f.Menus);
        Assert.Null(f.Lifecycle.System.Catalogue);
        f.Recompose();
        Assert.Null(f.Parent.Composers["maininventory"].GetElement("vanillaexpanded-crafting-toggle"));
        using var reloaded = new LifecycleFixture();
        reloaded.FinalizeWorld();
        Assert.Equal(1, reloaded.Builds);
        Assert.NotNull(reloaded.System.Catalogue);
    }

    [Fact]
    public void UnrelatedConfigReloadKeepsEnabledSystemAndExistingMenu()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var catalogue = f.Lifecycle.System.Catalogue;
        var cache = f.Lifecycle.System.IconCache;
        f.SetEnabled(true);
        Assert.Same(menu, Assert.Single(f.Menus));
        Assert.Same(catalogue, f.Lifecycle.System.Catalogue);
        Assert.Same(cache, f.Lifecycle.System.IconCache);
        Assert.Equal(1, f.Lifecycle.Builds);
    }

    [Fact]
    public void ConfigReloadBeforeWorldFinalizeDoesNotReadRecipesEarly()
    {
        using var scope = new NativeScope();
        using var f = new LifecycleFixture();
        f.System.OnConfigReloaded(f.Api.Object);
        Assert.Equal(0, f.Builds);
        f.FinalizeWorld();
        Assert.Equal(1, f.Builds);
        f.System.Dispose();
        f.System.OnConfigReloaded(f.Api.Object);
        Assert.Null(f.System.Catalogue);
        Assert.Equal(1, f.Builds);
    }

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
            f.AssertOpenerInvisible();
            f.Lifecycle.Drain();
            Assert.Same(restored, Assert.Single(f.Menus));
            first = restored;
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
        Assert.Equal(0, menu.SingleComposer.GetHorizontalTabs("categories").activeElement);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        menu.SingleComposer.GetTextInput("search").SetValue(" output ");
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        int category = Array.FindIndex(tabs.tabs, tab => tab.Name == "tools");
        tabs.SetValue(category);
        var tiles = Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(73 * scale);
        Assert.Equal(73, tiles.ScrollPosition);
        int? firstOutput = Assert.IsType<GuiElementCraftingTile>(tiles.Elements[0]).Selection?.Output.Id;
        Assert.NotNull(firstOutput);

        var composer = menu.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 10);
        menu.TryOpen();
        Assert.NotSame(composer, menu.SingleComposer);
        AssertBrowsingState(menu);
        for (int i = 0; i < 3; i++)
        {
            f.Parent.TryClose();
            Assert.Empty(f.Menus);
            f.Parent.TryOpen();
            var reopened = Assert.Single(f.Menus);
            Assert.NotSame(menu, reopened);
            AssertBrowsingState(reopened);
            f.Lifecycle.Drain();
            Assert.Same(reopened, Assert.Single(f.Menus));
            menu = reopened;
        }

        f.ClickCloseButton(menu);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        f.Lifecycle.Drain();
        Assert.Empty(f.Menus);
        f.Toggle();
        menu = Assert.Single(f.Menus);
        AssertBrowsingState(menu);
        menu.SingleComposer.GetTextInput("search").SetValue("no matching recipe");
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        Assert.Equal("no matching recipe", menu.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(category, menu.SingleComposer.GetHorizontalTabs("categories").activeElement);
        Assert.Equal("No recipes match your search in this category.", menu.SingleComposer.GetDynamicText("empty").GetText());
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);

        void AssertBrowsingState(GuiDialogCraftingMenu current)
        {
            Assert.Equal(" output ", current.SingleComposer.GetTextInput("search").GetText());
            Assert.Equal(category, current.SingleComposer.GetHorizontalTabs("categories").activeElement);
            var currentTiles = Assert.IsType<GuiElementCraftingTiles>(current.SingleComposer.GetElement("tiles"));
            Assert.Equal(73, currentTiles.ScrollPosition);
            Assert.Equal(73 * scale, current.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
            Assert.Equal(firstOutput, Assert.IsType<GuiElementCraftingTile>(currentTiles.Elements[0]).Selection?.Output.Id);
        }
    }

    [Fact]
    public void RestoredScrollClampsToEvaluatedResultsAndWorldExitClearsBrowsingState()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        using (var menu = new GuiDialogCraftingMenu(f.Lifecycle.Api.Object, f.Parent,
            f.Parent.Composers["maininventory"], f.Lifecycle.System.Catalogue!, f.Lifecycle.F.Allocator,
            f.Lifecycle.System.IconCache!, new(null, "output", 500)))
        {
            Assert.True(menu.TryOpen());
            Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());
            Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
            Assert.NotNull(Assert.IsType<GuiElementCraftingTile>(
                Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles")).Elements[0]).Selection);
            menu.TryClose();
        }
        f.Toggle();
        var current = Assert.Single(f.Menus);
        current.SingleComposer.GetTextInput("search").SetValue("not found");
        f.Lifecycle.Events.Raise(value => value.LeaveWorld += null);
        Assert.Empty(f.Menus);
        f.Parent.TryClose();
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        if (!f.Menus.Any()) f.Toggle();
        current = Assert.Single(f.Menus);
        Assert.Equal("", current.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(0, current.SingleComposer.GetHorizontalTabs("categories").activeElement);
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
        var tabs = menu.SingleComposer.GetHorizontalTabs("categories");
        int index = category == null ? 0 : tabs.tabs.Length - 1;
        tabs.SetValue(index);
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
        Assert.Equal(index, menu.SingleComposer.GetHorizontalTabs("categories").activeElement);
        menu.SingleComposer.GetTextInput("search").SetValue("");
        f.Parent.TryClose();
        var grid = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        grid[0].Itemstack = Assert.Single(f.Lifecycle.Recipes).ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        Assert.Equal("", menu.SingleComposer.GetTextInput("search").GetText());
        Assert.Equal(index, menu.SingleComposer.GetHorizontalTabs("categories").activeElement);
        var tiles = Assert.IsType<GuiElementCraftingTiles>(menu.SingleComposer.GetElement("tiles"));
        Assert.True(Assert.IsType<GuiElementCraftingTile>(tiles.Elements[0]).Selection!.Evaluation.CanCraft);
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
        f.AssertOpenerInvisible();
    }

    [Theory]
    [InlineData(GlKeys.E)]
    [InlineData(GlKeys.I)]
    public void InventoryHotkeyFromFocusedMenuClosesParentAndRestoresMenu(GlKeys key)
    {
        using var f = new MenuFixture();
        f.Parent.Opening = f.Recompose;
        var hotkey = new HotKey { CurrentMapping = new KeyCombination { KeyCode = (int)key } };
        Mock.Get(f.Lifecycle.Api.Object.Input).Setup(value => value.GetHotKeyByCode("inventory")).Returns(hotkey);
        f.Parent.TryOpen();
        f.Toggle();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var menu = Assert.Single(f.Menus);
            menu.Focus();
            menu.SingleComposer.GetTextInput("search").OnFocusLost();
            var args = new KeyEvent { KeyCode = (int)key };
            menu.OnKeyDown(args);
            Assert.True(args.Handled);
            Assert.False(f.Parent.IsOpened());
            Assert.Empty(f.Menus);
            f.Parent.TryOpen();
            var restored = Assert.Single(f.Menus);
            Assert.NotSame(menu, restored);
            Assert.True(restored.IsOpened());
            f.Lifecycle.Drain();
            Assert.Same(restored, Assert.Single(f.Menus));
            f.AssertOpenerInvisible();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapePreservesOpenPreferenceRegardlessOfDialogDispatchOrder(bool parentFirst)
    {
        using var f = new MenuFixture();
        f.Parent.Opening = f.Recompose;
        f.Parent.TryOpen();
        f.Toggle();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var menu = Assert.Single(f.Menus);
            if (parentFirst) Assert.True(f.Parent.OnEscapePressed());
            else Assert.True(menu.OnEscapePressed());
            Assert.False(f.Parent.IsOpened());
            Assert.Empty(f.Menus);
            f.Parent.TryOpen();
            Assert.NotSame(menu, Assert.Single(f.Menus));
            f.Lifecycle.Drain();
            Assert.True(Assert.Single(f.Menus).IsOpened());
            f.AssertOpenerInvisible();
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void SearchIsFirstInNativeTabOrderAfterOpeningResizingAndReopening(float scale, bool includeRecipes)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: includeRecipes);
        Mock.Get(f.Lifecycle.Api.Object.Gui).Setup(value => value.RequestFocus(It.IsAny<GuiDialog>()))
            .Callback<GuiDialog>(dialog => dialog.Focus());
        f.Parent.Opening = f.Recompose;
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        AssertTabOrder(menu);
        var previousComposer = menu.SingleComposer;
        f.ResizePanelSpace(previousComposer.Bounds.fixedWidth + 50, previousComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(previousComposer, menu.SingleComposer);
        AssertTabOrder(menu);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        AssertTabOrder(Assert.Single(f.Menus));

        static void AssertTabOrder(GuiDialogCraftingMenu menu)
        {
            var composer = menu.SingleComposer;
            var search = composer.GetTextInput("search");
            var categories = composer.GetHorizontalTabs("categories");
            Assert.Same(search, composer.FirstTabbableElement);
            Assert.Equal(0, search.TabIndex);
            Assert.True(composer.FocusElement(0));
            search.OnFocusLost();
            Assert.Null(composer.CurrentTabIndexElement);
            menu.Focus();
            PressTab();
            Assert.Same(search, composer.CurrentTabIndexElement);
            PressTab();
            Assert.Same(categories, composer.CurrentTabIndexElement);
            PressTab();
            Assert.Same(composer.GetToggleButton("craftable-only"), composer.CurrentTabIndexElement);
            PressTab(shift: true);
            Assert.Same(categories, composer.CurrentTabIndexElement);
            PressTab(shift: true);
            Assert.Same(search, composer.CurrentTabIndexElement);
            Assert.True(composer.FocusElement(composer.MaxTabIndex));
            PressTab();
            Assert.Same(search, composer.CurrentTabIndexElement);

            void PressTab(bool shift = false)
            {
                var args = new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = shift };
                menu.OnKeyDown(args);
                Assert.True(args.Handled);
            }
        }
    }

    [Fact]
    public void InventoryHotkeyRemainsSearchInputWhileSearchIsFocused()
    {
        using var f = new MenuFixture();
        var hotkey = new HotKey { CurrentMapping = new KeyCombination { KeyCode = (int)GlKeys.E } };
        Mock.Get(f.Lifecycle.Api.Object.Input).Setup(value => value.GetHotKeyByCode("inventory")).Returns(hotkey);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        menu.Focus();
        menu.SingleComposer.GetTextInput("search").OnFocusGained();
        var args = new KeyEvent { KeyCode = (int)GlKeys.E };
        menu.OnKeyDown(args);
        Assert.True(args.Handled);
        Assert.True(f.Parent.IsOpened());
        Assert.Same(menu, Assert.Single(f.Menus));
        Assert.True(menu.CaptureAllInputs());
    }

    [Fact]
    public void ExplicitlyClosingTheMenuKeepsItClosedWhenTheParentReopens()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        f.ClickCloseButton(menu);
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
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
        f.AssertOpenerInvisible();
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.True(Assert.Single(f.Menus).IsOpened());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldCleanupDetachesRestorationAndInvalidatesQueuedRecomposition(bool dispose)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        f.Recompose();
        if (dispose) f.Lifecycle.System.Dispose();
        else f.Lifecycle.Events.Raise(value => value.LeaveWorld += null);
        f.Lifecycle.Drain();
        Assert.Empty(f.Menus);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
    }

    [Fact]
    public void FinalizeCachesImmutableCatalogueWhileEachNewSessionReadsFreshInventory()
    {
        using var scope = new NativeScope();
        using var f = new LifecycleFixture();
        Assert.Null(f.System.Catalogue);
        f.FinalizeWorld();
        var catalogue = f.System.Catalogue!;
        Assert.Equal(1, f.Builds);
        Assert.Single(catalogue.Recipes);
        int captures = 0;
        CraftingMenuSession Open() => new(catalogue, f.F.Allocator, () =>
        {
            captures++;
            return CraftingInventorySnapshot.Capture(f.F.Player, 3, 3, RuntimeEnv.MainThreadId, true);
        }, (_, _) => throw new InvalidOperationException("No placement expected."));
        var first = Open();
        first.Refresh();
        var row = Assert.Single(first.Rows);
        Assert.False(row.Evaluation.CanCraft);
        first.Select(row.Output.Id);
        first.ChooseAlternative(row.Recipe.Id);
        f.F.Hotbar[0].Itemstack = f.F.Stack(1, "a", 1);
        var reopened = Open();
        reopened.Refresh();
        Assert.True(Assert.Single(reopened.Rows).Evaluation.CanCraft);
        Assert.Null(reopened.SelectedOutput);
        Assert.False(Assert.Single(reopened.Rows).IsExplicit);
        Assert.Equal(2, captures);
        Assert.Equal(1, f.Builds);
        Assert.Same(catalogue, f.System.Catalogue);
        f.Events.Raise(events => events.LeaveWorld += null);
        Assert.Null(f.System.Catalogue);
        f.FinalizeWorld();
        Assert.NotSame(catalogue, f.System.Catalogue);
        Assert.Equal(2, f.Builds);
    }

    [Fact]
    public void NewWorldRebuildsFromItsRecipesAndDisposalUnsubscribes()
    {
        using var scope = new NativeScope();
        using var f = new LifecycleFixture();
        f.FinalizeWorld();
        var old = f.System.Catalogue;
        f.Events.Raise(events => events.LeaveWorld += null);
        f.Recipes.Clear();
        f.Recipes.Add(f.F.Recipe(f.F.Ingredient(f.F.Stack(2, "b", 1))));
        f.FinalizeWorld();
        Assert.Equal(2, f.Builds);
        Assert.NotSame(old, f.System.Catalogue);
        Assert.Equal("b", Assert.Single(f.System.Catalogue!.Recipes).Ingredients[0]!.Code!.Path);
        f.System.Dispose();
        f.FinalizeWorld();
        Assert.Equal(2, f.Builds);
        Assert.Null(f.System.Catalogue);
        Assert.Empty(f.Queue);
        f.Events.VerifyRemove(events => events.LevelFinalize -= It.IsAny<Action>(), Times.AtLeastOnce);
        f.Events.VerifyRemove(events => events.LeaveWorld -= It.IsAny<Action>(), Times.AtLeastOnce);
        var composed = (Delegate?)typeof(CraftingMenuDialogPatch).GetField("Composed",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Assert.DoesNotContain(composed?.GetInvocationList() ?? Array.Empty<Delegate>(),
            handler => ReferenceEquals(handler.Target, f.System));
    }

    [Fact]
    public void LifecycleRebuildRejectsNonOwningThread()
    {
        using var scope = new NativeScope();
        using var f = new LifecycleFixture();
        Exception? error = null;
        var worker = new Thread(() => error = Record.Exception(f.FinalizeWorld));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, f.Builds);
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void NativeTilesFitAndClipVisibleIconsAndDisposeTheirTexturesAndSubscriptions(float scale)
    {
        using var scope = new NativeScope();
        RuntimeEnv.GUIScale = scale;
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 256).Select(id =>
            f.Recipe(f.Ingredient(f.Stack(id, "ingredient-" + id, 1)))).ToArray();
        f.Register(recipes);
        int captures = 0;
        var session = new CraftingMenuSession(f.Catalogue.Build(recipes), f.Allocator, () =>
        {
            captures++;
            return CraftingInventorySnapshot.Capture(f.Player, 3, 3, RuntimeEnv.MainThreadId, true);
        }, (_, _) => throw new InvalidOperationException("No placement expected."));
        session.Refresh();
        using var graphics = new CraftingRenderFixture(scale);
        var render = graphics.Render;
        var liveTextures = graphics.Textures;
        render.Setup(value => value.RenderMesh(It.IsAny<MeshRef>())).Callback(() =>
        {
            var bounds = Assert.Single(graphics.Clips);
            Assert.Equal(46 * scale, bounds.InnerWidth, 4);
            Assert.Equal(46 * scale, bounds.InnerHeight, 4);
            Assert.Same(graphics.Programs[^1].Object, graphics.Active);
        });
        var viewport = ElementBounds.Fixed(0, 0, 96, 96).WithEmptyParent();
        viewport.CalcWorldBounds();
        using var surface = new ImageSurface(Format.Argb32, 96, 144);
        using var ctx = new Context(surface);
        for (int cycle = 0; cycle < 5; cycle++)
        {
            int inspected = 0;
            using var tiles = new GuiElementCraftingTiles(graphics.Api.Object,
                ElementBounds.Fixed(0, 0, 96, 144).WithParent(viewport), viewport, session,
                _ => inspected++, (_, _) => { }, graphics.Cache, graphics.CreateShader);
            tiles.BeforeCalcBounds();
            tiles.Bounds.CalcWorldBounds();
            tiles.ComposeElements(ctx, surface);
            Assert.Equal(25, liveTextures.Count);
            int beforeBakes = graphics.Bakes.Count;
            for (int frame = 0; frame < 6; frame++)
            {
                render.Invocations.Clear();
                foreach (var program in graphics.Programs) program.Invocations.Clear();
                tiles.RenderInteractiveElements(0);
                Assert.Equal(4, render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderMesh)));
                Assert.Equal(cycle == 0 ? 4 : beforeBakes, graphics.Bakes.Count);
                Assert.Equal(cycle == 0 ? 4 : 6, graphics.Allocations.Count);
                graphics.Programs[^1].Verify(value => value.Use(), Times.Once);
                Assert.Empty(graphics.Clips);
            }
            tiles.Scroll(24 * scale);
            for (int frame = 0; frame < 2; frame++)
            {
                render.Invocations.Clear();
                tiles.RenderInteractiveElements(0);
                Assert.Equal(6, render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderMesh)));
                Assert.Equal(cycle == 0 ? 6 : beforeBakes, graphics.Bakes.Count);
            }
            Assert.Equal(6, graphics.Allocations.Count);
            Assert.All(graphics.Bakes, bake =>
            {
                Assert.Equal((int)Math.Ceiling(46 * scale / 2) * 2, bake.Size);
                Assert.Equal(2 * GuiElementPassiveItemSlot.unscaledItemSize / GuiElementPassiveItemSlot.unscaledSlotSize,
                    bake.Scale, 5);
            });
            Assert.Empty(graphics.Clips);
            Assert.Equal(1, captures);
            tiles.Dispose();
            Assert.Empty(liveTextures);
            Assert.Equal(6, graphics.Allocations.Count);
            Assert.True(graphics.Meshes[^1].Object.Disposed);
            int afterClose = inspected;
            session.Search("");
            Assert.Equal(afterClose, inspected);
        }
        graphics.Cache.Dispose();
        Assert.Empty(graphics.Allocations);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public void WorldLifecycleOwnsReadyAndDeferredIconAllocations(int cleanup, bool immediate)
    {
        using var scope = new NativeScope();
        using var graphics = new CraftingRenderFixture { CompleteImmediately = immediate };
        using var f = new LifecycleFixture();
        f.Api.SetupGet(value => value.Render).Returns(graphics.Render.Object);
        f.Api.SetupGet(value => value.ItemTextureAtlas).Returns(graphics.Api.Object.ItemTextureAtlas);
        f.FinalizeWorld();
        var cache = Assert.IsType<CraftingIconCache>(f.System.IconCache);
        var recipe = Assert.Single(f.System.Catalogue!.Recipes);
        var position = cache.Get(recipe, 46, true, out bool baked);
        Assert.True(baked);
        Assert.Equal(immediate, position != null);
        Assert.Single(graphics.Bakes);
        if (cleanup == 0) f.Events.Raise(value => value.LeaveWorld += null);
        else if (cleanup == 1) f.System.Dispose();
        else f.FinalizeWorld();
        if (!immediate) graphics.Complete(graphics.Bakes[0]);
        Assert.Empty(graphics.Allocations);
        Assert.NotSame(cache, f.System.IconCache);
        Assert.Throws<ObjectDisposedException>(() => cache.Get(recipe, 46, true, out _));
        f.Events.VerifyRemove(value => value.ReloadTextures -= It.IsAny<Action>(), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RepeatedDialogCleanupDetachesCallbacksAndCancelsQueuedInventoryWork(int closeMode)
    {
        using var scope = new NativeScope();
        using var f = new LifecycleFixture();
        var gui = new Mock<IGuiAPI>();
        var loaded = new List<GuiDialog>();
        gui.SetupGet(value => value.LoadedGuis).Returns(loaded);
        gui.Setup(value => value.RegisterDialog(It.IsAny<GuiDialog[]>()))
            .Callback<GuiDialog[]>(dialogs => loaded.AddRange(dialogs));
        f.Api.SetupGet(value => value.Gui).Returns(gui.Object);
        f.Api.SetupGet(value => value.Network).Returns(Mock.Of<IClientNetworkAPI>());
        f.World.SetupGet(value => value.Player).Returns(f.F.Player);
        var manager = (ClientPlayerInventoryManager)f.F.Player.InventoryManager;
        manager.Inventories.Clear();
        var grid = new InventoryCraftingGrid("craftinggrid-test", null!) { Api = f.F.Api.Object };
        grid.InvNetworkUtil.Api = f.F.Api.Object;
        grid.Open(f.F.Player);
        manager.Inventories.Add(grid.InventoryID, grid);
        using var parent = new ParentDialog(f.Api.Object);
        using var composer = GuiComposer.CreateEmpty(f.Api.Object);
        for (int cycle = 0; cycle < 5; cycle++)
        {
            if (f.System.Catalogue == null) f.FinalizeWorld();
            Assert.True(parent.TryOpen());
            using var dialog = new GuiDialogCraftingMenu(f.Api.Object, parent, composer,
                f.System.Catalogue!, f.F.Allocator, f.System.IconCache!);
            f.Api.Object.Gui.RegisterDialog(dialog);
            int disposed = 0;
            dialog.Disposed += () => disposed++;
            var session = (CraftingMenuSession)typeof(GuiDialogCraftingMenu).GetField("session",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            grid.DidModifyItemSlot(grid[0]);
            Assert.Single(f.Queue);
            if (closeMode == 0) parent.TryClose();
            else if (closeMode == 1) f.Events.Raise(value => value.LeaveWorld += null);
            else dialog.Dispose();
            f.Drain();
            Assert.Empty(session.Rows);
            Assert.DoesNotContain(dialog, loaded);
            Assert.Equal(1, disposed);
            dialog.Dispose();
            Assert.Equal(1, disposed);
            grid.DidModifyItemSlot(grid[0]);
            Assert.Empty(f.Queue);
        }
    }

    private sealed class MenuFixture : IDisposable
    {
        private readonly CraftingRenderFixture graphics;
        internal readonly LifecycleFixture Lifecycle;
        internal readonly ParentDialog Parent;
        private readonly List<GuiDialog> loaded = new();
        private readonly List<GuiComposer> composers = new();
        private readonly string originalLocale = Lang.CurrentLocale;
        private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        internal IEnumerable<GuiDialogCraftingMenu> Menus => loaded.OfType<GuiDialogCraftingMenu>();
        internal GuiElementCraftingMenuButton ToggleButton =>
            (GuiElementCraftingMenuButton)Parent.Composers["maininventory"].GetToggleButton("vanillaexpanded-crafting-toggle");
        internal readonly IAsset BookAsset;
        internal readonly List<(IAsset Asset, int Width, int Height, int? Color)> BookDraws = new();
        internal int TextureCount => graphics.Textures.Count;

        internal MenuFixture(bool enabled = true, float scale = 1, bool longLabels = false, bool includeRecipes = false)
        {
            graphics = new CraftingRenderFixture(scale);
            Lifecycle = new LifecycleFixture(enabled);
            var translations = new Mock<ITranslationService>();
            translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key switch
                {
                    "vanillaexpanded:craftingmenu-title" => longLabels ? "Available crafting recipes" : "Crafting recipes",
                    "vanillaexpanded:craftingmenu-auto" => longLabels ? "Automatic selection" : "Auto",
                    "vanillaexpanded:craftingmenu-handbook" => longLabels ? "Open survival handbook" : "Handbook",
                    "vanillaexpanded:craftingmenu-craftable-only" => longLabels
                        ? "Only recipes with all ingredients available" : "Craftable only",
                    "vanillaexpanded:craftingmenu-empty" => "No recipes are available in this category.",
                    "vanillaexpanded:craftingmenu-empty-search" => "No recipes match your search in this category.",
                    "vanillaexpanded:craftingmenu-empty-craftable" => "No recipes in this category are currently craftable.",
                    "vanillaexpanded:craftingmenu-empty-craftable-search" => longLabels
                        ? "Recipes match the current search in the selected category, but none can currently be crafted with the available ingredients."
                        : "Recipes match your search, but none are currently craftable.",
                    "vanillaexpanded:craftingmenu-clear-search" => longLabels ? "Clear the current search text" : "Clear search",
                    "vanillaexpanded:craftingmenu-show-uncraftable" => longLabels
                        ? "Show recipes with unavailable ingredients" : "Show uncraftable",
                    "vanillaexpanded:craftingmenu-toggle" => "Recipes",
                    "vanillaexpanded:craftingmenu-category-all" => "All",
                    "vanillaexpanded:craftingmenu-category-other" => "Other",
                    _ => key
                });
            Lang.AvailableLanguages["en"] = translations.Object;
            Lang.ChangeLanguage("en");
            var api = Lifecycle.Api;
            api.SetupGet(value => value.Gui).Returns(graphics.Gui.Object);
            api.SetupGet(value => value.Render).Returns(graphics.Render.Object);
            api.SetupGet(value => value.Input).Returns(graphics.Api.Object.Input);
            api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
            api.SetupGet(value => value.Network).Returns(Mock.Of<IClientNetworkAPI>());
            var registry = new Mock<IClassRegistryAPI>();
            registry.Setup(value => value.CreateInvNetworkUtil(It.IsAny<InventoryBase>(), api.Object))
                .Returns(Mock.Of<IInventoryNetworkUtil>());
            api.SetupGet(value => value.ClassRegistry).Returns(registry.Object);
            var assets = new Mock<IAssetManager>();
            assets.Setup(value => value.Get(It.IsAny<AssetLocation>()))
                .Returns(Mock.Of<IAsset>(value => value.Data == Array.Empty<byte>()));
            byte[] svg = File.ReadAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                @"..\..\..\..\VanillaExpanded\assets\vanillaexpanded\textures\gui\crafting-book.svg")));
            BookAsset = Mock.Of<IAsset>(value => value.Data == svg);
            assets.Setup(value => value.Get(new AssetLocation("vanillaexpanded:textures/gui/crafting-book.svg")))
                .Returns(BookAsset);
            api.SetupGet(value => value.Assets).Returns(assets.Object);
            graphics.Gui.Setup(value => value.DrawSvg(It.IsAny<IAsset>(), It.IsAny<ImageSurface>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
                .Callback<IAsset, ImageSurface, int, int, int, int, int?>((asset, _, _, _, width, height, color) =>
                    BookDraws.Add((asset, width, height, color)));
            graphics.Render.Setup(value => value.BitmapCreateFromPng(It.IsAny<byte[]>()))
                .Returns(() => new BitmapExternal(2, 2));
            graphics.Gui.SetupGet(value => value.Icons).Returns(new IconUtil(api.Object));
            graphics.Gui.SetupGet(value => value.LoadedGuis).Returns(loaded);
            graphics.Gui.Setup(value => value.RegisterDialog(It.IsAny<GuiDialog[]>()))
                .Callback<GuiDialog[]>(dialogs => loaded.AddRange(dialogs));
            var window = ElementBounds.Fixed(0, 0, 1920, 1080).WithEmptyParent();
            window.CalcWorldBounds();
            graphics.Gui.SetupGet(value => value.WindowBounds).Returns(window);
            graphics.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
                .Returns((string name, ElementBounds bounds) => CreateComposer(name, bounds));
            Lifecycle.World.SetupGet(value => value.Player).Returns(Lifecycle.F.Player);
            Lifecycle.World.SetupGet(value => value.Rand).Returns(new Random(123));
            var manager = (ClientPlayerInventoryManager)Lifecycle.F.Player.InventoryManager;
            manager.Inventories.Clear();
            var grid = new InventoryCraftingGrid("craftinggrid-test", null!) { Api = Lifecycle.F.Api.Object };
            grid.InvNetworkUtil.Api = Lifecycle.F.Api.Object;
            grid.Open(Lifecycle.F.Player);
            manager.Inventories.Add(grid.InventoryID, grid);
            if (!includeRecipes) Lifecycle.Recipes.Clear();
            Lifecycle.FinalizeWorld();
            Parent = new ParentDialog(api.Object);
            Recompose();
            Lifecycle.System.Attach(Parent, Parent.Composers["maininventory"]);
        }

        private GuiComposer CreateComposer(string name, ElementBounds bounds) =>
            (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { Lifecycle.Api.Object, bounds, name }, null)!;

        internal void Recompose()
        {
            foreach (var recipe in Lifecycle.Recipes)
                if (recipe.Output?.ResolvedItemStack?.Collectible is MockItem item)
                    item.SetApi(Lifecycle.Api.Object);
            var composer = CreateComposer("inventory", ElementBounds.Fixed(500, 100, 400, 500).WithEmptyParent());
            composers.Add(composer);
            composer.AddStaticElement(new GuiElementParent(Lifecycle.Api.Object,
                ElementBounds.Fixed(20, 50, 144, 144)), "craftinggrid");
            composer.AddStaticElement(new GuiElementParent(Lifecycle.Api.Object,
                ElementBounds.Fixed(180, 90, 48, 48)), "outputslot");
            Lifecycle.System.Attach(Parent, composer);
            Parent.Composers["maininventory"] = composer;
        }

        internal void Toggle()
        {
            var button = ToggleButton.Bounds;
            int x = (int)(button.absX + 5);
            int y = (int)(button.absY + 5);
            var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            Parent.OnMouseDown(down);
            Assert.True(down.Handled);
            Parent.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        }

        internal void ToggleCraftable(GuiDialogCraftingMenu menu)
        {
            var bounds = menu.SingleComposer.GetToggleButton("craftable-only").Bounds;
            int x = (int)(bounds.absX + 5);
            int y = (int)(bounds.absY + 5);
            var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            menu.OnMouseDown(down);
            Assert.True(down.Handled);
            menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        }

        internal void Click(GuiElement button, bool handled = true)
        {
            var args = new MouseEvent((int)(button.Bounds.absX + 5), (int)(button.Bounds.absY + 5),
                EnumMouseButton.Left, 0);
            button.OnMouseDownOnElement(Lifecycle.Api.Object, args);
            button.OnMouseUpOnElement(Lifecycle.Api.Object, args);
            Assert.Equal(handled, args.Handled);
        }

        internal void SetMouse(int x, int y)
        {
            var input = Mock.Get(Lifecycle.Api.Object.Input);
            input.SetupGet(value => value.MouseX).Returns(x);
            input.SetupGet(value => value.MouseY).Returns(y);
        }

        internal void AssertToggleHidden()
        {
            Assert.False(ToggleButton.Enabled);
            AssertOpenerInvisible();
            Assert.Empty(Menus);
        }

        internal void AssertOpenerInvisible()
        {
            Assert.False(ToggleButton.Visible);
            Assert.False(ToggleButton.Focusable);
            graphics.Render.Invocations.Clear();
            ToggleButton.RenderInteractiveElements(0);
            Assert.Empty(graphics.Render.Invocations);
            var mouse = new MouseEvent((int)ToggleButton.Bounds.absX + 5, (int)ToggleButton.Bounds.absY + 5,
                EnumMouseButton.Left, 0);
            ToggleButton.OnMouseDownOnElement(Lifecycle.Api.Object, mouse);
            Assert.False(mouse.Handled);
            Parent.OnMouseDown(mouse);
            Assert.False(mouse.Handled);
            ToggleButton.OnFocusGained();
            var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
            ToggleButton.OnKeyDown(Lifecycle.Api.Object, enter);
            Assert.False(enter.Handled);
            var hint = ToggleButton.Hint;
            var input = Mock.Get(Lifecycle.Api.Object.Input);
            input.SetupGet(value => value.MouseX).Returns(mouse.X);
            input.SetupGet(value => value.MouseY).Returns(mouse.Y);
            hint.RenderInteractiveElements(0);
            Assert.False(hint.IsVisible);
            Assert.False(hint.IsNowShown);
        }

        internal void ResizePanelSpace(double width, double height)
        {
            var window = graphics.Gui.Object.WindowBounds;
            window.fixedWidth = 906 + width;
            window.fixedHeight = height;
            window.MarkDirtyRecursive();
            window.CalcWorldBounds();
            var parent = Parent.Composers["maininventory"].Bounds;
            parent.fixedY = 0;
            parent.fixedHeight = height;
            parent.MarkDirtyRecursive();
            parent.CalcWorldBounds();
        }

        internal void SetEnabled(bool enabled)
        {
            Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(VanillaExpandedModSystem.Config,
                new Vintagestory.API.Datastructures.StringAttribute(
                    "{\"EnableCraftingMenu\":\"" + (enabled ? "true" : "false") + "\"}")));
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.Systems).Returns(Lifecycle.Loaded
                ? new ModSystem[] { Lifecycle.System } : Array.Empty<ModSystem>());
            Lifecycle.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            LiveConfigReload.NotifyAll(Lifecycle.Api.Object);
        }

        internal void ClickCloseButton(GuiDialogCraftingMenu menu)
        {
            var titlebar = menu.SingleComposer.GetTitleBar("titlebar");
            var args = new MouseEvent((int)(titlebar.Bounds.absX + titlebar.Bounds.OuterWidth
                - GuiElement.scaled(12 + GuiElementDialogTitleBar.unscaledCloseIconSize / 2.0)),
                (int)(titlebar.Bounds.absY + GuiElement.scaled(10)), EnumMouseButton.Left, 0);
            titlebar.OnMouseUpOnElement(Lifecycle.Api.Object, args);
            Assert.True(args.Handled);
        }

        public void Dispose()
        {
            Lifecycle.Dispose();
            Parent.Dispose();
            foreach (var composer in composers.Take(composers.Count - 1)) composer.Dispose();
            graphics.Dispose();
            if (originalEnglish == null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = originalEnglish;
            Lang.ChangeLanguage(originalLocale);
        }
    }

    private sealed class ParentDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        internal Action? Opening;
        public override string ToggleKeyCombinationCode => "inventory";
        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            Opening?.Invoke();
        }
    }

    private sealed class NativeScope : IDisposable
    {
        private readonly int thread = RuntimeEnv.MainThreadId;
        private readonly float scale = RuntimeEnv.GUIScale;
        internal NativeScope()
        {
            RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;
            RuntimeEnv.GUIScale = 1;
        }
        public void Dispose()
        {
            RuntimeEnv.MainThreadId = thread;
            RuntimeEnv.GUIScale = scale;
        }
    }

    private sealed class LifecycleFixture : IDisposable
    {
        internal readonly AllocationFixture F = new();
        internal readonly Mock<ICoreClientAPI> Api = new();
        internal readonly Mock<IClientWorldAccessor> World = new();
        internal readonly Mock<IClientEventAPI> Events = new();
        internal readonly Queue<Action> Queue = new();
        internal readonly List<GridRecipe> Recipes = new();
        internal readonly CraftingMenuSystem System;
        internal readonly bool Loaded;
        internal int Builds;
        private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableCraftingMenu;

        internal LifecycleFixture(bool enabled = true)
        {
            VanillaExpandedModSystem.Config.EnableCraftingMenu = enabled;
            Recipes.Add(F.Recipe(F.Ingredient(F.Stack(1, "a", 1))));
            F.Register(Recipes.ToArray());
            Api.SetupGet(value => value.World).Returns(World.Object);
            Api.SetupGet(value => value.ModLoader).Returns(Mock.Of<IModLoader>());
            Api.SetupGet(value => value.Event).Returns(Events.Object);
            World.SetupGet(value => value.GridRecipes).Returns(Recipes);
            World.SetupGet(value => value.FastSearchRecipesByIngredient).Returns(F.Catalogue.Index);
            Events.Setup(value => value.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Queue.Enqueue(action));
            System = new CraftingMenuSystem(_ =>
            {
                Builds++;
                return (F.Catalogue.Build(Recipes.ToArray()), F.Allocator);
            });
            Loaded = System.ShouldLoad(EnumAppSide.Client);
            if (Loaded) System.StartClientSide(Api.Object);
        }

        internal void FinalizeWorld() => Events.Raise(value => value.LevelFinalize += null);
        internal void Drain() { while (Queue.TryDequeue(out var action)) action(); }

        public void Dispose()
        {
            System.Dispose();
            VanillaExpandedModSystem.Config.EnableCraftingMenu = originalEnabled;
        }
    }
}
