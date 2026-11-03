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

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookDialogNativeInputTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NativeMovementMenuAllowsDraggingAndFixedResumesInventoryFollowing(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var composer = book.SingleComposer;
        var parent = f.Parent.Composers["maininventory"].Bounds;
        Assert.False(composer.GetTitleBar("titlebar").Movable);
        Assert.Empty(f.DialogPositions);
        Assert.Equal(2, composer.Bounds.absX - parent.absX - parent.OuterWidth, 4);
        double dockedX = composer.Bounds.absX;
        parent.fixedX += 20;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        book.OnRenderGUI(0);
        Assert.Equal(dockedX + 20 * scale, composer.Bounds.absX, 4);
        f.SelectMovement(book, movable: true);
        book.OnRenderGUI(0);
        var titlebar = composer.GetTitleBar("titlebar");
        Assert.True(titlebar.Movable);
        double x = composer.Bounds.absX;
        double y = composer.Bounds.absY;
        int startX = (int)(titlebar.Bounds.absX + GuiElement.scaled(30));
        int startY = (int)(titlebar.Bounds.absY + GuiElement.scaled(12));
        var down = new MouseEvent(startX, startY, EnumMouseButton.Left, 0);
        book.OnMouseDown(down);
        Assert.True(down.Handled);
        int deltaX = (int)(40 * scale);
        int deltaY = (int)(30 * scale);
        book.OnMouseMove(new MouseEvent(startX + deltaX, startY + deltaY, EnumMouseButton.None, 0));
        Assert.Equal(x + deltaX, composer.Bounds.absX, 4);
        Assert.Equal(y + deltaY, composer.Bounds.absY, 4);
        book.OnMouseUp(new MouseEvent(startX + deltaX, startY + deltaY, EnumMouseButton.Left, 0));
        book.OnMouseMove(new MouseEvent(startX + deltaX + 10, startY + deltaY + 10, EnumMouseButton.None, 0));
        parent.fixedX += 30;
        parent.fixedY += 20;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        book.OnRenderGUI(0);
        Assert.Same(composer, book.SingleComposer);
        Assert.Equal(x + deltaX, composer.Bounds.absX, 4);
        Assert.Equal(y + deltaY, composer.Bounds.absY, 4);
        Assert.Equal((int)((x + deltaX) / scale), f.DialogPositions["vanillaexpanded-recipebook"].X);
        Assert.Equal((int)((y + deltaY) / scale), f.DialogPositions["vanillaexpanded-recipebook"].Y);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        book = Assert.Single(f.Menus);
        composer = book.SingleComposer;
        titlebar = composer.GetTitleBar("titlebar");
        Assert.True(titlebar.Movable);
        Assert.Equal(x + deltaX, composer.Bounds.absX, 4);
        Assert.Equal(y + deltaY, composer.Bounds.absY, 4);
        f.SelectMovement(book, movable: false);
        Assert.False(titlebar.Movable);
        Assert.DoesNotContain("vanillaexpanded-recipebook", f.DialogPositions.Keys);
        book.OnRenderGUI(0);
        var expected = RecipeBookLayout.Place(parent,
            f.Parent.Composers["maininventory"].GetElement("craftinggrid").Bounds,
            f.Parent.Composers["maininventory"].GetElement("outputslot").Bounds,
            f.Lifecycle.Api.Object.Gui.WindowBounds, 1, 1)!;
        expected.CalcWorldBounds();
        Assert.Equal(expected.absX, book.SingleComposer.Bounds.absX, 4);
        Assert.Equal(expected.absY, book.SingleComposer.Bounds.absY, 4);
        f.Parent.TryClose();
        f.Parent.TryOpen();
        book = Assert.Single(f.Menus);
        Assert.False(book.SingleComposer.GetTitleBar("titlebar").Movable);
        Assert.DoesNotContain("vanillaexpanded-recipebook", f.DialogPositions.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MovablePositionSurvivesReopeningAndScaleChangesWithoutNeedingDockSpace(float scale)
    {
        var positions = new Dictionary<string, Vec2i>
        {
            ["vanillaexpanded-recipebook"] = new(50, 60)
        };
        using var f = new MenuFixture(scale: scale, savedPositions: positions);
        f.ResizePanelSpace(0, 700);
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        AssertPosition(book, 50, 60);
        var original = book.SingleComposer;
        book.SingleComposer.GetTextInput("search").SetValue("retained");
        RuntimeEnv.GUIScale = scale + 0.25f;
        book.TryOpen(false);
        Assert.NotSame(original, book.SingleComposer);
        AssertPosition(book, 50, 60);
        Assert.Equal("retained", book.SingleComposer.GetTextInput("search").GetText());
        f.Parent.TryClose();
        f.Parent.TryOpen();
        book = Assert.Single(f.Menus);
        AssertPosition(book, 50, 60);
        f.ResizePanelSpace(0, 350);
        book.TryOpen(false);
        Assert.True(book.IsOpened());
        Assert.True(book.SingleComposer.GetTitleBar("titlebar").Movable);
        Assert.True(book.SingleComposer.Bounds.absY + book.SingleComposer.Bounds.OuterHeight
            <= f.Lifecycle.Api.Object.Gui.WindowBounds.InnerHeight + .001);
        f.Parent.TryClose();
        Assert.Empty(f.Menus);

        static void AssertPosition(GuiDialogRecipeBook book, double x, double y)
        {
            Assert.True(book.SingleComposer.GetTitleBar("titlebar").Movable);
            Assert.Equal(x * GuiElement.scaled(1), book.SingleComposer.Bounds.absX, 4);
            Assert.Equal(y * GuiElement.scaled(1), book.SingleComposer.Bounds.absY, 4);
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void ScrollbarArrowsAndDragKeepNativeThumbAndRecipeViewportSynchronized(float scale)
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
        var scrollbar = Assert.IsType<GuiElementStyledScrollbar>(book.SingleComposer.GetScrollbar("scroll"));
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        int x = (int)(scrollbar.Bounds.absX + scrollbar.Bounds.OuterWidth / 2);
        int bottomY = (int)(scrollbar.Bounds.absY + scrollbar.Bounds.OuterHeight - 5 * scale);
        Click(bottomY);
        Assert.Equal(50, tiles.ScrollPosition, 3);
        Assert.Equal(50 * scale, scrollbar.CurrentYPosition, 3);
        int topY = (int)(scrollbar.Bounds.absY + 5 * scale);
        Click(topY);
        Assert.Equal(0, tiles.ScrollPosition);
        Assert.Equal(0, scrollbar.CurrentYPosition);
        Assert.True(scrollbar.HasFocus);
        double maximum = 2 + Math.Ceiling(101d / tiles.Geometry.Columns) * 50 - 5
            - tiles.InsideClipBounds.InnerHeight / scale;
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        scrollbar.RenderInteractiveElements(0);
        var draws = render.Invocations.Where(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)).ToArray();
        Assert.Equal(4, draws.Length);
        var upArrow = draws[1].Arguments;
        var downArrow = draws[2].Arguments;
        var thumb = draws[3].Arguments;
        double trackTop = Convert.ToDouble(upArrow[2]) + Convert.ToDouble(upArrow[4]);
        double trackBottom = Convert.ToDouble(downArrow[2]);
        double thumbTop = Convert.ToDouble(thumb[2]);
        double thumbHeight = Convert.ToDouble(thumb[4]);
        double availableTravel = trackBottom - trackTop - thumbHeight;
        Assert.True(availableTravel > 4);
        int startY = (int)(thumbTop + thumbHeight / 2);
        int dragPixels = Math.Max(1, (int)Math.Floor(availableTravel / 4));
        double expectedPartial = maximum * dragPixels / availableTravel;
        book.OnMouseDown(new MouseEvent(x, startY, EnumMouseButton.Left, 0));
        Assert.True(scrollbar.mouseDownOnScrollbarHandle);
        book.OnMouseMove(new MouseEvent(x, startY + dragPixels, EnumMouseButton.Left, 0));
        Assert.Equal(expectedPartial, tiles.ScrollPosition, 3);
        Assert.Equal(expectedPartial * scale, scrollbar.CurrentYPosition, 3);
        int endpointY = startY + (int)availableTravel;
        Assert.True(endpointY < trackBottom);
        book.OnMouseMove(new MouseEvent(x, endpointY, EnumMouseButton.Left, 0));
        Assert.Equal(maximum, tiles.ScrollPosition, 3);
        Assert.Equal(tiles.ScrollPosition * scale, scrollbar.CurrentYPosition, 3);
        book.OnMouseUp(new MouseEvent(-100, -100, EnumMouseButton.Left, 0));
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
        book.OnMouseMove(new MouseEvent(x, -100, EnumMouseButton.Left, 0));
        Assert.Equal(maximum, tiles.ScrollPosition, 3);

        void Click(int y)
        {
            var args = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            book.OnMouseDown(args);
            Assert.True(args.Handled);
            book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void InlineSearchClearUsesNativeEditingAndPreservesOtherCriteriaAcrossResizeAndReopen(float scale)
    {
        // Arrange a native dialog with a selected recipe and a non-default category.
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        Assert.Single(f.Lifecycle.Recipes).Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));
        Assert.False(input.ClearVisible);
        AssertIcon(false);
        var tabs = GetCategoryTabs(menu.SingleComposer);
        tabs.SetValue(Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);

        // Act: enter a no-match query and clear it through the native clear control.
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

        // Assert clearing restores search focus without resetting category, pin, or evaluation.
        Assert.Equal("", input.GetText());
        Assert.False(input.ClearVisible);
        Assert.True(input.HasFocus);
        Assert.True(menu.CaptureAllInputs());
        Assert.Equal("tools", session.Category);
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(row.Allocation, Assert.Single(session.Rows).Allocation);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        AssertIcon(false);

        // Act: repeat clearing with craftable-only enabled, including whitespace-only input.
        f.ToggleCraftable(menu);
        input.SetValue("no matching recipe");
        ClickClear();
        Assert.True(session.CraftableOnly.Enabled);
        Assert.True(menu.SingleComposer.GetSwitch("craftable-only").On);
        Assert.Equal(RecipeBookEmptyState.NoCraftableRecipes, session.EmptyState);
        input.SetValue(" \t ");
        Assert.True(input.ClearVisible);
        ClickClear();
        Assert.Equal("", input.GetText());
        input.SetValue("output");

        // Act: resize and reopen the same dialog composer while retaining the active query.
        for (int i = 0; i < 3; i++)
        {
            int textures = f.TextureCount;
            var composer = menu.SingleComposer;
            f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 5);
            menu.TryOpen();
            input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));
            Assert.NotSame(composer, menu.SingleComposer);
            Assert.InRange(f.TextureCount, 1, textures);
            Assert.Equal("output", input.GetText());
            Assert.True(input.ClearVisible);
            AssertIcon(true);
            // A smaller viewport can remove pooled tiles; compare resource counts again at the original size.
            f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight);
            menu.TryOpen();
            input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));
            Assert.Equal(textures, f.TextureCount);
            Assert.Equal("output", input.GetText());
            Assert.True(input.ClearVisible);
            AssertIcon(true);
        }

        // Act: close and reopen the parent inventory, creating a new dialog from browsing state.
        f.Parent.TryClose();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));

        // Assert the reopened dialog retained the query and still clears it through native input.
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
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NativeCraftableCheckboxPreservesIndependentCriteriaAcrossInputResizeAndSessionReset(float scale)
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var toggle = menu.SingleComposer.GetSwitch("craftable-only");
        Assert.False(toggle.On);
        Assert.False(session.CraftableOnly.Enabled);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        var tabs = GetCategoryTabs(menu.SingleComposer);
        tabs.SetValue(Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));
        Assert.Equal(41, session.Rows.Length);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(50 * scale);
        Assert.True(menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);
        f.ToggleCraftable(menu, label: true);
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
        foreach (bool enabled in new[] { false, true })
        {
            var space = new KeyEvent { KeyCode = (int)GlKeys.Space };
            menu.OnKeyDown(space);
            Assert.True(space.Handled);
            Assert.Equal(enabled, toggle.On);
            Assert.Equal(enabled, session.CraftableOnly.Enabled);
            AssertCriteria();
        }
        for (int i = 0; i < 3; i++)
        {
            var composer = menu.SingleComposer;
            f.ResizePanelSpace(composer.Bounds.fixedWidth + 20, composer.Bounds.fixedHeight - 10);
            menu.TryOpen();
            Assert.NotSame(composer, menu.SingleComposer);
            Assert.True(menu.SingleComposer.GetSwitch("craftable-only").On);
            Assert.True(session.CraftableOnly.Enabled);
            Assert.Single(session.Rows);
            AssertCriteria();
            f.ToggleCraftable(menu, label: true);
            Assert.False(session.CraftableOnly.Enabled);
            Assert.Equal(41, session.Rows.Length);
            f.ToggleCraftable(menu, label: true);
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
            var newSession = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
            Assert.NotSame(session, newSession);
            Assert.False(newSession.CraftableOnly.Enabled);
            Assert.False(reopened.SingleComposer.GetSwitch("craftable-only").On);
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
    public void BookButtonUsesSvgAndNativeMouseAndKeyboardActivation(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        var button = f.ToggleButton;
        Assert.Empty(button.GetText());
        Assert.Equal("vanillaexpanded-recipe-book", button.icon);
        Assert.True(button.Toggleable);
        Assert.Equal(28 * scale, button.Bounds.InnerWidth, 4);
        Assert.Equal(button.Bounds.InnerWidth, button.Bounds.InnerHeight, 4);
        AssertBookTabPosition(f);
        Assert.Equal("Recipe Book", button.Hint.GetText());
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
        f.AssertToggleOpen();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.Toggle();
        Assert.Empty(f.Menus);
        Assert.True(button.Visible);
        Assert.False(button.On);
        button.OnFocusGained();
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        button.OnKeyDown(f.Lifecycle.Api.Object, enter);
        Assert.True(enter.Handled);
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.AssertToggleOpen();
        button.OnFocusGained();
        var close = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        button.OnKeyDown(f.Lifecycle.Api.Object, close);
        Assert.True(close.Handled);
        Assert.Empty(f.Menus);
        Assert.True(button.Visible);
        Assert.False(button.On);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void CategoryIconsComposeViaNativeSvgApiAndRegistrationAndTexturesFollowMenuLifetime(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        string[] codes = ["construction", "decorative", "mechanics", "terrain", "tools", "clothing", "items", "mod:custom"];
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = codes;
        f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other"));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        int baseline = f.TextureCount;
        f.CategoryDraws.Clear();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var tabs = GetCategoryTabs(menu.SingleComposer);
        Assert.Equal(10, tabs.tabs.Length);
        Assert.Equal(30, f.CategoryDraws.Count);
        Assert.All(RecipeBookCategoryIcons.Codes, code =>
            Assert.Equal(3, f.CategoryDraws.Count(draw => ReferenceEquals(draw.Asset, f.CategoryAssets[code]))));
        Assert.All(f.CategoryDraws, draw =>
        {
            Assert.InRange(draw.Width, (int)(18 * scale), (int)(22 * scale));
            Assert.Equal(draw.Width, draw.Height);
            Assert.True(draw.X >= 0 && draw.Y >= 0);
            Assert.True(draw.Y + draw.Height <= draw.SurfaceHeight);
            Assert.True(draw.SurfaceWidth >= draw.X + draw.Width);
            Assert.NotNull(draw.Color);
        });
        var registry = f.Lifecycle.Api.Object.Gui.Icons.CustomIcons;
        var registered = RecipeBookCategoryIcons.Codes.ToDictionary(RecipeBookCategoryIcons.Id, code =>
            registry[RecipeBookCategoryIcons.Id(code)]);
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
        f.Parent.TryOpen();
        Assert.Single(f.Menus);
        Assert.All(registered, icon => Assert.Same(icon.Value, registry[icon.Key]));
        string replaced = RecipeBookCategoryIcons.Id("tools");
        IconRendererDelegate replacement = (_, _, _, _, _, _) => { };
        registry[replaced] = replacement;
        f.Lifecycle.System.Dispose();
        Assert.Empty(f.Menus);
        Assert.DoesNotContain(registered.Keys.Where(key => key != replaced), registry.ContainsKey);
        Assert.Same(replacement, registry[replaced]);
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
        for (int i = 0; i < 80; i++)
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tabs = GetCategoryTabs(menu.SingleComposer);
        Assert.Equal("All", tabs.tabs[0].Name);
        Assert.Equal("Other", tabs.tabs[^1].Name);
        Assert.DoesNotContain(tabs.tabs, tab => tab.Name is "general" or "flora");
        Assert.Equal(82, session.Rows.Length);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(50 * scale);
        Assert.True(menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);
        var pressed = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        int pressX = (int)pressed.Bounds.absX + 10;
        int pressY = (int)pressed.Bounds.absY + 10;
        tiles.OnMouseDown(f.Lifecycle.Api.Object, new MouseEvent(pressX, pressY, EnumMouseButton.Left, 0));
        Assert.True(pressed.Pressed);
        menu.SingleComposer.GetTextInput("search").SetValue(" output ");
        Assert.False(pressed.Pressed);
        Assert.Equal(82, session.Rows.Length);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        menu.SingleComposer.GetTextInput("search").SetValue("output");
        tiles.Scroll(50 * scale);
        Assert.True(menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);

        int toolIndex = Array.FindIndex(tabs.tabs, tab => tab.Name == "tools");
        tabs.SetValue(toolIndex);
        Assert.Equal("tools", session.Category);
        Assert.Single(session.Rows);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        Assert.NotNull(Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]).Selection);
        Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());

        var previousComposer = menu.SingleComposer;
        f.ResizePanelSpace(previousComposer.Bounds.fixedWidth + 50, previousComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(previousComposer, menu.SingleComposer);
        tabs = GetCategoryTabs(menu.SingleComposer);
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
        Assert.Equal(80, session.Rows.Length);
        tabs.SetValue(0, false);
        var firstTab = tabs.Composer!.GetToggleButton("tab-0");
        int x = (int)(firstTab.Bounds.absX + firstTab.Bounds.OuterWidth / 2);
        int y = (int)(firstTab.Bounds.absY + firstTab.Bounds.OuterHeight / 2);
        f.SetMouse(x, y);
        Assert.True(firstTab.IsPositionInside(x, y));
        tabs.OnMouseDownOnElement(f.Lifecycle.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.Null(session.Category);
        Assert.Equal(82, session.Rows.Length);
        Assert.Equal("output", menu.SingleComposer.GetTextInput("search").GetText());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void NativeSortSelectorUsesMouseOrKeyboardAndResetsScrollWithoutLeakingTextures(float scale, bool keyboard)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var available = Assert.Single(f.Lifecycle.Recipes);
        available.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "ZZZZ");
        for (int i = 0; i < 40; i++)
        {
            var recipe = f.Lifecycle.F.Recipe(f.Lifecycle.F.Ingredient(
                f.Lifecycle.F.Stack(i + 2, "ingredient-" + i, 1)));
            recipe.Output!.ResolvedItemStack!.Attributes.SetString("displayName", $"Recipe {i:D2}");
            f.Lifecycle.Recipes.Add(recipe);
        }
        var inventory = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        inventory[0].Itemstack = available.ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        int baseline = f.TextureCount;
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        Assert.Equal("ZZZZ", session.Rows[0].Recipe.DisplayName);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        tiles.Scroll(50 * scale);
        Assert.Equal(50, tiles.ScrollPosition);
        var pressed = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        pressed.OnMouseDownOnElement(f.Lifecycle.Api.Object, new MouseEvent(
            (int)(pressed.Bounds.absX + 5), (int)(pressed.Bounds.absY + 5), EnumMouseButton.Left, 0));
        Assert.True(pressed.Pressed);
        var sort = menu.SingleComposer.GetDropDown("sort");
        int shown = f.TextureCount;
        if (keyboard)
        {
            menu.SingleComposer.FocusElement(sort.TabIndex);
            var args = new KeyEvent { KeyCode = (int)GlKeys.End };
            menu.OnKeyDown(args);
            Assert.True(args.Handled);
        }
        else
        {
            int x = (int)(sort.Bounds.absX + 5);
            int y = (int)(sort.Bounds.absY + 5);
            f.SetMouse(x, y);
            menu.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            Assert.True(sort.listMenu.IsOpened);
            y = (int)(sort.listMenu.Bounds.renderY + sort.listMenu.Bounds.InnerHeight + 45 * scale);
            f.SetMouse(x, y);
            menu.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            Assert.False(sort.listMenu.IsOpened);
        }
        Assert.Equal("name", sort.SelectedValue);
        Assert.Equal("name", session.Sort.Selected.Id);
        Assert.Equal("Recipe 00", session.Rows[0].Recipe.DisplayName);
        Assert.Equal("ZZZZ", session.Rows[^1].Recipe.DisplayName);
        Assert.False(pressed.Pressed);
        Assert.Equal(0, tiles.ScrollPosition);
        Assert.Equal(0, menu.SingleComposer.GetScrollbar("scroll").CurrentYPosition);
        Assert.Equal(shown, f.TextureCount);
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
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
            f.AssertToggleOpen();
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
            f.AssertToggleOpen();
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

        static void AssertTabOrder(GuiDialogRecipeBook menu)
        {
            var composer = menu.SingleComposer;
            var search = composer.GetTextInput("search");
            var categories = GetCategoryTabs(composer);
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
            Assert.Same(composer.GetSwitch("craftable-only"), composer.CurrentTabIndexElement);
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

    [Theory]
    [InlineData((int)GlKeys.Period, '.', true, false, false)]
    [InlineData((int)GlKeys.X, 'x', true, false, false)]
    [InlineData((int)GlKeys.Period, '.', false, false, false)]
    [InlineData((int)GlKeys.X, 'x', false, false, false)]
    [InlineData((int)GlKeys.X, 'x', true, true, false)]
    [InlineData((int)GlKeys.X, 'x', false, true, false)]
    [InlineData((int)GlKeys.X, 'x', true, false, true)]
    [InlineData((int)GlKeys.X, 'x', false, false, true)]
    public void NativeIntegerInputRejectsNonintegerEditsAndRestoresTheLastAcceptedSize(
        int key, char character, bool commitOnEnter, bool recomposeGeometry, bool recomposeScale)
    {
        // Arrange
        var savedSettings = new Dictionary<string, int> { [RecipeBookSystem.TileSizeSetting] = 57 };
        using var f = new MenuFixture(savedIntSettings: savedSettings);
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = f.OpenOptions(book);
        FocusSizeInput(f, book, options);
        options.SizeInput.SetValue("63");
        var accepted = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        book.OnKeyDown(accepted);
        Assert.True(accepted.Handled);
        Assert.Equal(63, savedSettings[RecipeBookSystem.TileSizeSetting]);

        // A real keypress reaches the native IntMode input; SetValue would bypass its rejected-edit path.
        // Act
        var invalidEdit = new KeyEvent { KeyCode = key, KeyChar = character };
        book.OnKeyPress(invalidEdit);
        Assert.True(invalidEdit.Handled);
        Assert.True(options.RejectedSizeEdit);
        Assert.Equal(63, savedSettings[RecipeBookSystem.TileSizeSetting]);

        // The changed tile size has not reached layout yet; this render must recompose before the
        // rejected edit is committed. Optional second recompositions cover ordinary layout changes.
        var previousComposer = book.SingleComposer;
        book.OnRenderGUI(0);
        Assert.NotSame(previousComposer, book.SingleComposer);
        if (recomposeGeometry)
        {
            previousComposer = book.SingleComposer;
            f.ResizePanelSpace(previousComposer.Bounds.fixedWidth + 50, previousComposer.Bounds.fixedHeight - 20);
            book.TryOpen();
            Assert.NotSame(previousComposer, book.SingleComposer);
        }
        else if (recomposeScale)
        {
            previousComposer = book.SingleComposer;
            RuntimeEnv.GUIScale += 0.25f;
            book.TryOpen(false);
            Assert.NotSame(previousComposer, book.SingleComposer);
        }

        options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        Assert.True(options.IsOpened);
        Assert.True(options.SizeInput.HasFocus);

        if (commitOnEnter)
        {
            var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
            book.OnKeyDown(enter);
            Assert.True(enter.Handled);
            Assert.True(options.IsOpened);
            Assert.True(options.SizeInput.HasFocus);
        }
        else
        {
            var search = book.SingleComposer.GetTextInput("search");
            int x = (int)(search.Bounds.absX + search.Bounds.OuterWidth / 2);
            int y = (int)(search.Bounds.absY + search.Bounds.OuterHeight / 2);
            f.SetMouse(x, y);
            book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            Assert.False(options.IsOpened);
            Assert.True(search.HasFocus);
        }

        // Assert
        Assert.Equal("63", options.SizeInput.GetText());
        Assert.Equal(63, savedSettings[RecipeBookSystem.TileSizeSetting]);
        f.Lifecycle.Api.Verify(api => api.TriggerIngameError(It.IsAny<object>(),
            "recipebook-tile-size", "Localized invalid tile-size message."), Times.Once);
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

    [Theory]
    [InlineData(GlKeys.E, 'e')]
    [InlineData(GlKeys.I, 'i')]
    public void InventoryHotkeyRestoresAnUnfocusedBookAndItsTextEventDoesNotChangeSearch(GlKeys key, char character)
    {
        using var f = new MenuFixture();
        f.Parent.Opening = f.Recompose;
        var hotkey = new HotKey { CurrentMapping = new KeyCombination { KeyCode = (int)key } };
        Mock.Get(f.Lifecycle.Api.Object.Input).Setup(value => value.GetHotKeyByCode("inventory")).Returns(hotkey);
        f.Parent.TryOpen();
        f.Toggle();
        var initial = Assert.Single(f.Menus);
        initial.SingleComposer.GetTextInput("search").SetValue("saved query");
        f.Parent.TryClose();

        typeof(GuiDialog).GetMethod("OnKeyCombinationToggle", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Parent, [hotkey.CurrentMapping]);
        var restored = Assert.Single(f.Menus);
        Assert.True(f.Parent.Focused);
        Assert.False(restored.Focused);
        Assert.False(restored.CaptureAllInputs());
        Assert.False(restored.SingleComposer.GetTextInput("search").HasFocus);
        DispatchText(new KeyEvent { KeyCode = (int)key, KeyChar = character });
        Assert.Equal("saved query", restored.SingleComposer.GetTextInput("search").GetText());

        var composer = restored.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth + 40, composer.Bounds.fixedHeight - 20);
        restored.OnRenderGUI(0);
        Assert.NotSame(composer, restored.SingleComposer);
        Assert.True(f.Parent.Focused);
        Assert.False(restored.CaptureAllInputs());
        Assert.False(restored.SingleComposer.GetTextInput("search").HasFocus);

        var search = restored.SingleComposer.GetTextInput("search");
        restored.SingleComposer.FocusElement(search.TabIndex);
        Assert.True(restored.Focused);
        var typed = new KeyEvent { KeyCode = (int)key, KeyChar = character };
        DispatchText(typed);
        Assert.True(typed.Handled);
        Assert.Equal("saved query" + character, search.GetText());
        restored.UnFocus();
        Assert.False(restored.CaptureAllInputs());

        void DispatchText(KeyEvent args)
        {
            foreach (var dialog in f.Lifecycle.Api.Object.Gui.LoadedGuis.ToArray())
            {
                if (!dialog.ShouldReceiveKeyboardEvents()) continue;
                dialog.OnKeyPress(args);
                if (args.Handled) break;
            }
        }
    }

    [Fact]
    public void ExplicitBookOpeningFocusesSearchAndAcceptsTheFirstCharacter()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        Assert.True(book.Focused);
        Assert.True(book.SingleComposer.GetTextInput("search").HasFocus);
        var typed = new KeyEvent { KeyCode = (int)GlKeys.E, KeyChar = 'e' };
        book.OnKeyPress(typed);
        Assert.True(typed.Handled);
        Assert.Equal("e", book.SingleComposer.GetTextInput("search").GetText());
    }

    private static void FocusSizeInput(MenuFixture f, GuiDialogRecipeBook book, GuiElementRecipeBookOptions options)
    {
        Mock.Get(f.Lifecycle.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        var bounds = options.SizeInput.Bounds;
        int x = (int)(bounds.absX + bounds.OuterWidth / 2);
        int y = (int)(bounds.absY + bounds.OuterHeight / 2);
        f.SetMouse(x, y);
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        book.OnMouseDown(down);
        Assert.True(down.Handled);
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.True(options.SizeInput.HasFocus);
    }
}
