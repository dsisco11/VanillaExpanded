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
            var frame = Assert.IsType<GuiElementCustomDraw>(composer.GetElement("recipes-background")).Bounds;
            var viewport = composer.GetElement("tiles").InsideClipBounds;
            var scrollbar = composer.GetScrollbar("scroll").Bounds;
            double border = Math.Max(1, Math.Round(2 * (double)scale, MidpointRounding.AwayFromZero));
            Assert.Equal(frame.absX + border, viewport.absX, 4);
            Assert.Equal(frame.absY + border, viewport.absY, 4);
            Assert.Equal(frame.absY + frame.OuterHeight - border, viewport.absY + viewport.OuterHeight, 4);
            Assert.Equal(frame.absY + border, scrollbar.absY, 4);
            Assert.Equal(frame.absY + frame.OuterHeight - border, scrollbar.absY + scrollbar.OuterHeight, 4);
            var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
            tiles.ResetScroll();
            Assert.Equal(viewport.absY, tiles.Bounds.absY, 4);
            Assert.Equal(viewport.absY + Math.Round(2 * (double)scale, MidpointRounding.AwayFromZero),
                tiles.Elements[0].Bounds.absY, 4);
            Assert.All(tiles.Elements, tile => Assert.Same(viewport, tile.InsideClipBounds));
            foreach (float position in new[] { 30f, 33f, 34f, 71f })
            {
                tiles.Scroll(position * scale);
                Assert.Equal(position, tiles.ScrollPosition);
                var first = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
                Assert.NotNull(first.Selection);
                Assert.Equal(viewport.absY + Math.Round((2 - position % 50) * scale, MidpointRounding.AwayFromZero),
                    first.Bounds.absY, 4);
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
            Assert.Equal(scrollbar.absX + scrollbar.OuterWidth + border, frame.absX + frame.OuterWidth, 4);
            var search = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search")).FieldBounds;
            Assert.Equal(search.absX, frame.absX, 4);
            Assert.Equal(search.OuterWidth, frame.OuterWidth, 4);
            Assert.True(search.absY + search.OuterHeight < frame.absY);
            Assert.True(frame.absY + frame.OuterHeight < composer.GetButton("previous").Bounds.absY);
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
        Assert.Equal(7, before.RowOffset, 4);
        int selected = session.SelectedOutput!.Value;
        double minimumWidth = (double)typeof(GuiDialogRecipeBook).GetField("minimumWidth",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        f.ResizePanelSpace(minimumWidth, 420);
        book.TryOpen();
        var resized = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        Assert.NotSame(original, resized);
        Assert.True(resized.Geometry.Columns < original.Geometry.Columns);
        var after = resized.CaptureView();
        int anchorIndex = session.Rows.IndexOf(session.Rows.Single(row => row.Output.Id == before.AnchorOutput));
        Assert.Equal(anchorIndex / resized.Geometry.Columns * 50 + before.RowOffset, resized.ScrollPosition, 3);
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
        Assert.Equal(7, before.RowOffset, 4);
        RuntimeEnv.GUIScale = newScale;
        Assert.Equal(157, original.ScrollPosition, 4);
        book.TryOpen();
        var resized = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        Assert.NotSame(original, resized);
        var after = resized.CaptureView();
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
        var title = originalComposer.GetTitleBar("titlebar");
        var search = originalComposer.GetTextInput("search");
        double border = Math.Max(1, Math.Round(2 * (double)scale, MidpointRounding.AwayFromZero)) / scale;
        double viewportReserve = 48 + 2 * border;
        double counterWidth = originalComposer.GetDynamicText("alternative").Bounds.OuterWidth / scale;
        double navigationWidth = new[] { "previous", "auto", "next" }
            .Sum(key => originalComposer.GetElement(key).Bounds.OuterWidth / scale) + 6;
        double footerWidth = 32 + 2 * (Math.Max(navigationWidth,
            originalComposer.GetElement("handbook").Bounds.OuterWidth / scale) + 4) + counterWidth;
        double titleWidth = GuiStyle.ElementToDialogPadding + title.Font.GetTextExtents(title.GetText()).Width / scale
            + 8 + 80;
        var craftable = originalComposer.GetSwitch("craftable-only");
        var craftableLabel = originalComposer.GetStaticText("craftable-label");
        double craftableWidth = 32 + 17 + 16
            + craftableLabel.Font.GetTextExtents(craftableLabel.GetText()).Width / scale + 1
            + originalComposer.GetDropDown("sort").Bounds.OuterWidth / scale;
        double actionWidth = viewportReserve + new[] { "clear-search", "show-uncraftable" }
            .Max(key => originalComposer.GetButton(key).Bounds.OuterWidth / scale);
        double optionsWidth = Math.Ceiling(CairoFont.WhiteSmallText().WithFontSize(14).GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-remember-open")).Width / scale + 10) + 17 + 8 + 71;
        var originalTabs = GetCategoryTabs(originalComposer);
        var categoryTitle = originalComposer.GetDynamicText("category-title");
        double tabsWidth = 16 + GuiElementIconTabs.MeasureWidth(originalTabs.tabs.Length);
        double categoryWidth = 32 + originalTabs.tabs.Max(tab => categoryTitle.Font.GetTextExtents(tab.Name).Width / scale + 1);
        double minimumWidth = Math.Ceiling(new[] { optionsWidth, actionWidth, craftableWidth,
            footerWidth, titleWidth, tabsWidth, categoryWidth }.Max());
        double textHeight = Math.Ceiling(new[] { "empty", "empty-search", "empty-craftable", "empty-craftable-search" }
            .Max(key => new TextDrawUtil().GetMultilineTextHeight(CairoFont.WhiteSmallText().WithFontSize(14),
                Lang.Get("vanillaexpanded:recipebook-" + key), (minimumWidth - viewportReserve) * scale)) / scale);
        double emptyHeight = textHeight + 8
            + originalComposer.GetElement("auto").Bounds.OuterHeight / scale;
        double minimumHeight = Math.Ceiling(36 + 12 + search.Bounds.OuterHeight / scale
            + 10 + GetCategoryTabs(originalComposer).Bounds.OuterHeight / scale
            + 12 + categoryTitle.Bounds.OuterHeight / scale
            + 8 + 30
            + 10 + 2 * border + Math.Max(47, emptyHeight) + 12
            + originalComposer.GetElement("auto").Bounds.OuterHeight / scale + 16);
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
            AssertEmptyStateCentered(menu);
            var composer = menu.SingleComposer;
            var panel = composer.Bounds;
            var title = composer.GetTitleBar("titlebar");
            var search = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search")).FieldBounds;
            var tabs = GetCategoryTabs(composer).Bounds;
            var categoryPanel = composer.GetElement("category-panel").Bounds;
            var categoryTitle = composer.GetDynamicText("category-title");
            var craftable = composer.GetSwitch("craftable-only");
            var craftableLabel = composer.GetStaticText("craftable-label");
            var sort = composer.GetDropDown("sort");
            Assert.Null(composer.GetElement("sort-label"));
            var viewport = composer.GetElement("tiles").Bounds.ParentBounds;
            var scroll = composer.GetScrollbar("scroll").Bounds;
            var empty = composer.GetDynamicText("empty").Bounds;
            var previous = composer.GetElement("previous").Bounds;
            var auto = composer.GetElement("auto").Bounds;
            var next = composer.GetElement("next").Bounds;
            var counter = composer.GetDynamicText("alternative").Bounds;
            var handbook = composer.GetButton("handbook");
            double scale = GuiElement.scaled(1);
            double border = Math.Max(1, Math.Round(2 * scale, MidpointRounding.AwayFromZero));
            Assert.Equal(tabs.absX, categoryPanel.absX, 4);
            Assert.Equal(tabs.OuterWidth, categoryPanel.OuterWidth, 4);
            Assert.Equal(tabs.absY + tabs.OuterHeight - Math.Max(1,
                Math.Round(scale, MidpointRounding.AwayFromZero)), categoryPanel.absY, 4);
            Assert.Equal(categoryPanel.absX,
                GetCategoryTabs(composer).Composer!.GetToggleButton("tab-0").Bounds.absX, 4);
            Assert.True(categoryPanel.absX < search.absX);
            Assert.True(categoryPanel.absX + categoryPanel.OuterWidth > search.absX + search.OuterWidth);
            Assert.True(categoryPanel.absY + categoryPanel.OuterHeight > handbook.Bounds.absY + handbook.Bounds.OuterHeight);
            Assert.Equal(36 * scale, title.Bounds.OuterHeight, 4);
            Assert.Equal(14, title.Font.UnscaledFontsize);
            Assert.Equal(title.Bounds.absY + title.Bounds.OuterHeight + 12 * scale, tabs.absY, 4);
            Assert.Equal(tabs.absY + tabs.OuterHeight + 12 * scale, categoryTitle.Bounds.absY, 4);
            Assert.Equal(17 * scale, categoryTitle.Bounds.OuterHeight, 4);
            Assert.Equal(categoryTitle.Bounds.absY + categoryTitle.Bounds.OuterHeight + 8 * scale, sort.Bounds.absY, 4);
            Assert.True(categoryTitle.Font.GetTextExtents(categoryTitle.GetText()).Width <= categoryTitle.Bounds.InnerWidth);
            Assert.True(categoryTitle.Font.UnscaledFontsize < craftableLabel.Font.UnscaledFontsize);
            Assert.True(craftableLabel.Bounds.absX + craftableLabel.Bounds.OuterWidth + 8 * scale <= sort.Bounds.absX + .001);
            Assert.Equal(sort.Bounds.absY + sort.Bounds.OuterHeight + 10 * scale, search.absY, 4);
            Assert.Equal(search.absY + search.OuterHeight + 10 * scale + border, viewport.absY, 4);
            Assert.True(craftable.Bounds.OuterHeight < search.OuterHeight);
            Assert.Equal(30 * scale, sort.Bounds.OuterHeight, 4);
            Assert.Equal(28 * scale, search.OuterHeight, 4);
            Assert.True(sort.Font.UnscaledFontsize < composer.GetTextInput("search").Font.UnscaledFontsize);
            Assert.Equal(14, craftableLabel.Font.UnscaledFontsize);
            Assert.All(sort.listMenu.Names, name =>
                Assert.True(sort.Font.GetTextExtents(name).Width + 30 * scale <= sort.Bounds.InnerWidth + .001));
            Assert.Equal(craftable.Bounds.InnerWidth, craftable.Bounds.InnerHeight, 4);
            Assert.True(craftable.Bounds.absX + craftable.Bounds.OuterWidth + 8 * scale <= craftableLabel.Bounds.absX + .001);
            Assert.True(craftableLabel.Font.GetTextExtents(craftableLabel.GetText()).Width <=
                craftableLabel.Bounds.InnerWidth + .001);
            Assert.Equal(craftable.Bounds.absY + craftable.Bounds.InnerHeight / 2,
                craftableLabel.Bounds.absY + craftableLabel.Bounds.InnerHeight / 2, 4);
            Assert.Equal(sort.Bounds.absY + sort.Bounds.InnerHeight / 2,
                craftable.Bounds.absY + craftable.Bounds.InnerHeight / 2, 4);
            Assert.True(viewport.InnerHeight + .001 >= 47 * scale);
            Assert.True(viewport.absX + viewport.OuterWidth + 4 * scale <= scroll.absX + .001);
            Assert.Equal(viewport.absY, scroll.absY, 4);
            Assert.Equal(viewport.OuterHeight, scroll.OuterHeight, 4);
            Assert.Equal(viewport.absY + viewport.OuterHeight + 12 * scale + border, previous.absY, 4);
            Assert.True(previous.absX + previous.OuterWidth + 3 * scale <= auto.absX + .001);
            Assert.True(auto.absX + auto.OuterWidth + 3 * scale <= next.absX + .001);
            Assert.True(next.absX + next.OuterWidth + 4 * scale <= counter.absX + .001);
            Assert.True(counter.absX + counter.OuterWidth + 4 * scale <= handbook.Bounds.absX + .001);
            Assert.Equal(search.absX + search.OuterWidth / 2, counter.absX + counter.OuterWidth / 2, 4);
            Assert.Equal(search.absX, previous.absX, 4);
            Assert.Equal(search.absX + search.OuterWidth, handbook.Bounds.absX + handbook.Bounds.OuterWidth, 4);
            Assert.True(CairoFont.WhiteSmallText().WithFontSize(12).GetTextExtents(Lang.Get("vanillaexpanded:recipebook-handbook")).Width
                + 16 * scale <= handbook.Bounds.OuterWidth + .001);
            double titleTextRight = title.Bounds.absX + GuiElement.scaled(GuiStyle.ElementToDialogPadding)
                + title.Font.GetTextExtents(title.GetText()).Width;
            double titleMenuLeft = title.Bounds.absX + title.Bounds.OuterWidth
                - GuiElement.scaled(2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20);
            Assert.True(titleTextRight + 8 * scale <= titleMenuLeft + .001);
            Assert.Equal(panel.absX + 8 * scale, tabs.absX, 4);
            Assert.Equal(panel.InnerWidth - 16 * scale, tabs.OuterWidth, 4);
            Assert.Equal(categoryPanel.absX + 8 * scale, categoryTitle.Bounds.absX, 4);
            var categoryTabs = GetCategoryTabs(composer);
            Assert.False(categoryTabs.Composer!.GetButton("left").Visible);
            Assert.False(categoryTabs.Composer.GetButton("right").Visible);
            foreach (var bounds in new[] { categoryTitle.Bounds, search, craftable.Bounds, craftableLabel.Bounds, sort.Bounds, viewport, scroll, previous, auto, next, counter,
                handbook.Bounds })
            {
                Assert.True(bounds.absX >= panel.absX + 16 * scale - .001);
                Assert.True(bounds.absX + bounds.OuterWidth <= panel.absX + panel.InnerWidth - 16 * scale + .001);
                Assert.True(bounds.absY + bounds.OuterHeight <= panel.absY + panel.InnerHeight - 16 * scale + .001);
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
                Assert.True(CairoFont.WhiteSmallText().WithFontSize(12).GetTextExtents(button.Text).Width
                    + 16 * scale <= button.Bounds.OuterWidth + .001);
            }
            var textUtil = new TextDrawUtil();
            foreach (string key in new[] { "empty", "empty-search", "empty-craftable", "empty-craftable-search" })
                Assert.True(textUtil.GetMultilineTextHeight(CairoFont.WhiteSmallText().WithFontSize(14),
                    Lang.Get("vanillaexpanded:recipebook-" + key), empty.InnerWidth)
                    + 8 * scale + auto.OuterHeight <= viewport.InnerHeight + .001);
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
            Assert.True(menu.SingleComposer.Bounds.fixedWidth >= 16 + GuiElementIconTabs.MeasureWidth(tabs.tabs.Length));
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
            Assert.True(heading.Bounds.absY > tabs.Bounds.absY + tabs.Bounds.OuterHeight);
            Assert.True(heading.Bounds.absY + heading.Bounds.OuterHeight <
                menu.SingleComposer.GetSwitch("craftable-only").Bounds.absY);
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void BaselineShellAdoptsSharedControlsAndCentersTheRealMultiDigitCounter(float scale)
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
        Assert.Equal(340, composer.Bounds.fixedWidth);
        Assert.Equal(420, composer.Bounds.fixedHeight);
        Assert.IsType<GuiElementRecipeBookBackground>(composer.GetElement("dialog-background"));
        var title = Assert.IsType<GuiElementRecipeBookTitleBar>(composer.GetTitleBar("titlebar"));
        Assert.Equal(FontWeight.Bold, title.Font.FontWeight);
        Assert.Equal(14, title.Font.UnscaledFontsize);
        var input = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search"));
        Assert.Equal(0, input.TabIndex);
        Assert.Equal(14, input.Font.UnscaledFontsize);
        Assert.Equal(28 * scale, input.FieldBounds.OuterHeight, 4);
        Assert.Equal(input.FieldBounds.absX + 8 * scale, input.Bounds.absX + 3, 4);
        Assert.IsType<GuiElementStyledDropDown>(composer.GetDropDown("sort"));
        Assert.Equal("availability", composer.GetDropDown("sort").SelectedValue);
        Assert.IsType<GuiElementStyledCheckbox>(composer.GetSwitch("craftable-only"));
        var options = Assert.IsType<GuiElementRecipeBookOptions>(composer.GetElement("options"));
        Assert.IsType<GuiElementStyledCheckbox>(options.Checkbox);
        Assert.Equal(options.Bounds.absX, composer.GetHoverText("options-hint").Bounds.absX, 4);
        Assert.Equal(options.Bounds.absY, composer.GetHoverText("options-hint").Bounds.absY, 4);
        foreach (string key in new[] { "previous", "auto", "next", "handbook", "clear-search", "show-uncraftable" })
        {
            var button = Assert.IsType<GuiElementStyledButton>(composer.GetButton(key));
            Assert.Equal(8 * scale, button.Bounds.absPaddingX, 4);
        }
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        var row = Assert.Single(session.Rows);
        Assert.Equal(12, row.Output.Alternatives.Length);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Output.Alternatives[^1].Id);
        var counter = composer.GetDynamicText("alternative");
        Assert.Equal("12/12", counter.GetText());
        Assert.Equal(12, counter.Font.UnscaledFontsize);
        Assert.Equal(FontWeight.Normal, counter.Font.FontWeight);
        Assert.Equal(input.FieldBounds.absX + input.FieldBounds.OuterWidth / 2,
            counter.Bounds.absX + counter.Bounds.OuterWidth / 2, 4);
        Assert.True(counter.Font.GetTextExtents(counter.GetText()).Width <= counter.Bounds.InnerWidth);
        Assert.True(composer.GetButton("next").Bounds.absX + composer.GetButton("next").Bounds.OuterWidth
            + 4 * scale <= counter.Bounds.absX);
        Assert.True(counter.Bounds.absX + counter.Bounds.OuterWidth + 4 * scale
            <= composer.GetButton("handbook").Bounds.absX);
        var opened = f.OpenOptions(book);
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        opened.RenderInteractiveElements(0);
        var checkboxDraws = render.Invocations.Where(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Count == 7 && (float)call.Arguments[5] == 311).ToArray();
        Assert.Equal(2, checkboxDraws.Length);
        Assert.All(checkboxDraws, call => Assert.Equal(17 * (double)scale, (double)call.Arguments[3], 4));
        using var image = new ImageSurface(Format.Argb32, (int)(340 * scale), (int)(420 * scale));
        using var context = new Context(image);
        using var background = new GuiElementRecipeBookBackground(f.Lifecycle.Api.Object,
            ElementBounds.Fixed(0, 0, 340, 420).WithEmptyParent());
        background.ComposeElements(context, image);
        image.Flush();
        Assert.Equal(0x817159, Pixel(0, 20 * scale));
        Assert.InRange(Pixel(10 * scale, 2 * scale) >> 16, 73, 75);
        Assert.InRange(Pixel(10 * scale, 418 * scale) >> 16, 59, 61);
        using var bar = new GuiElementRecipeBookTitleBar(f.Lifecycle.Api.Object, "Recipe Book", composer,
            () => { }, ElementBounds.Fixed(0, 0, 340, 36).WithEmptyParent());
        bar.ComposeTextElements(context, image);
        image.Flush();
        int stroke = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
        Assert.Equal(0x2a221a, Pixel(10 * scale, 36 * scale - stroke));
        Assert.InRange(Pixel(10 * scale, 2 * scale) >> 16, 98, 101);
        Assert.InRange(Pixel(10 * scale, 34 * scale - stroke) >> 16, 74, 79);
        using var list = new GuiElementCustomDraw(f.Lifecycle.Api.Object,
            ElementBounds.Fixed(0, 50, 100, 80).WithEmptyParent(), GuiElementRecipeBookBackground.DrawList);
        list.ComposeElements(context, image);
        image.Flush();
        Assert.Equal(0x2c251c, Pixel(0, 60 * scale));
        Assert.Equal(0x352d22, Pixel(5 * scale, 60 * scale));

        int Pixel(double x, double y)
        {
            int p = (int)y * image.Stride + (int)x * 4;
            var pixels = image.Data;
            return pixels[p + 2] << 16 | pixels[p + 1] << 8 | pixels[p];
        }
    }
}
