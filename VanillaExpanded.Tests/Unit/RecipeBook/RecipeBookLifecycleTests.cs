using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookLifecycleTests
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
            Assert.Equal(viewport.absY, tiles.Elements[0].Bounds.absY, 4);
            Assert.All(tiles.Elements, tile => Assert.Same(viewport, tile.InsideClipBounds));
            foreach (float position in new[] { 45f, 48f, 49f, 96f })
            {
                tiles.Scroll(position * scale);
                Assert.Equal(position, tiles.ScrollPosition);
                var first = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
                Assert.NotNull(first.Selection);
                Assert.Equal(viewport.absY + Math.Round(-position % 35 * scale, MidpointRounding.AwayFromZero),
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
        original.Scroll(112 * scale);
        if (keyboard) original.OnKeyDown(f.Lifecycle.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Tab });
        book.SingleComposer.FocusElement(original.TabIndex);
        original.Scroll(112 * scale);
        original.FocusElement(original.Geometry.Columns + 2);
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
        Assert.Equal(anchorIndex / resized.Geometry.Columns * 35 + before.RowOffset, resized.ScrollPosition, 3);
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
        original.Scroll(112 * oldScale);
        var first = Assert.IsType<GuiElementRecipeBookTile>(original.Elements[0]);
        original.UpdateHover((int)first.PixelBounds.renderX + 5, (int)original.InsideClipBounds.renderY + 5);
        var before = original.CaptureView();
        Assert.Equal(7, before.RowOffset, 4);
        RuntimeEnv.GUIScale = newScale;
        Assert.Equal(112, original.ScrollPosition, 4);
        book.TryOpen();
        var resized = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        Assert.NotSame(original, resized);
        var after = resized.CaptureView();
        Assert.Equal(before.AnchorOutput, after.AnchorOutput);
        Assert.Equal(before.RowOffset, after.RowOffset, 4);
        Assert.Equal(before.InspectedOutput, after.InspectedOutput);
    }

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
        Assert.True(menu.SingleComposer.GetSwitch("craftable-only").On);
        Assert.Equal(RecipeBookEmptyState.NoCraftableRecipes, session.EmptyState);
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
        f.Parent.TryClose();
        f.Parent.TryOpen();
        menu = Assert.Single(f.Menus);
        input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));
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
        var input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));
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

    private static void AssertEmptyStateCentered(GuiDialogRecipeBook menu)
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        session.Select(Assert.Single(session.Rows).Output.Id);
        f.Lifecycle.Api.SetupGet(api => api.LinkProtocols).Returns(new Dictionary<string, Action<LinkTextComponent>>());
        f.Click(menu.SingleComposer.GetButton("handbook"));
        f.Lifecycle.Api.Verify(api => api.TriggerIngameError(menu, "recipebook",
            "vanillaexpanded:recipebook-handbook-unavailable"), Times.Once);
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
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

    private sealed class HiddenFilter : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => false;
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
        tiles.Scroll(35 * scale);
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
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = width + 16;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        int baseline = f.TextureCount;
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        var tileBounds = tiles.Elements[0].Bounds;
        tiles.UpdateHover((int)(tileBounds.absX + 5), (int)(tileBounds.absY + 5));
        var popup = tooltip.Popup!;
        int shown = f.TextureCount;
        var content = popup.GetRichtext("recipe");
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        var text = content.Components.OfType<RichTextComponent>()
            .Where(component => component is not ClearFloatTextComponent).ToArray();
        Assert.Equal(new[] { "3/4", name, "2/2", stick.GetName() }, text.Select(component => component.DisplayText));
        Assert.Equal(GuiStyle.ErrorTextColor, text[0].Font.Color);
        Assert.Equal(CairoFont.WhiteSmallText().Color, text[2].Font.Color);
        Assert.All(text, component =>
        {
            Assert.Equal(CairoFont.WhiteDetailText().UnscaledFontsize, component.Font.UnscaledFontsize);
            Assert.True(component.Font.UnscaledFontsize < CairoFont.WhiteSmallText().UnscaledFontsize);
        });
        Assert.True(text[1].Lines.Length > 1);
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var unresolved = row.Recipe.Recipe.Clone();
        unresolved.ResolvedIngredients = null;
        var entry = new CraftingRecipeEntry(1000, unresolved, row.Recipe.DisplayName);
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var entry = new CraftingRecipeEntry(1000, row.Recipe.Recipe,
            "A long localized crafting output name that must wrap across several centered title lines");
        f.Lifecycle.Api.Object.Gui.WindowBounds.fixedWidth = windowWidth / scale;
        f.Lifecycle.Api.Object.Gui.WindowBounds.CalcWorldBounds();
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var recipe = row.Recipe.Recipe.Clone();
        var item = new Mock<MockItem>(555, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        item.Object.Code = new AssetLocation("game:empty-description");
        item.Setup(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
                It.IsAny<IWorldAccessor>(), false))
            .Callback<ItemSlot, StringBuilder, IWorldAccessor, bool>((_, text, _, _) => text.Append(" \r\n"));
        recipe.Output!.ResolvedItemStack = hasOutput ? new ItemStack(item.Object) : null;
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
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
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
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

    private static void AssertBookTabPosition(MenuFixture f)
    {
        var composer = f.Parent.Composers["maininventory"];
        var button = f.ToggleButton.Bounds;
        var hint = f.ToggleButton.Hint.Bounds;
        var output = composer.GetElement("outputslot").Bounds;
        var inputs = composer.GetElement("craftinggrid").Bounds;
        Assert.Same(output, button.ParentBounds);
        Assert.Equal(output.absX - GuiElement.scaled(4), button.absX + button.OuterWidth, 4);
        Assert.Equal(output.absY + output.InnerHeight / 2, button.absY + button.InnerHeight / 2, 4);
        Assert.True(button.absY >= inputs.absY + inputs.OuterHeight);
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
        Assert.False(f.ToggleButton.On);
        f.ResizePanelSpace(320, 420);
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.AssertToggleOpen();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void RecipeBookOptionsSvgStatesUseIsolatedTexturesWithoutModifyingTheSharedDialogSurface(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        using var surface = new ImageSurface(Format.Argb32, 128, 64);
        using var ctx = new Context(surface);
        ctx.SetSourceRGB(1, 1, 1);
        ctx.Paint();
        ctx.SetSourceRGB(0, 0, 0);
        for (int x = 0; x < 128; x += 4)
        {
            ctx.Rectangle(x, 0, 2, 64);
            ctx.Fill();
        }
        surface.Flush();
        var original = surface.Data.ToArray();
        using var options = new GuiElementRecipeBookOptions(f.Lifecycle.Api.Object,
            ElementBounds.Fixed(40.3, 10.3, 17, 17).WithEmptyParent());
        var gui = Mock.Get(f.Lifecycle.Api.Object.Gui);
        gui.Invocations.Clear();
        options.ComposeElements(ctx, surface);
        surface.Flush();
        Assert.Equal(original, surface.Data);
        Assert.Equal(2, f.OptionsDraws.Count);
        int iconSize = (int)Math.Round(GuiElement.scaled(17), MidpointRounding.AwayFromZero);
        Assert.All(f.OptionsDraws, draw =>
        {
            Assert.Equal(iconSize, draw.Width);
            Assert.Equal(iconSize, draw.Height);
            Assert.Equal(iconSize, draw.SurfaceWidth);
            Assert.Equal(iconSize, draw.SurfaceHeight);
        });
        Assert.NotEqual(f.OptionsDraws[0].Color, f.OptionsDraws[1].Color);
        Assert.Equal(6, gui.Invocations.Count(call =>
            call.Method.Name == nameof(IGuiAPI.LoadOrUpdateCairoTexture) && !(bool)call.Arguments[1]));
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        foreach (bool hovered in new[] { false, true })
        {
            f.SetMouse(hovered ? (int)(options.Bounds.absX + 5) : 0,
                hovered ? (int)(options.Bounds.absY + 5) : 0);
            render.Invocations.Clear();
            options.RenderInteractiveElements(0);
            var draw = Assert.Single(render.Invocations, call =>
                call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
            Assert.Equal(Math.Round(options.Bounds.absX, MidpointRounding.AwayFromZero), (double)draw.Arguments[1]);
            Assert.Equal(Math.Round(options.Bounds.absY, MidpointRounding.AwayFromZero), (double)draw.Arguments[2]);
            Assert.Equal((double)iconSize, (double)draw.Arguments[3]);
            Assert.Equal((double)iconSize, (double)draw.Arguments[4]);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RecipeBookOptionsControlRestorationWithoutClearingBrowsingState(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        int baseline = f.TextureCount;
        f.Toggle();
        var book = Assert.Single(f.Menus);
        book.SingleComposer.GetTextInput("search").SetValue("retained search");
        var options = f.OpenOptions(book);
        Assert.True(options.IsOpened);
        Assert.True(options.HasFocus);
        Assert.True(book.Focused);
        Assert.True(options.Checkbox.On);
        Assert.InRange(options.PopupBounds.absX, book.SingleComposer.Bounds.absX, options.Bounds.absX);
        Assert.InRange(System.Math.Abs(options.PopupBounds.absY - options.Bounds.absY - options.Bounds.OuterHeight), 0, 1);
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        book.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.False(options.Checkbox.On);
        Assert.False(f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
        Assert.True(options.IsOpened);
        Assert.True(book.OnEscapePressed());
        Assert.True(book.Focused);

        f.Parent.TryClose();
        Assert.False(options.IsOpened);
        Assert.Equal(baseline, f.TextureCount);
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
        f.Toggle();
        book = Assert.Single(f.Menus);
        Assert.Equal("retained search", book.SingleComposer.GetTextInput("search").GetText());
        var secondOptions = f.OpenOptions(book);
        Assert.False(secondOptions.Checkbox.On);
        f.ToggleReopenOption(book);
        Assert.True(secondOptions.Checkbox.On);
        Assert.Equal("retained search", book.SingleComposer.GetTextInput("search").GetText());
        Assert.True(secondOptions.IsOpened);
        Assert.True(book.OnEscapePressed());
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
        f.Parent.TryOpen();
        Assert.Equal("retained search", Assert.Single(f.Menus).SingleComposer.GetTextInput("search").GetText());
    }

    [Fact]
    public void RecipeBookRememberOpenPreferenceSurvivesClientSessions()
    {
        var savedSettings = new Dictionary<string, bool>();
        using (var f = new MenuFixture(savedSettings: savedSettings))
        {
            f.Parent.TryOpen();
            f.Toggle();
            var book = Assert.Single(f.Menus);
            f.OpenOptions(book);
            book.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        }
        using var next = new MenuFixture(savedSettings: savedSettings);
        next.Parent.TryOpen();
        next.Toggle();
        next.Parent.TryClose();
        next.Parent.TryOpen();
        Assert.Empty(next.Menus);
        next.Toggle();
        var restoredOptions = next.OpenOptions(Assert.Single(next.Menus));
        Assert.False(restoredOptions.Checkbox.On);
    }

    [Theory]
    [InlineData((int)GlKeys.Enter)]
    [InlineData((int)GlKeys.KeypadEnter)]
    [InlineData((int)GlKeys.Space)]
    public void RecipeBookOptionsKeyboardTogglesBothStatesAndEscapeOnlyDismissesTheList(int key)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        Assert.True(book.SingleComposer.FocusElement(options.TabIndex));
        var open = new KeyEvent { KeyCode = key };
        book.OnKeyDown(open);
        Assert.True(open.Handled);
        Assert.True(options.IsOpened);
        foreach (bool enabled in new[] { false, true })
        {
            var toggle = new KeyEvent { KeyCode = key };
            book.OnKeyDown(toggle);
            Assert.True(toggle.Handled);
            Assert.Equal(enabled, options.Checkbox.On);
            Assert.Equal(enabled, f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
        }
        Assert.True(book.OnEscapePressed());
        Assert.False(options.IsOpened);
        Assert.True(options.HasFocus);
        Assert.True(book.IsOpened());
        Assert.True(f.Parent.IsOpened());
    }

    [Fact]
    public void RecipeBookOptionsOutsideClickDismissesTheListAndAllowsSearchFocus()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = f.OpenOptions(book);
        var search = book.SingleComposer.GetTextInput("search");
        search.SetValue("retained search");
        Mock.Get(f.Lifecycle.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        int x = (int)(search.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(search.Bounds.absY + GuiElement.scaled(5));
        f.SetMouse(x, y);
        book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.False(options.IsOpened);
        Assert.True(search.HasFocus);
        Assert.True(book.IsOpened());
        Assert.True(f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenRecipeBookOptionsAreDisposedWithTheirBook(bool externalDisposal)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = f.OpenOptions(book);
        if (externalDisposal) book.Dispose();
        else f.Parent.TryClose();
        Assert.False(options.IsOpened);
        Assert.Empty(f.Menus);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void OpeningReleaseDoesNotImmediatelyToggleTheRecipeBookClosed(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        f.ResizePanelSpace(150, 1080);
        int x = (int)(f.ToggleButton.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(f.ToggleButton.Bounds.absY + GuiElement.scaled(5));
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        f.Parent.OnMouseDown(down);
        Assert.True(down.Handled);
        Assert.Empty(f.Menus);
        var up = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        f.Parent.OnMouseUp(up);
        var menu = Assert.Single(f.Menus);
        menu.OnMouseUp(up);
        Assert.Same(menu, Assert.Single(f.Menus));
        Assert.True(menu.IsOpened());
        f.AssertToggleOpen();
        f.Toggle();
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        Assert.False(f.ToggleButton.On);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CancelledBookButtonPressPreservesTheOpenState(bool initiallyOpen, bool loseFocus)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        if (initiallyOpen) f.Toggle();
        var button = f.ToggleButton;
        int x = (int)(button.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(button.Bounds.absY + GuiElement.scaled(5));
        f.Parent.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        if (loseFocus) button.OnFocusLost();
        else x = (int)(button.Bounds.absX - GuiElement.scaled(5));
        f.Parent.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));

        Assert.Equal(initiallyOpen, f.Menus.Any(menu => menu.IsOpened()));
        Assert.Equal(initiallyOpen, button.On);
        f.Toggle();
        Assert.Equal(!initiallyOpen, f.Menus.Any(menu => menu.IsOpened()));
        Assert.Equal(!initiallyOpen, button.On);
    }

    [Fact]
    public void SessionFilterRegistrationsSurviveRecompositionAndAreReleasedWithTheDialog()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
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
        var newSession = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
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
        tiles.Scroll(35 * scale);
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
        tiles.Scroll(35 * scale);
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
            + 10 + 2 * border + Math.Max(GuiElementRecipeBookTiles.TileSize, emptyHeight) + 12
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
            Assert.True(viewport.InnerHeight + .001 >= GuiElementRecipeBookTiles.TileSize * scale);
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
            .Returns(new VanillaExpandedConfig { EnableRecipeBook = enabled });
        try
        {
            loadedField.SetValue(null, false);
            var system = new RecipeBookSystem();
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
        tiles.Scroll(35 * scale);
        Assert.Equal(35, tiles.ScrollPosition);
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
        RecipeBookSession Open() => new(catalogue, f.F.Allocator, () =>
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
        var composed = (Delegate?)typeof(RecipeBookDialogPatch).GetField("Composed",
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
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator, () =>
        {
            captures++;
            return CraftingInventorySnapshot.Capture(f.Player, 3, 3, RuntimeEnv.MainThreadId, true);
        }, (_, _) => throw new InvalidOperationException("No placement expected."));
        session.Refresh();
        using var graphics = new RecipeBookRenderFixture(scale);
        var render = graphics.Render;
        var liveTextures = graphics.Textures;
        var viewport = ElementBounds.Fixed(0, 0, 82, 65).WithEmptyParent();
        viewport.CalcWorldBounds();
        render.Setup(value => value.RenderMesh(It.IsAny<MeshRef>())).Callback(() =>
        {
            Assert.Equal(2, graphics.Clips.Count);
            Assert.Same(viewport, graphics.Clips.ToArray()[1]);
            var bounds = graphics.Clips.Peek();
            Assert.InRange(bounds.InnerWidth, Math.Floor(30 * scale), Math.Ceiling(30 * scale));
            Assert.InRange(bounds.InnerHeight, Math.Floor(30 * scale), Math.Ceiling(30 * scale));
            Assert.Same(graphics.Programs[^1].Object, graphics.Active);
        });
        using var surface = new ImageSurface(Format.Argb32, 96, 144);
        using var ctx = new Context(surface);
        for (int cycle = 0; cycle < 5; cycle++)
        {
            int inspected = 0;
            using var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
                ElementBounds.Fixed(0, 0, 96, 144).WithParent(viewport), viewport, session,
                _ => inspected++, (_, _) => { }, graphics.Cache, graphics.CreateShader);
            tiles.BeforeCalcBounds();
            tiles.Bounds.CalcWorldBounds();
            tiles.ComposeElements(ctx, surface);
            Assert.Single(liveTextures);
            int beforeBakes = graphics.Bakes.Count;
            for (int frame = 0; frame < 6; frame++)
            {
                render.Invocations.Clear();
                foreach (var program in graphics.Programs) program.Invocations.Clear();
                graphics.Frame(tiles);
                Assert.Equal(4, render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderMesh)));
                Assert.Equal(cycle == 0 ? (frame == 0 ? 0 : 4) : beforeBakes, graphics.Bakes.Count);
                Assert.Equal(cycle == 0 ? (frame == 0 ? 0 : 4) : 6, graphics.Allocations.Count);
                graphics.Programs[^1].Verify(value => value.Use(), Times.Once);
                Assert.Empty(graphics.Clips);
            }
            tiles.Scroll(24 * scale);
            for (int frame = 0; frame < 2; frame++)
            {
                render.Invocations.Clear();
                graphics.Frame(tiles);
                Assert.Equal(6, render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderMesh)));
                Assert.Equal(cycle == 0 ? (frame == 0 ? 4 : 6) : beforeBakes, graphics.Bakes.Count);
            }
            Assert.Equal(6, graphics.Allocations.Count);
            Assert.All(graphics.Bakes, bake =>
            {
                Assert.Equal((int)Math.Ceiling((Math.Round(30 * scale, MidpointRounding.AwayFromZero)
                    - 2 * Math.Round(2 * scale, MidpointRounding.AwayFromZero)) / 2) * 2, bake.Size);
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
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void WorldLifecycleOwnsLocallyQueuedNativePendingAndReadyIcons(int cleanup, int phase)
    {
        using var scope = new NativeScope();
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = phase == 2 };
        using var f = new LifecycleFixture();
        f.Api.SetupGet(value => value.Render).Returns(graphics.Render.Object);
        f.Api.SetupGet(value => value.ItemTextureAtlas).Returns(graphics.Api.Object.ItemTextureAtlas);
        f.FinalizeWorld();
        var cache = Assert.IsType<RecipeBookIconCache>(f.System.IconCache);
        var recipe = Assert.Single(f.System.Catalogue!.Recipes);
        Assert.Null(cache.Get(recipe, 46, true, out bool queued));
        Assert.True(queued);
        if (phase > 0)
        {
            graphics.BeforeFrame(cache);
            Assert.Single(graphics.Bakes);
            graphics.EndFrame();
        }
        Assert.Equal(phase == 2, cache.Get(recipe, 46, false, out _) != null);
        if (cleanup == 0) f.Events.Raise(value => value.LeaveWorld += null);
        else if (cleanup == 1) f.System.Dispose();
        else f.FinalizeWorld();
        if (phase == 1) graphics.Complete(graphics.Bakes[0]);
        graphics.BeforeFrame(cache);
        Assert.Equal(phase == 0 ? 0 : 1, graphics.Bakes.Count);
        Assert.Empty(graphics.Allocations);
        Assert.NotSame(cache, f.System.IconCache);
        Assert.Throws<ObjectDisposedException>(() => cache.Get(recipe, 46, true, out _));
        f.Events.VerifyRemove(value => value.ReloadTextures -= It.IsAny<Action>(), Times.Once);
        f.Events.Verify(value => value.RegisterRenderer(cache, EnumRenderStage.Before, "crafting-icon-requests"), Times.Once);
        f.Events.Verify(value => value.UnregisterRenderer(cache, EnumRenderStage.Before), Times.Once);
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
            using var dialog = new GuiDialogRecipeBook(f.Api.Object, parent, composer,
                f.System.Catalogue!, f.F.Allocator, f.System.IconCache!);
            f.Api.Object.Gui.RegisterDialog(dialog);
            int disposed = 0;
            dialog.Disposed += () => disposed++;
            var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
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

    private static GuiElementIconTabs GetCategoryTabs(GuiComposer composer) =>
        Assert.IsType<GuiElementIconTabs>(composer.GetElement("categories"));

    private sealed class MenuFixture : IDisposable
    {
        private readonly RecipeBookRenderFixture graphics;
        internal readonly LifecycleFixture Lifecycle;
        internal readonly ParentDialog Parent;
        private readonly List<GuiDialog> loaded = new();
        private readonly List<GuiComposer> composers = new();
        private readonly string originalLocale = Lang.CurrentLocale;
        private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        internal IEnumerable<GuiDialogRecipeBook> Menus => loaded.OfType<GuiDialogRecipeBook>();
        internal GuiElementRecipeBookButton ToggleButton =>
            (GuiElementRecipeBookButton)Parent.Composers["maininventory"].GetToggleButton("vanillaexpanded-crafting-toggle");
        internal readonly IAsset BookAsset;
        internal readonly IAsset OptionsAsset;
        internal readonly List<(int Width, int Height, int SurfaceWidth, int SurfaceHeight, int? Color)> OptionsDraws = new();
        internal readonly List<(IAsset Asset, int Width, int Height, int? Color)> BookDraws = new();
        internal readonly Dictionary<string, IAsset> CategoryAssets = new();
        internal readonly List<(IAsset Asset, int X, int Y, int Width, int Height, int SurfaceWidth, int SurfaceHeight, int? Color)> CategoryDraws = new();
        internal int TextureCount => graphics.Textures.Count;
        internal readonly Dictionary<string, Vec2i> DialogPositions;

        internal MenuFixture(bool enabled = true, float scale = 1, bool longLabels = false, bool includeRecipes = false,
            Dictionary<string, bool>? savedSettings = null,
            Dictionary<string, Vec2i>? savedPositions = null, Dictionary<string, string>? categoryNames = null)
        {
            graphics = new RecipeBookRenderFixture(scale);
            DialogPositions = savedPositions ?? new();
            graphics.Gui.Setup(value => value.GetDialogPosition(It.IsAny<string>()))
                .Returns((string name) => DialogPositions.GetValueOrDefault(name));
            graphics.Gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()))
                .Callback<string, Vec2i?>((name, position) =>
                {
                    if (position == null) DialogPositions.Remove(name);
                    else DialogPositions[name] = new(position.X, position.Y);
                });
            Lifecycle = new LifecycleFixture(enabled, savedSettings);
            var translations = new Mock<ITranslationService>();
            translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key switch
                {
                    "vanillaexpanded:recipebook-title" => longLabels ? "Recipe book with all available recipes" : "Recipe Book",
                    "vanillaexpanded:recipebook-auto" => longLabels ? "Automatic selection" : "Auto",
                    "vanillaexpanded:recipebook-handbook" => longLabels ? "Open survival handbook" : "Handbook",
                    "vanillaexpanded:recipebook-craftable-only" => longLabels
                        ? "Hide recipes without all required ingredients" : "Hide uncraftable",
                    "vanillaexpanded:recipebook-sort" => longLabels ? "Recipe sorting preference" : "Sort by",
                    "vanillaexpanded:recipebook-sort-availability" => longLabels ? "Current ingredient availability" : "Availability",
                    "vanillaexpanded:recipebook-sort-name" => longLabels ? "Alphabetical output name" : "Name",
                    "vanillaexpanded:recipebook-empty" => "No recipes are available in this category.",
                    "vanillaexpanded:recipebook-empty-search" => "No recipes match your search in this category.",
                    "vanillaexpanded:recipebook-empty-craftable" => "No recipes in this category are currently craftable.",
                    "vanillaexpanded:recipebook-empty-craftable-search" => longLabels
                        ? "Recipes match the current search in the selected category, but none can currently be crafted with the available ingredients."
                        : "Recipes match your search, but none are currently craftable.",
                    "vanillaexpanded:recipebook-clear-search" => longLabels ? "Clear the current search text" : "Clear search",
                    "vanillaexpanded:recipebook-show-uncraftable" => longLabels
                        ? "Show recipes with unavailable ingredients" : "Show uncraftable",
                    "vanillaexpanded:recipebook-toggle" => "Recipe Book",
                    "vanillaexpanded:recipebook-remember-open" => longLabels
                        ? "Automatically reopen the recipe book with the inventory" : "Reopen with inventory",
                    "vanillaexpanded:recipebook-category-all" => longLabels
                        ? "All recipes available in the selected crafting catalogue" : "All",
                    "vanillaexpanded:recipebook-category-other" => "Other",
                    _ => key
                });
            translations.Setup<string?>(value => value.GetIfExists(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => categoryNames?.GetValueOrDefault(key));
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
                @"..\..\..\..\VanillaExpanded\assets\vanillaexpanded\textures\gui\recipe-book.svg")));
            BookAsset = Mock.Of<IAsset>(value => value.Data == svg);
            assets.Setup(value => value.Get(new AssetLocation("vanillaexpanded:textures/gui/recipe-book.svg")))
                .Returns(BookAsset);
            byte[] optionsSvg = File.ReadAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                @"..\..\..\..\VanillaExpanded\assets\vanillaexpanded\textures\gui\recipe-book-options.svg")));
            OptionsAsset = Mock.Of<IAsset>(value => value.Data == optionsSvg);
            assets.Setup(value => value.Get(new AssetLocation("vanillaexpanded:textures/gui/recipe-book-options.svg")))
                .Returns(OptionsAsset);
            foreach (string code in RecipeBookCategoryIcons.Codes)
            {
                byte[] categorySvg = File.ReadAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                    @"..\..\..\..\VanillaExpanded\assets\vanillaexpanded\textures\gui\recipe-book-categories", code + ".svg")));
                var asset = Mock.Of<IAsset>(value => value.Data == categorySvg);
                CategoryAssets.Add(code, asset);
                assets.Setup(value => value.Get(RecipeBookCategoryIcons.Asset(code))).Returns(asset);
            }
            api.SetupGet(value => value.Assets).Returns(assets.Object);
            graphics.Gui.Setup(value => value.DrawSvg(It.IsAny<IAsset>(), It.IsAny<ImageSurface>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
                .Callback<IAsset, ImageSurface, int, int, int, int, int?>((asset, surface, x, y, width, height, color) =>
                {
                    if (ReferenceEquals(asset, BookAsset)) BookDraws.Add((asset, width, height, color));
                    else if (ReferenceEquals(asset, OptionsAsset))
                        OptionsDraws.Add((width, height, surface.Width, surface.Height, color));
                    else CategoryDraws.Add((asset, x, y, width, height, surface.Width, surface.Height, color));
                });
            graphics.Render.Setup(value => value.BitmapCreateFromPng(It.IsAny<byte[]>()))
                .Returns(() => new BitmapExternal(2, 2));
            graphics.Gui.SetupGet(value => value.Icons).Returns(new IconUtil(api.Object));
            graphics.Gui.SetupGet(value => value.LoadedGuis).Returns(loaded);
            graphics.Gui.Setup(value => value.RegisterDialog(It.IsAny<GuiDialog[]>()))
                .Callback<GuiDialog[]>(dialogs => loaded.AddRange(dialogs));
            graphics.Gui.Setup(value => value.RequestFocus(It.IsAny<GuiDialog>()))
                .Callback<GuiDialog>(dialog =>
                {
                    foreach (var existing in loaded) existing.UnFocus();
                    dialog.Focus();
                });
            var window = ElementBounds.Fixed(0, 0, 1920, 1080).WithEmptyParent();
            window.CalcWorldBounds();
            graphics.Gui.SetupGet(value => value.WindowBounds).Returns(window);
            graphics.Render.SetupGet(value => value.FrameWidth).Returns(() => window.OuterWidthInt);
            graphics.Render.SetupGet(value => value.FrameHeight).Returns(() => window.OuterHeightInt);
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
                ElementBounds.Fixed(72, 214, 48, 48)), "outputslot");
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

        internal void SelectMovement(GuiDialogRecipeBook book, bool movable)
        {
            var titlebar = book.SingleComposer.GetTitleBar("titlebar");
            int x = (int)(titlebar.Bounds.absX + titlebar.Bounds.OuterWidth - GuiElement.scaled(44));
            int y = (int)(titlebar.Bounds.absY + GuiElement.scaled(10));
            SetMouse(x, y);
            book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            var menu = (GuiElementListMenu)typeof(GuiElementDialogTitleBar)
                .GetField("listMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(titlebar)!;
            Assert.True(menu.IsOpened);
            Assert.Equal(new[] { "auto", "manual" }, menu.Values);
            x = (int)(menu.Bounds.renderX + GuiElement.scaled(5));
            y = (int)(menu.Bounds.renderY + menu.Bounds.InnerHeight + GuiElement.scaled(movable ? 45 : 15));
            SetMouse(x, y);
            var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            book.OnMouseDown(down);
            Assert.True(down.Handled);
            book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            Assert.False(menu.IsOpened);
            Assert.Equal(movable, titlebar.Movable);
        }

        internal GuiElementRecipeBookOptions OpenOptions(GuiDialogRecipeBook book)
        {
            var options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
            int loadedCount = loaded.Count;
            int x = (int)(options.Bounds.absX + options.Bounds.OuterWidth / 2);
            int y = (int)(options.Bounds.absY + options.Bounds.OuterHeight / 2);
            book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
            Assert.True(options.IsOpened);
            Assert.Equal(loadedCount, loaded.Count);
            return options;
        }

        internal void ToggleReopenOption(GuiDialogRecipeBook book)
        {
            var options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
            int x = (int)(options.PopupBounds.absX + GuiElement.scaled(10));
            int y = (int)(options.PopupBounds.absY + GuiElement.scaled(15));
            var input = Mock.Get(Lifecycle.Api.Object.Input);
            input.SetupGet(value => value.MouseX).Returns(x);
            input.SetupGet(value => value.MouseY).Returns(y);
            var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
            book.OnMouseDown(down);
            Assert.True(down.Handled);
            book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        }

        internal void ToggleCraftable(GuiDialogRecipeBook menu)
        {
            var bounds = menu.SingleComposer.GetSwitch("craftable-only").Bounds;
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
            Assert.Empty(Menus);
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

        internal void AssertToggleOpen()
        {
            Assert.True(ToggleButton.Visible);
            Assert.True(ToggleButton.Enabled);
            Assert.True(ToggleButton.Focusable);
            Assert.True(ToggleButton.On);
            var bounds = ToggleButton.Bounds;
            Assert.True(ToggleButton.IsPositionInside((int)(bounds.absX + 5), (int)(bounds.absY + 5)));
            graphics.Render.Invocations.Clear();
            ToggleButton.RenderInteractiveElements(0);
            Assert.NotEmpty(graphics.Render.Invocations);
        }

        internal void ResizePanelSpace(double width, double height)
        {
            var window = graphics.Gui.Object.WindowBounds;
            window.fixedWidth = 900 + 2 / GuiElement.scaled(1) + width;
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
                    "{\"EnableRecipeBook\":\"" + (enabled ? "true" : "false") + "\"}")));
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.Systems).Returns(Lifecycle.Loaded
                ? new ModSystem[] { Lifecycle.System } : Array.Empty<ModSystem>());
            Lifecycle.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            LiveConfigReload.NotifyAll(Lifecycle.Api.Object);
        }

        internal void ClickCloseButton(GuiDialogRecipeBook menu)
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
        internal readonly RecipeBookSystem System;
        internal readonly bool Loaded;
        internal int Builds;
        private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableRecipeBook;

        internal LifecycleFixture(bool enabled = true, Dictionary<string, bool>? savedSettings = null)
        {
            savedSettings ??= new();
            var boolSettings = new Mock<ISettingsClass<bool>>();
            boolSettings.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((string key, bool defaultValue) => savedSettings.GetValueOrDefault(key, defaultValue));
            boolSettings.SetupSet(value => value[It.IsAny<string>()] = It.IsAny<bool>())
                .Callback((string key, bool value) => savedSettings[key] = value);
            Api.SetupGet(value => value.Settings).Returns(Mock.Of<ISettings>(value => value.Bool == boolSettings.Object));
            VanillaExpandedModSystem.Config.EnableRecipeBook = enabled;
            Recipes.Add(F.Recipe(F.Ingredient(F.Stack(1, "a", 1))));
            F.Register(Recipes.ToArray());
            Api.SetupGet(value => value.World).Returns(World.Object);
            Api.SetupGet(value => value.ModLoader).Returns(Mock.Of<IModLoader>());
            Api.SetupGet(value => value.Event).Returns(Events.Object);
            World.SetupGet(value => value.GridRecipes).Returns(Recipes);
            World.SetupGet(value => value.FastSearchRecipesByIngredient).Returns(F.Catalogue.Index);
            Events.Setup(value => value.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => Queue.Enqueue(action));
            System = new RecipeBookSystem(_ =>
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
            VanillaExpandedModSystem.Config.EnableRecipeBook = originalEnabled;
        }
    }
}
