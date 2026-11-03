using System.Reflection;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookNativeBoundsCompatibilityTests
{
    /// <summary>Verifies render-lifecycle Follow translates native hit, render, and scissor regions at fractional scales.</summary>
    [Theory]
    [InlineData(1.25f, 13.25, 8.5)]
    [InlineData(1.75f, 17.5, 9.25)]
    public void RenderLifecycleTranslationMovesNativeHitRenderAndScissorRegions(
        float scale, double deltaX, double deltaY)
    {
        // Arrange
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        f.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(countBounds: true,
            createIconShader: f.CreateIconShader);
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var composer = book.SingleComposer;
        var search = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search"));
        var tabs = Assert.IsType<GuiElementIconTabs>(composer.GetElement("categories"));
        var tab = tabs.Composer!.GetToggleButton("tab-0");
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
        var tile = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        var scrollbar = Assert.IsType<GuiElementStyledScrollbar>(composer.GetScrollbar("scroll"));
        book.OnRenderGUI(0);
        var oldSearchPoint = NearTopLeft(search.FieldBounds);
        var oldClearPoint = NearTopLeft(search.ClearBounds);
        var oldTabPoint = NearTopLeft(tab.Bounds);
        var oldTilePoint = NearTopLeft(tile.PixelBounds);
        Assert.True(search.IsPositionInside(oldSearchPoint.X, oldSearchPoint.Y));
        Assert.True(search.ClearBounds.PointInside(oldClearPoint.X, oldClearPoint.Y));
        Assert.True(tab.IsPositionInside(oldTabPoint.X, oldTabPoint.Y));
        Assert.True(tile.IsPositionInside(oldTilePoint.X, oldTilePoint.Y));
        var oldScissor = CaptureScissor(f, tabs);
        var oldScrollbarDraws = CaptureScrollbarDraws(f, scrollbar);
        var root = calls.GetRootBounds(book);
        var child = calls.GetChildBounds(book);
        int rootCalculations = root.Calculations;
        int childCalculations = child.Calculations;
        var parent = f.Parent.Composers["maininventory"].Bounds;

        // Act: move the actual inventory parent, then exercise the dialog's real render lifecycle.
        parent.fixedX += deltaX;
        parent.fixedY += deltaY;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        book.OnRenderGUI(0);

        // Assert the shell is retained and the native root-only update preserves translated descendants.
        double pixelDeltaX = deltaX * scale;
        double pixelDeltaY = deltaY * scale;
        Assert.Same(composer, book.SingleComposer);
        Assert.Same(search, book.SingleComposer.GetTextInput("search"));
        Assert.Same(tabs, book.SingleComposer.GetElement("categories"));
        Assert.Same(tiles, book.SingleComposer.GetElement("tiles"));
        Assert.Equal(rootCalculations + 1, root.Calculations);
        Assert.Equal(childCalculations, child.Calculations);
        Assert.Equal(1, calls.MeasurementCalls);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(1, calls.CompositionCalls);

        var newSearchPoint = Center(search.FieldBounds);
        var newClearPoint = Center(search.ClearBounds);
        var newTabPoint = Center(tab.Bounds);
        var newTilePoint = Center(tile.PixelBounds);
        Assert.False(search.IsPositionInside(oldSearchPoint.X, oldSearchPoint.Y));
        Assert.True(search.IsPositionInside(newSearchPoint.X, newSearchPoint.Y));
        Assert.False(search.ClearBounds.PointInside(oldClearPoint.X, oldClearPoint.Y));
        Assert.True(search.ClearBounds.PointInside(newClearPoint.X, newClearPoint.Y));
        Assert.False(tab.IsPositionInside(oldTabPoint.X, oldTabPoint.Y));
        Assert.True(tab.IsPositionInside(newTabPoint.X, newTabPoint.Y));
        Assert.False(tile.IsPositionInside(oldTilePoint.X, oldTilePoint.Y));
        Assert.True(tile.IsPositionInside(newTilePoint.X, newTilePoint.Y));

        var newScissor = CaptureScissor(f, tabs);
        Assert.Same(oldScissor.Bounds, newScissor.Bounds);
        Assert.InRange(Math.Abs(newScissor.RenderX - oldScissor.RenderX - pixelDeltaX), 0, 1);
        Assert.InRange(Math.Abs(newScissor.RenderY - oldScissor.RenderY - pixelDeltaY), 0, 1);
        var newScrollbarDraws = CaptureScrollbarDraws(f, scrollbar);
        Assert.NotEmpty(oldScrollbarDraws);
        Assert.Equal(oldScrollbarDraws.Length, newScrollbarDraws.Length);
        for (int i = 0; i < oldScrollbarDraws.Length; i++)
        {
            Assert.InRange(Math.Abs(newScrollbarDraws[i].X - oldScrollbarDraws[i].X - pixelDeltaX), 0, 1);
            Assert.InRange(Math.Abs(newScrollbarDraws[i].Y - oldScrollbarDraws[i].Y - pixelDeltaY), 0, 1);
        }
    }

    [Theory]
    [InlineData(1.25f, 360.25, 21.25)]
    [InlineData(1.75f, 180, 20)]
    public void RootOnlyPositionUpdateTranslatesComposedNativeControlsWithoutRecomposition(
        float scale, double deltaX, double deltaY)
    {
        // Arrange
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        foreach (var recipe in f.Lifecycle.Recipes)
            recipe.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "phase-one-match");
        for (int i = 0; i < 40; i++)
        {
            var recipe = f.Lifecycle.F.Catalogue.Recipe("phase-one-" + i);
            recipe.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "phase-one-match");
            f.Lifecycle.Recipes.Add(recipe);
        }
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();

        var book = Assert.Single(f.Menus);
        var composer = book.SingleComposer;
        var search = Assert.IsType<GuiElementClearableTextInput>(composer.GetTextInput("search"));
        search.SetValue("phase-one-match");
        Assert.True(search.ClearVisible);
        Assert.True(composer.FocusElement(search.TabIndex));

        var tabs = Assert.IsType<GuiElementIconTabs>(composer.GetElement("categories"));
        var tab = tabs.Composer!.GetToggleButton("tab-0");
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
        var tile = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[tiles.Geometry.Columns]);
        Assert.NotNull(tile.Selection);
        var scrollbar = Assert.IsType<GuiElementStyledScrollbar>(composer.GetScrollbar("scroll"));
        var up = GetArrow(scrollbar, "up");
        var down = GetArrow(scrollbar, "down");
        var titlebar = composer.GetTitleBar("titlebar");
        var options = Assert.IsType<GuiElementRecipeBookOptions>(composer.GetElement("options"));
        var footer = new[]
        {
            composer.GetButton("previous"),
            composer.GetButton("auto"),
            composer.GetButton("next"),
            composer.GetButton("handbook")
        };

        tiles.Scroll(75 * scale);
        tabs.RenderInteractiveElements(0);
        scrollbar.RenderInteractiveElements(0);
        search.RenderInteractiveElements(0);
        titlebar.RenderInteractiveElements(0);
        options.RenderInteractiveElements(0);
        foreach (var button in footer) button.RenderInteractiveElements(0);
        var oldScrollbarDraws = CaptureScrollbarDraws(f, scrollbar);

        ElementBounds[] tracked =
        [
            composer.Bounds,
            search.Bounds, search.FieldBounds, search.ClearBounds, search.ClearHint.Bounds,
            tabs.Bounds, tabs.Composer!.Bounds, tabs.Viewport!,
            tab.Bounds, tabs.Hints[0].Bounds,
            tiles.Bounds, tile.Bounds, tile.PixelBounds, tiles.InsideClipBounds!,
            scrollbar.Bounds, up.Bounds, down.Bounds,
            .. footer.Select(button => button.Bounds),
            composer.GetButton("clear-search").Bounds,
            composer.GetButton("show-uncraftable").Bounds,
            titlebar.Bounds, options.Bounds,
            composer.GetHoverText("craftable-hint").Bounds,
            composer.GetHoverText("sort-hint").Bounds,
            composer.GetHoverText("handbook-hint").Bounds,
            composer.GetHoverText("options-hint").Bounds
        ];
        var before = tracked.Distinct().Select(BoundsState.Capture).ToArray();
        Assert.All(before.Where(state => !ReferenceEquals(state.Bounds, tile.PixelBounds)),
            state => Assert.True(state.Bounds.Initialized, state.Bounds.ToString()));

        var oldSearchPoint = Center(search.FieldBounds);
        var oldClearPoint = Center(search.ClearBounds);
        var oldTabPoint = Center(tab.Bounds);
        var oldTilePoint = Center(tile.PixelBounds);
        var oldUpPoint = Center(up.Bounds);
        var oldDownPoint = Center(down.Bounds);
        var oldFooterPoint = Center(footer[1].Bounds);
        var oldTitlePoint = Center(titlebar.Bounds);
        var oldOptionsPoint = Center(options.Bounds);
        var oldScissor = CaptureScissor(f, tabs);
        double oldPixelClipX = oldScissor.RenderX;
        double oldPixelClipY = oldScissor.RenderY;
        double oldViewportX = tiles.InsideClipBounds!.renderX;
        double oldViewportY = tiles.InsideClipBounds.renderY;
        int textureCount = f.TextureCount;
        double pixelDeltaX = deltaX * scale;
        double pixelDeltaY = deltaY * scale;
        Assert.True(up.Enabled);
        Assert.True(down.Enabled);
        Assert.True(search.HasFocus);
        Assert.True(search.IsPositionInside(oldSearchPoint.X, oldSearchPoint.Y));
        Assert.True(search.ClearBounds.PointInside(oldClearPoint.X, oldClearPoint.Y));
        Assert.True(tile.IsPositionInside(oldTilePoint.X, oldTilePoint.Y));

        // Act
        composer.Bounds.fixedX += deltaX;
        composer.Bounds.fixedY += deltaY;
        composer.Bounds.CalcWorldBounds();
        _ = tile.PixelBounds;

        // Assert
        Assert.Same(composer, book.SingleComposer);
        Assert.Same(search, book.SingleComposer.GetTextInput("search"));
        Assert.Same(tabs, book.SingleComposer.GetElement("categories"));
        Assert.Same(tiles, book.SingleComposer.GetElement("tiles"));
        Assert.Same(scrollbar, book.SingleComposer.GetScrollbar("scroll"));
        Assert.Same(titlebar, book.SingleComposer.GetTitleBar("titlebar"));
        Assert.True(search.HasFocus);
        Assert.Equal(textureCount, f.TextureCount);
        Assert.All(before.Where(state => !ReferenceEquals(state.Bounds, tile.PixelBounds)),
            state => Assert.True(state.Bounds.Initialized, state.Bounds.ToString()));
        foreach (var state in before)
            AssertTranslated(state, pixelDeltaX, pixelDeltaY,
                ReferenceEquals(state.Bounds, tile.PixelBounds), ReferenceEquals(state.Bounds, composer.Bounds));
        var newScrollbarDraws = CaptureScrollbarDraws(f, scrollbar);
        Assert.Equal(oldScrollbarDraws.Length, newScrollbarDraws.Length);
        for (int i = 0; i < oldScrollbarDraws.Length; i++)
        {
            Assert.InRange(Math.Abs(newScrollbarDraws[i].X - oldScrollbarDraws[i].X - pixelDeltaX), 0, 1);
            Assert.InRange(Math.Abs(newScrollbarDraws[i].Y - oldScrollbarDraws[i].Y - pixelDeltaY), 0, 1);
        }

        var newSearchPoint = Center(search.FieldBounds);
        var newClearPoint = Center(search.ClearBounds);
        var newTabPoint = Center(tab.Bounds);
        var newTilePoint = Center(tile.PixelBounds);
        var newUpPoint = Center(up.Bounds);
        var newDownPoint = Center(down.Bounds);
        var newFooterPoint = Center(footer[1].Bounds);
        var newTitlePoint = Center(titlebar.Bounds);
        var newOptionsPoint = Center(options.Bounds);

        Assert.False(search.IsPositionInside(oldSearchPoint.X, oldSearchPoint.Y));
        Assert.True(search.IsPositionInside(newSearchPoint.X, newSearchPoint.Y));
        Assert.False(search.ClearBounds.PointInside(oldClearPoint.X, oldClearPoint.Y));
        Assert.True(search.ClearBounds.PointInside(newClearPoint.X, newClearPoint.Y));
        Assert.False(tab.IsPositionInside(oldTabPoint.X, oldTabPoint.Y));
        Assert.True(tab.IsPositionInside(newTabPoint.X, newTabPoint.Y));
        Assert.False(tile.IsPositionInside(oldTilePoint.X, oldTilePoint.Y));
        Assert.True(tile.IsPositionInside(newTilePoint.X, newTilePoint.Y));
        Assert.True(tile.IsInViewport);
        Assert.False(up.IsPositionInside(oldUpPoint.X, oldUpPoint.Y));
        Assert.True(up.IsPositionInside(newUpPoint.X, newUpPoint.Y));
        Assert.False(down.IsPositionInside(oldDownPoint.X, oldDownPoint.Y));
        Assert.True(down.IsPositionInside(newDownPoint.X, newDownPoint.Y));
        Assert.False(footer[1].IsPositionInside(oldFooterPoint.X, oldFooterPoint.Y));
        Assert.True(footer[1].IsPositionInside(newFooterPoint.X, newFooterPoint.Y));
        Assert.False(titlebar.IsPositionInside(oldTitlePoint.X, oldTitlePoint.Y));
        Assert.True(titlebar.IsPositionInside(newTitlePoint.X, newTitlePoint.Y));
        Assert.False(options.IsPositionInside(oldOptionsPoint.X, oldOptionsPoint.Y));
        Assert.True(options.IsPositionInside(newOptionsPoint.X, newOptionsPoint.Y));

        int fullFieldX = (int)(search.FieldBounds.absX + search.FieldBounds.OuterWidth - 2);
        int fullFieldY = (int)(search.FieldBounds.absY + search.FieldBounds.OuterHeight / 2);
        Assert.False(search.Bounds.PointInside(fullFieldX, fullFieldY));
        Assert.True(search.IsPositionInside(fullFieldX, fullFieldY));

        // The clipped child composer passes its original pixel-snapped clip to the native renderer.
        var newScissor = CaptureScissor(f, tabs);
        Assert.Same(oldScissor.Bounds, newScissor.Bounds);
        Assert.InRange(Math.Abs(newScissor.RenderX - oldPixelClipX - pixelDeltaX), 0, 1);
        Assert.InRange(Math.Abs(newScissor.RenderY - oldPixelClipY - pixelDeltaY), 0, 1);
        Assert.InRange(Math.Abs(tiles.InsideClipBounds.renderX - oldViewportX - pixelDeltaX), 0, 1);
        Assert.InRange(Math.Abs(tiles.InsideClipBounds.renderY - oldViewportY - pixelDeltaY), 0, 1);

        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        tabs.RenderInteractiveElements(0);
        Assert.Contains(render.Invocations, call => call.Method.Name == nameof(IRenderAPI.PushScissor)
            && ReferenceEquals(call.Arguments[0], newScissor.Bounds));
        render.Invocations.Clear();
        // The icon renderer is covered separately; hide tiles here so this assertion isolates
        // the composed tile viewport's native scissor path without requiring a graphics shader.
        var tileSelections = tiles.Elements.Cast<GuiElementRecipeBookTile>()
            .Select(element => element.Selection).ToArray();
        try
        {
            foreach (GuiElementRecipeBookTile element in tiles.Elements)
                element.Selection = null;
            tiles.RenderInteractiveElements(0);
        }
        finally
        {
            for (int i = 0; i < tiles.Elements.Count; i++)
                ((GuiElementRecipeBookTile)tiles.Elements[i]).Selection = tileSelections[i];
        }
        Assert.Contains(render.Invocations, call => call.Method.Name == nameof(IRenderAPI.PushScissor)
            && ReferenceEquals(call.Arguments[0], tiles.InsideClipBounds));
        render.Invocations.Clear();
        search.RenderInteractiveElements(0);
        Assert.Contains(render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Any(argument => ReferenceEquals(argument, search.FieldBounds)));
        Assert.Contains(render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Any(argument => ReferenceEquals(argument, search.ClearBounds)));

        // A click at the previous arrow location is no longer routed to the control; the translated arrow still works.
        Mock.Get(f.Lifecycle.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw)
            .Returns(new bool[512]);
        float scrollBeforeOldHit = tiles.ScrollPosition;
        book.OnMouseDown(new MouseEvent(oldUpPoint.X, oldUpPoint.Y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(oldUpPoint.X, oldUpPoint.Y, EnumMouseButton.Left, 0));
        Assert.Equal(scrollBeforeOldHit, tiles.ScrollPosition);
        book.OnMouseDown(new MouseEvent(newUpPoint.X, newUpPoint.Y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(newUpPoint.X, newUpPoint.Y, EnumMouseButton.Left, 0));
        Assert.True(tiles.ScrollPosition < scrollBeforeOldHit);

        Assert.True(composer.FocusElement(search.TabIndex));
        var clearDown = new MouseEvent(newClearPoint.X, newClearPoint.Y, EnumMouseButton.Left, 0);
        search.OnMouseDownOnElement(f.Lifecycle.Api.Object, clearDown);
        var clearUp = new MouseEvent(newClearPoint.X, newClearPoint.Y, EnumMouseButton.Left, 0);
        search.OnMouseUpOnElement(f.Lifecycle.Api.Object, clearUp);
        Assert.True(clearUp.Handled);
        Assert.Equal("", search.GetText());
        var typed = new KeyEvent { KeyCode = (int)GlKeys.Z, KeyChar = 'z' };
        book.OnKeyPress(typed);
        Assert.True(typed.Handled);
        Assert.Equal("z", search.GetText());
    }

    [Fact]
    public void RootOnlyPositionUpdateTranslatesOpenOptionsPopupAndRetainsNativeFocus()
    {
        // Arrange
        using var f = new MenuFixture(scale: 1.25f);
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var composer = book.SingleComposer;
        var options = f.OpenOptions(book);
        options.RenderInteractiveElements(0);
        options.RestoreMenu(focusSize: true, text: "57");
        var popup = GetPopup(options);
        var tileSizeHint = GetPopupHint(popup);
        var optionsHint = GetPrivateHint(options);
        Assert.True(options.IsOpened);
        Assert.True(options.SizeInput.HasFocus);
        // This detached hover bounds shares the options control's parent but is not in its child list.
        optionsHint.Bounds.CalcWorldBounds();

        var tracked = new[]
        {
            composer.Bounds, options.Bounds, options.PopupBounds, popup.Bounds,
            options.Checkbox.Bounds, options.SizeInput.Bounds,
            tileSizeHint.Bounds, optionsHint.Bounds,
            composer.GetHoverText("options-hint").Bounds
        };
        var before = tracked.Distinct().Select(BoundsState.Capture).ToArray();
        Assert.All(before.Where(state => !ReferenceEquals(state.Bounds, composer.Bounds)),
            state => Assert.True(state.Bounds.Initialized, state.Bounds.ToString()));
        int textureCount = f.TextureCount;
        var oldPopupPoint = Center(options.PopupBounds);
        var oldInputPoint = Center(options.SizeInput.Bounds);
        var oldCheckboxPoint = Center(options.Checkbox.Bounds);

        // Act
        const double deltaX = 360.25;
        const double deltaY = 21.25;
        composer.Bounds.fixedX += deltaX;
        composer.Bounds.fixedY += deltaY;
        composer.Bounds.CalcWorldBounds();

        // Assert
        double pixelDeltaX = deltaX * 1.25;
        double pixelDeltaY = deltaY * 1.25;
        Assert.Same(composer, book.SingleComposer);
        Assert.Same(options, composer.GetElement("options"));
        Assert.Same(popup, GetPopup(options));
        Assert.True(options.IsOpened);
        Assert.True(options.SizeInput.HasFocus);
        Assert.Equal("57", options.SizeInput.GetText());
        Assert.Equal(textureCount, f.TextureCount);
        foreach (var state in before)
            AssertTranslated(state, pixelDeltaX, pixelDeltaY,
                specialPixelBounds: false, specialRoot: ReferenceEquals(state.Bounds, composer.Bounds));

        var newPopupPoint = Center(options.PopupBounds);
        var newInputPoint = Center(options.SizeInput.Bounds);
        var newCheckboxPoint = Center(options.Checkbox.Bounds);
        Assert.False(options.PopupBounds.PointInside(oldPopupPoint.X, oldPopupPoint.Y));
        Assert.True(options.PopupBounds.PointInside(newPopupPoint.X, newPopupPoint.Y));
        Assert.False(options.SizeInput.Bounds.PointInside(oldInputPoint.X, oldInputPoint.Y));
        Assert.True(options.SizeInput.Bounds.PointInside(newInputPoint.X, newInputPoint.Y));
        Assert.False(options.Checkbox.Bounds.PointInside(oldCheckboxPoint.X, oldCheckboxPoint.Y));
        Assert.True(options.Checkbox.Bounds.PointInside(newCheckboxPoint.X, newCheckboxPoint.Y));

        options.RenderInteractiveElements(0);
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        Assert.Contains(render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Count >= 3
            && Convert.ToDouble(call.Arguments[1]) == Math.Round(options.Bounds.absX, MidpointRounding.AwayFromZero)
            && Convert.ToDouble(call.Arguments[2]) == Math.Round(options.Bounds.absY, MidpointRounding.AwayFromZero));

        // Arrange: move focus to another popup control so a dropped click cannot pass.
        Assert.True(popup.FocusElement(options.Checkbox.TabIndex));
        Assert.True(options.Checkbox.HasFocus);
        Assert.False(options.SizeInput.HasFocus);

        // Act: dispatch a native composer click at the translated number input.
        Mock.Get(f.Lifecycle.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw)
            .Returns(new bool[512]);
        f.SetMouse(newInputPoint.X, newInputPoint.Y);
        book.OnMouseDown(new MouseEvent(newInputPoint.X, newInputPoint.Y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(newInputPoint.X, newInputPoint.Y, EnumMouseButton.Left, 0));

        // Assert
        Assert.True(options.SizeInput.HasFocus);
        Assert.False(options.Checkbox.HasFocus);
        Assert.Equal("57", options.SizeInput.GetText());
    }

    [Fact]
    public void NativeRootCalculationLeavesInitializedChildrenAloneAndRepairsAnExplicitlyDirtyChild()
    {
        // Arrange
        using var nativeScope = new NativeScope();
        var window = ElementBounds.Fixed(0, 0, 800, 600).WithEmptyParent();
        var root = new CountingBounds(20, 30, 300, 200);
        window.WithChild(root);
        var child = new CountingBounds(10, 15, 80, 40);
        root.WithChild(child);
        window.CalcWorldBounds();
        Assert.Equal(1, root.Calculations);
        Assert.Equal(1, child.Calculations);
        double originalChildX = child.absX;
        double originalChildY = child.absY;

        // Act: position-only movement leaves the child initialized; native getters derive its new origin.
        root.fixedX += 17.25;
        root.fixedY += 9.5;
        root.CalcWorldBounds();

        // Assert
        Assert.Equal(2, root.Calculations);
        Assert.Equal(1, child.Calculations);
        Assert.True(root.Initialized);
        Assert.True(child.Initialized);
        Assert.Equal(originalChildX + 17.25 * GuiElement.scaled(1), child.absX, 4);
        Assert.Equal(originalChildY + 9.5 * GuiElement.scaled(1), child.absY, 4);

        // A child whose own geometry changed still follows the explicit dirty/recalculate path.
        child.fixedWidth += 12;
        child.MarkDirtyRecursive();
        root.CalcWorldBounds();
        Assert.Equal(2, child.Calculations);
        Assert.True(child.Initialized);
        Assert.Equal(92 * GuiElement.scaled(1), child.OuterWidth, 4);
    }

    private static GuiElementStyledArrowButton GetArrow(GuiElementStyledScrollbar scrollbar, string name) =>
        (GuiElementStyledArrowButton)typeof(GuiElementStyledScrollbar)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scrollbar)!;

    private static GuiComposer GetPopup(GuiElementRecipeBookOptions options) =>
        (GuiComposer)typeof(GuiElementRecipeBookOptions)
            .GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(options)!;

    private static GuiElementHoverText GetPrivateHint(GuiElementRecipeBookOptions options) =>
        (GuiElementHoverText)typeof(GuiElementRecipeBookOptions)
            .GetField("hint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(options)!;

    private static GuiElementHoverText GetPopupHint(GuiComposer popup)
    {
        // RecipeBookOptions adds this hover element without an explicit key.
        var elements = (IDictionary<string, GuiElement>)typeof(GuiComposer)
            .GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(popup)!;
        return Assert.IsType<GuiElementHoverText>(Assert.Single(elements.Values.OfType<GuiElementHoverText>()));
    }

    private readonly record struct ScissorSnapshot(ElementBounds Bounds, double RenderX, double RenderY);

    private static ScissorSnapshot CaptureScissor(MenuFixture f, GuiElementIconTabs tabs)
    {
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        tabs.RenderInteractiveElements(0);
        var call = Assert.Single(render.Invocations,
            invocation => invocation.Method.Name == nameof(IRenderAPI.PushScissor));
        var bounds = Assert.IsAssignableFrom<ElementBounds>(call.Arguments[0]);
        return new(bounds, bounds.renderX, bounds.renderY);
    }

    private static (double X, double Y)[] CaptureScrollbarDraws(
        MenuFixture f, GuiElementStyledScrollbar scrollbar)
    {
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        scrollbar.RenderInteractiveElements(0);
        return render.Invocations
            .Where(call => call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
                && call.Arguments.Count >= 3
                && call.Arguments[1] is IConvertible
                && call.Arguments[2] is IConvertible)
            .Select(call => (Convert.ToDouble(call.Arguments[1]), Convert.ToDouble(call.Arguments[2])))
            .ToArray();
    }

    private static (int X, int Y) Center(ElementBounds bounds) =>
        ((int)(bounds.absX + bounds.OuterWidth / 2), (int)(bounds.absY + bounds.OuterHeight / 2));

    /// <summary>Returns a point just inside a control's upper-left edge to distinguish small native translations.</summary>
    private static (int X, int Y) NearTopLeft(ElementBounds bounds) =>
        ((int)Math.Ceiling(bounds.absX + 1), (int)Math.Ceiling(bounds.absY + 1));

    private static void AssertTranslated(BoundsState before, double deltaX, double deltaY,
        bool specialPixelBounds, bool specialRoot)
    {
        var current = before.Bounds;
        Assert.Same(before.Parent, current.ParentBounds);
        Assert.Equal(before.Alignment, current.Alignment);
        Assert.Equal(before.HorizontalSizing, current.horizontalSizing);
        Assert.Equal(before.VerticalSizing, current.verticalSizing);
        if (!specialRoot)
        {
            Assert.Equal(before.FixedX, current.fixedX, 5);
            Assert.Equal(before.FixedY, current.fixedY, 5);
        }
        Assert.Equal(before.FixedWidth, current.fixedWidth, 5);
        Assert.Equal(before.FixedHeight, current.fixedHeight, 5);
        Assert.Equal(before.FixedOffsetX, current.fixedOffsetX, 5);
        Assert.Equal(before.FixedOffsetY, current.fixedOffsetY, 5);
        Assert.Equal(before.OuterWidth, current.OuterWidth, 4);
        Assert.Equal(before.OuterHeight, current.OuterHeight, 4);
        if (specialPixelBounds)
        {
            Assert.InRange(Math.Abs(current.absX - before.AbsX - deltaX), 0, 1);
            Assert.InRange(Math.Abs(current.absY - before.AbsY - deltaY), 0, 1);
        }
        else
        {
            Assert.Equal(before.AbsX + deltaX, current.absX, 4);
            Assert.Equal(before.AbsY + deltaY, current.absY, 4);
            Assert.Equal(before.RenderX + deltaX, current.renderX, 4);
            Assert.Equal(before.RenderY + deltaY, current.renderY, 4);
        }
        Assert.Equal(before.Initialized, current.Initialized);
    }

    private sealed record BoundsState(
        ElementBounds Bounds,
        ElementBounds? Parent,
        EnumDialogArea Alignment,
        ElementSizing HorizontalSizing,
        ElementSizing VerticalSizing,
        double FixedX,
        double FixedY,
        double FixedWidth,
        double FixedHeight,
        double FixedOffsetX,
        double FixedOffsetY,
        double AbsX,
        double AbsY,
        double RenderX,
        double RenderY,
        double OuterWidth,
        double OuterHeight,
        bool Initialized)
    {
        internal static BoundsState Capture(ElementBounds bounds) => new(
            bounds, bounds.ParentBounds, bounds.Alignment, bounds.horizontalSizing, bounds.verticalSizing,
            bounds.fixedX, bounds.fixedY, bounds.fixedWidth, bounds.fixedHeight,
            bounds.fixedOffsetX, bounds.fixedOffsetY, bounds.absX, bounds.absY, bounds.renderX, bounds.renderY,
            bounds.OuterWidth, bounds.OuterHeight, bounds.Initialized);
    }

    private sealed class CountingBounds : ElementBounds
    {
        internal int Calculations { get; private set; }

        internal CountingBounds(double x, double y, double width, double height)
        {
            fixedX = x;
            fixedY = y;
            fixedWidth = width;
            fixedHeight = height;
            BothSizing = ElementSizing.Fixed;
        }

        public override void CalcWorldBounds()
        {
            Calculations++;
            base.CalcWorldBounds();
        }
    }
}
