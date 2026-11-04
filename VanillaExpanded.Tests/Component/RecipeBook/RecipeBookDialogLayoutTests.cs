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
public sealed class RecipeBookDialogLayoutTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void RecipeListInsetFramesViewportAndScrollbarWithoutOverlappingAdjacentControls(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        for (int i = 0; i < 80; i++)
            f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-" + i));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        AssertFrame();
        var composer = book.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth + 40, composer.Bounds.fixedHeight - 10);
        book.TryOpen();
        AssertFrame();

        void AssertFrame()
        {
            var composer = book.SingleComposer;
            var viewport = composer.GetElement("tiles").InsideClipBounds;
            var scrollbar = composer.GetScrollbar("scroll").Bounds;
            AssertContained(viewport, composer.Bounds);
            AssertContained(scrollbar, composer.Bounds);
            Assert.True(viewport.absX + viewport.OuterWidth <= scrollbar.absX
                || scrollbar.absX + scrollbar.OuterWidth <= viewport.absX);
            var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
            tiles.ResetScroll();
            Assert.All(tiles.Elements, tile => Assert.Same(viewport, tile.InsideClipBounds));
            foreach (float position in new[] { 30f, 33f, 34f, 71f })
            {
                tiles.Scroll(position * scale);
                Assert.Equal(position, tiles.ScrollPosition);
                var first = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
                Assert.NotNull(first.Selection);
                Assert.True(first.Bounds.absY <= viewport.absY);
                Assert.True(first.Bounds.absY + first.Bounds.OuterHeight >= viewport.absY);
            }
            tiles.ResetScroll();
            composer.GetTextInput("search").SetValue("no matching recipe");
            var render = Mock.Get(f.Lifecycle.Api.Object.Render);
            render.Invocations.Clear();
            tiles.RenderInteractiveElements(0);
            Assert.Contains(render.Invocations, call => call.Method.Name == nameof(IRenderAPI.PushScissor)
                && ReferenceEquals(call.Arguments[0], viewport) && (bool)call.Arguments[1]);
            composer.GetTextInput("search").SetValue("");
            var search = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search")).FieldBounds;
            Assert.True(search.absY + search.OuterHeight <= viewport.absY);
            Assert.True(viewport.absY + viewport.OuterHeight <= composer.GetButton("previous").Bounds.absY);
            AssertContained(search, composer.Bounds);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.25, false)]
    [InlineData(1.5, false)]
    [InlineData(1.75, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.25, true)]
    [InlineData(1.5, true)]
    [InlineData(1.75, true)]
    [InlineData(2, true)]
    public void ResponsiveDialogRecompositionRestoresFocusedRecipeAndRowAnchor(float scale, bool keyboard)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        for (int i = 0; i < 100; i++)
            f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-" + i));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var original = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        session.Select(session.Rows[36].Output.Id);
        original.Scroll(157 * scale);
        if (keyboard) original.OnKeyDown(f.Lifecycle.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Tab });
        book.SingleComposer.FocusElement(original.TabIndex);
        original.Scroll(157 * scale);
        original.FocusElement(original.Geometry.Columns);
        var before = original.CaptureView();
        int selected = session.SelectedOutput!.Value;
        double minimumWidth = (double)typeof(GuiDialogRecipeBook).GetField("minimumWidth",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        f.ResizePanelSpace(minimumWidth, 420);
        book.TryOpen();
        var resized = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        Assert.NotSame(original, resized);
        Assert.True(resized.Geometry.Columns < original.Geometry.Columns);
        var after = resized.CaptureView();
        var restoredAnchor = Assert.Single(resized.Elements.Cast<GuiElementRecipeBookTile>()
            .Where(tile => tile.Selection?.Output.Id == before.AnchorOutput));
        var visibleTiles = resized.Elements.Cast<GuiElementRecipeBookTile>()
            .Where(tile => tile.Selection != null && tile.IsInViewport).ToArray();
        Assert.NotEmpty(visibleTiles);
        Assert.Contains(restoredAnchor, visibleTiles);
        Assert.Equal(visibleTiles.Min(tile => tile.Bounds.renderY), restoredAnchor.Bounds.renderY, 3);
        Assert.Equal(before.RowOffset, after.RowOffset, 3);
        Assert.Equal(before.FocusedOutput, after.FocusedOutput);
        Assert.Equal(before.InspectedOutput, after.InspectedOutput);
        Assert.Equal(selected, session.SelectedOutput);
        Assert.True(resized.HasFocus);
        Assert.Equal(keyboard, after.KeyboardFocus);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(1.25, 1.75)]
    [InlineData(1.75, 1.25)]
    public void ScaleTransitionCapturesTheOldCompositionRowBeforeRestoringAtTheNewScale(float oldScale, float newScale)
    {
        // Arrange scroll and inspection state in the old composition before changing GUI scale.
        using var f = new MenuFixture(scale: oldScale, includeRecipes: true);
        for (int i = 0; i < 100; i++)
            f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-" + i));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var original = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        original.Scroll(157 * oldScale);
        var first = Assert.IsType<GuiElementRecipeBookTile>(original.Elements[0]);
        original.UpdateHover((int)first.PixelBounds.renderX + 5, (int)original.InsideClipBounds.renderY + 5);
        var before = original.CaptureView();
        // Act: change scale only after capturing the anchor, then let the dialog recompose.
        RuntimeEnv.GUIScale = newScale;
        Assert.Equal(157, original.ScrollPosition, 4);
        book.TryOpen();
        var resized = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        Assert.NotSame(original, resized);
        var after = resized.CaptureView();

        // Assert the new composition restores the old anchor and inspection state.
        Assert.Equal(before.AnchorOutput, after.AnchorOutput);
        Assert.Equal(before.RowOffset, after.RowOffset, 4);
        Assert.Equal(before.InspectedOutput, after.InspectedOutput);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void BookToggleStaysLeftOfOutputSlotWhenParentAndOutputMoveResizeAndRecompose(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        AssertBookTabPosition(f);
        var composer = f.Parent.Composers["maininventory"];
        composer.Bounds.fixedHeight += 120;
        composer.Bounds.MarkDirtyRecursive();
        composer.ReCompose();
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
        var output = composer.GetElement("outputslot").Bounds;
        output.fixedX += 24;
        output.fixedY += 16;
        output.fixedHeight += 16;
        composer.Bounds.MarkDirtyRecursive();
        composer.ReCompose();
        AssertBookTabPosition(f);
        f.Recompose();
        AssertBookTabPosition(f);
        f.Parent.TryOpen();
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.25, false)]
    [InlineData(1.5, false)]
    [InlineData(1.75, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.25, true)]
    [InlineData(1.5, true)]
    [InlineData(1.75, true)]
    [InlineData(2, true)]
    public void ComposedControlsStaySeparatedAtMinimumSizeAndAfterResize(float scale, bool longLabels)
    {
        using var f = new MenuFixture(scale: scale, longLabels: longLabels);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        AssertLayout(menu);
        var originalComposer = menu.SingleComposer;
        var search = originalComposer.GetTextInput("search");
        double minimumWidth = (double)typeof(GuiDialogRecipeBook).GetField("minimumWidth",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        double minimumHeight = (double)typeof(GuiDialogRecipeBook).GetField("minimumHeight",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
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
        f.ResizePanelSpace(Math.Max(340, minimumWidth), 420);
        menu.TryOpen();
        Assert.Equal("preserved search", menu.SingleComposer.GetTextInput("search").GetText());
        AssertLayout(menu);

        static void AssertLayout(GuiDialogRecipeBook menu)
        {
            var composer = menu.SingleComposer;
            var panel = composer.Bounds;
            var title = composer.GetTitleBar("titlebar");
            var search = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search")).FieldBounds;
            var tabs = GetCategoryTabs(composer).Bounds;
            var categoryTitle = composer.GetDynamicText("category-title");
            var craftable = composer.GetSwitch("craftable-only");
            var craftableLabel = composer.GetStaticText("craftable-label");
            var sort = composer.GetDropDown("sort");
            var viewport = composer.GetElement("tiles").Bounds.ParentBounds;
            var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
            var scroll = composer.GetScrollbar("scroll").Bounds;
            var emptyElement = composer.GetDynamicText("empty");
            var empty = emptyElement.Bounds;
            var previous = composer.GetElement("previous").Bounds;
            var auto = composer.GetElement("auto").Bounds;
            var next = composer.GetElement("next").Bounds;
            var counter = composer.GetDynamicText("alternative").Bounds;
            var handbook = composer.GetButton("handbook");
            AssertContained(title.Bounds, panel);
            AssertContained(tabs, panel);
            AssertContained(categoryTitle.Bounds, panel);
            AssertContained(search, panel);
            AssertContained(craftable.Bounds, panel);
            AssertContained(craftableLabel.Bounds, panel);
            AssertContained(sort.Bounds, panel);
            AssertContained(viewport, panel);
            AssertContained(scroll, panel);
            AssertContained(previous, panel);
            AssertContained(auto, panel);
            AssertContained(next, panel);
            AssertContained(counter, panel);
            AssertContained(handbook.Bounds, panel);
            Assert.True(categoryTitle.Font.GetTextExtents(categoryTitle.GetText()).Width <= categoryTitle.Bounds.InnerWidth);
            // Native mouse-up handling consumes these computed local rectangles as functional title-bar hit targets.
            var titleBarType = typeof(GuiElementDialogTitleBar);
            var closeTarget = Assert.IsType<Vintagestory.API.Datastructures.Rectangled>(
                titleBarType.GetField("closeIconRect", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(title));
            var menuTarget = Assert.IsType<Vintagestory.API.Datastructures.Rectangled>(
                titleBarType.GetField("menuIconRect", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(title));
            // Locate the actual caption extent from its current draw origin; the inset itself is not a tested value.
            double titleTextRight = (int)composer.Bounds.renderX + title.Bounds.drawX + GuiElement.scaled(GuiStyle.ElementToDialogPadding)
                + title.Font.GetTextExtents(title.GetText()).Width;
            Assert.True(titleTextRight <= title.Bounds.absX + closeTarget.X);
            Assert.True(titleTextRight <= title.Bounds.absX + menuTarget.X);
            foreach (var target in new[] { closeTarget, menuTarget })
            {
                Assert.True(target.Width > 0 && target.Height > 0);
                Assert.True(target.X >= 0 && target.Y >= 0);
                Assert.True(target.X + target.Width <= title.Bounds.OuterWidth);
                Assert.True(target.Y + target.Height <= title.Bounds.OuterHeight);
                double left = title.Bounds.absX + target.X;
                double top = title.Bounds.absY + target.Y;
                Assert.True(left >= panel.absX && left + target.Width <= panel.absX + panel.OuterWidth);
                Assert.True(top >= panel.absY && top + target.Height <= panel.absY + panel.OuterHeight);
            }
            Assert.True(closeTarget.X + closeTarget.Width <= menuTarget.X
                || menuTarget.X + menuTarget.Width <= closeTarget.X
                || closeTarget.Y + closeTarget.Height <= menuTarget.Y
                || menuTarget.Y + menuTarget.Height <= closeTarget.Y);
            Assert.All(sort.listMenu.Names, name =>
                Assert.True(sort.Font.GetTextExtents(name).Width <= sort.Bounds.InnerWidth + .001));
            Assert.True(craftableLabel.Font.GetTextExtents(craftableLabel.GetText()).Width
                <= craftableLabel.Bounds.InnerWidth + .001);
            Assert.True(craftableLabel.Bounds.absX + craftableLabel.Bounds.OuterWidth <= sort.Bounds.absX
                || sort.Bounds.absX + sort.Bounds.OuterWidth <= craftableLabel.Bounds.absX);
            Assert.True(search.absY + search.OuterHeight <= viewport.absY);
            Assert.True(viewport.absX + viewport.OuterWidth <= scroll.absX
                || scroll.absX + scroll.OuterWidth <= viewport.absX);
            double tileExtent = GuiElement.scaled(tiles.TileSize);
            Assert.True(viewport.InnerWidth >= tileExtent);
            Assert.True(viewport.InnerHeight >= tileExtent);
            Assert.True(viewport.absY + viewport.OuterHeight <= previous.absY);
            Assert.True(previous.absX + previous.OuterWidth <= auto.absX);
            Assert.True(auto.absX + auto.OuterWidth <= next.absX);
            Assert.True(next.absX + next.OuterWidth <= counter.absX);
            Assert.True(counter.absX + counter.OuterWidth <= handbook.Bounds.absX);
            AssertButtonTextFits(handbook);
            var categoryTabs = GetCategoryTabs(composer);
            Assert.False(categoryTabs.Composer!.GetButton("left").Visible);
            Assert.False(categoryTabs.Composer.GetButton("right").Visible);
            AssertContained(empty, viewport);
            if (!string.IsNullOrEmpty(emptyElement.GetText()))
                Assert.True(new TextDrawUtil().GetMultilineTextHeight(emptyElement.Font, emptyElement.GetText(),
                    empty.InnerWidth) <= empty.InnerHeight + .001);
            foreach (string key in new[] { "clear-search", "show-uncraftable" })
            {
                var button = composer.GetButton(key);
                AssertButtonTextFits(button);
                if (button.Visible)
                    Assert.True(empty.absY + empty.OuterHeight <= button.Bounds.absY);
                AssertContained(button.Bounds, viewport);
            }

            static void AssertButtonTextFits(GuiElementTextButton button)
            {
                foreach (string fieldName in new[] { "normalText", "pressedText" })
                {
                    var text = Assert.IsType<GuiElementStaticText>(typeof(GuiElementTextButton)
                        .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button));
                    Assert.Equal(button.Text, text.GetText());
                    Assert.True(text.Font.GetTextExtents(text.GetText()).Width <= text.Bounds.InnerWidth + .001);
                }
            }
        }
    }

    [Theory]
    [InlineData(1f, 9)]
    [InlineData(1.25f, 18)]
    [InlineData(1.5f, 9)]
    [InlineData(1.75f, 18)]
    [InlineData(2f, 9)]
    public void CategoryPanelShowsAllTabsAndCurrentHeadingAcrossSelectionResizeAndReopen(float scale, int categoryCount)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        Assert.Single(f.Lifecycle.Recipes).Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs =
            Enumerable.Range(0, categoryCount).Select(index => "category-" + index).ToArray();
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var tabs = GetCategoryTabs(menu.SingleComposer);
        Assert.Equal(categoryCount + 1, tabs.tabs.Length);
        AssertPanel(menu);
        Assert.Equal("All", menu.SingleComposer.GetDynamicText("category-title").GetText());
        tabs.OnFocusGained();
        tabs.OnKeyDown(f.Lifecycle.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Right });
        Assert.Equal(tabs.tabs[1].Name, menu.SingleComposer.GetDynamicText("category-title").GetText());
        var lastTab = tabs.Composer!.GetToggleButton("tab-" + (tabs.tabs.Length - 1)).Bounds;
        int x = (int)(lastTab.absX + lastTab.OuterWidth / 2);
        int y = (int)(lastTab.absY + lastTab.OuterHeight / 2);
        menu.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        string selected = tabs.tabs[^1].Name;
        menu.SingleComposer.GetTextInput("search").SetValue("no results");
        AssertPanel(menu);
        Assert.Equal(selected, menu.SingleComposer.GetDynamicText("category-title").GetText());
        f.ResizePanelSpace(menu.SingleComposer.Bounds.fixedWidth + 24, menu.SingleComposer.Bounds.fixedHeight + 20);
        menu.TryOpen();
        AssertPanel(menu);
        Assert.Equal(selected, menu.SingleComposer.GetDynamicText("category-title").GetText());
        f.SelectMovement(menu, movable: true);
        menu.OnRenderGUI(0);
        AssertPanel(menu);
        f.Toggle();
        f.Toggle();
        menu = f.Menus.Last();
        Assert.True(menu.SingleComposer.GetTitleBar("titlebar").Movable);
        AssertPanel(menu);
        Assert.Equal(selected, menu.SingleComposer.GetDynamicText("category-title").GetText());

        static void AssertPanel(GuiDialogRecipeBook menu)
        {
            var tabs = GetCategoryTabs(menu.SingleComposer);
            Assert.False(tabs.Composer!.GetButton("left").Visible);
            Assert.False(tabs.Composer.GetButton("right").Visible);
            for (int index = 0; index < tabs.tabs.Length; index++)
            {
                var bounds = tabs.Composer.GetToggleButton("tab-" + index).Bounds;
                Assert.True(bounds.absX >= tabs.Viewport!.absX);
                Assert.True(bounds.absX + bounds.OuterWidth <= tabs.Viewport.absX + tabs.Viewport.InnerWidth + .001);
            }
            var heading = menu.SingleComposer.GetDynamicText("category-title");
            Assert.Equal(tabs.tabs[tabs.activeElement].Name, heading.GetText());
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void BaselineShellWiresControlsAndKeepsAlternativeCountUsable(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var first = Assert.Single(f.Lifecycle.Recipes);
        for (int i = 1; i < 12; i++)
        {
            var alternative = f.Lifecycle.F.Catalogue.Recipe("alternative-" + i);
            alternative.Output = first.Output!.Clone();
            f.Lifecycle.Recipes.Add(alternative);
        }
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var composer = book.SingleComposer;
        var input = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search"));
        Assert.Equal(0, input.TabIndex);
        Assert.True(composer.FocusElement(input.TabIndex));
        Assert.Same(input, composer.CurrentTabIndexElement);
        Assert.Equal("availability", composer.GetDropDown("sort").SelectedValue);
        var options = Assert.IsType<GuiElementRecipeBookOptions>(composer.GetElement("options"));
        Assert.NotNull(composer.GetHoverText("options-hint"));
        foreach (string key in new[] { "previous", "auto", "next", "handbook", "clear-search", "show-uncraftable" })
            Assert.NotNull(composer.GetButton(key));

        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        var row = Assert.Single(session.Rows);
        Assert.Equal(12, row.Output.Alternatives.Length);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Output.Alternatives[^1].Id);
        var counter = composer.GetDynamicText("alternative");
        Assert.Equal("12/12", counter.GetText());
        Assert.True(counter.Font.GetTextExtents(counter.GetText()).Width <= counter.Bounds.InnerWidth);
        Assert.True(composer.GetButton("next").Bounds.absX + composer.GetButton("next").Bounds.OuterWidth
            <= counter.Bounds.absX);
        Assert.True(counter.Bounds.absX + counter.Bounds.OuterWidth
            <= composer.GetButton("handbook").Bounds.absX);

        // Opening and toggling the options control must keep its setting wired to the UI.
        var opened = f.OpenOptions(book);
        Assert.True(opened.IsOpened);
        Assert.True(opened.Checkbox.On);
        f.ToggleReopenOption(book);
        Assert.False(opened.Checkbox.On);
        Assert.False(f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
    }

    private static void AssertContained(ElementBounds content, ElementBounds container)
    {
        Assert.True(content.absX >= container.absX - 0.001);
        Assert.True(content.absY >= container.absY - 0.001);
        Assert.True(content.absX + content.OuterWidth <= container.absX + container.OuterWidth + 0.001);
        Assert.True(content.absY + content.OuterHeight <= container.absY + container.OuterHeight + 0.001);
    }
}
