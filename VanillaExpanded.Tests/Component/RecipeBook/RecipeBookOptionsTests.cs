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
public sealed class RecipeBookOptionsTests
{
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
    [InlineData("30", 30)]
    [InlineData("90", 90)]
    public void TileSizeChangesApplyImmediatelyPreserveBrowsingAndPersistAcrossClients(string text, int expectedSize)
    {
        // Arrange
        var savedSettings = new Dictionary<string, int>();
        using (var f = new MenuFixture(includeRecipes: true, savedIntSettings: savedSettings))
        {
            for (int i = 0; i < 80; i++)
                f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-" + i));
            f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
            f.Lifecycle.FinalizeWorld();
            f.Recompose();
            f.Parent.TryOpen();
            f.Toggle();
            var book = Assert.Single(f.Menus);
            var oldTiles = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
            Assert.Equal(45, oldTiles.TileSize);
            var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
            book.SingleComposer.GetTextInput("search").SetValue("OUTPUT");
            session.SelectCategory(CraftingCatalogue.OtherCategory);
            session.Sort.Select("name");
            int selectedOutput = session.Rows[20].Output.Id;
            session.Select(selectedOutput);
            oldTiles.Scroll(180);
            var before = oldTiles.CaptureView();
            Assert.True(oldTiles.ScrollPosition > 0);

            var options = f.OpenOptions(book);
            FocusSizeInput(f, book, options);
            Assert.Equal("45", options.SizeInput.GetText());

            // Act
            options.SizeInput.SetValue(text);
            Assert.Equal(expectedSize, f.Lifecycle.Api.Object.Settings.Int.Get(RecipeBookSystem.TileSizeSetting, 45));
            Assert.True(book.TryOpen());

            // Assert
            var resizedTiles = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
            var resizedOptions = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
            Assert.NotSame(oldTiles, resizedTiles);
            Assert.Equal(expectedSize, resizedTiles.TileSize);
            Assert.Equal(expectedSize + RecipeBookTileGeometry.Gap, resizedTiles.RowPitch);
            Assert.Equal("OUTPUT", book.SingleComposer.GetTextInput("search").GetText());
            Assert.Equal(CraftingCatalogue.OtherCategory, session.Category);
            Assert.Equal("name", session.Sort.Selected.Id);
            Assert.Equal(selectedOutput, session.SelectedOutput);
            Assert.Contains(resizedTiles.Elements.Cast<GuiElementRecipeBookTile>(),
                tile => tile.Selection?.Output.Id == before.AnchorOutput && tile.IsInViewport);
            Assert.True(resizedTiles.ScrollPosition > 0);
            Assert.True(resizedOptions.IsOpened);
            Assert.True(resizedOptions.SizeInput.HasFocus);
            Assert.Equal(text, resizedOptions.SizeInput.GetText());
            Assert.Equal(expectedSize, savedSettings[RecipeBookSystem.TileSizeSetting]);
        }

        // A newly-created client reads its own persisted GUI-unit setting.
        using var next = new MenuFixture(savedIntSettings: savedSettings);
        next.Parent.TryOpen();
        next.Toggle();
        var restoredBook = Assert.Single(next.Menus);
        var restoredTiles = Assert.IsType<GuiElementRecipeBookTiles>(restoredBook.SingleComposer.GetElement("tiles"));
        var restoredOptions = next.OpenOptions(restoredBook);
        Assert.Equal(expectedSize, restoredTiles.TileSize);
        Assert.Equal(text, restoredOptions.SizeInput.GetText());
    }

    [Fact]
    public void ReducingTileSizePreservesThePartiallyVisibleAnchorAndOptionsFocus()
    {
        // Arrange
        var savedSettings = new Dictionary<string, int> { [RecipeBookSystem.TileSizeSetting] = 90 };
        using var f = new MenuFixture(includeRecipes: true, savedIntSettings: savedSettings);
        for (int i = 0; i < 80; i++)
            f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-" + i));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
        var oldTiles = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        Assert.Equal(90, oldTiles.TileSize);
        double oldRowPitch = oldTiles.RowPitch;
        int selectedOutput = session.Rows[20].Output.Id;
        session.Select(selectedOutput);
        oldTiles.Scroll(80);
        var before = oldTiles.CaptureView();
        Assert.Equal(80, before.RowOffset, 4);
        Assert.Equal(session.Rows[0].Output.Id, before.AnchorOutput);
        AssertPartiallyVisible(Assert.Single(oldTiles.Elements.Cast<GuiElementRecipeBookTile>(),
            tile => tile.Selection?.Output.Id == before.AnchorOutput), oldTiles.InsideClipBounds);

        var options = f.OpenOptions(book);
        FocusSizeInput(f, book, options);
        options.SizeInput.SetValue("30");

        // Act
        Assert.True(book.TryOpen());

        // Assert
        var resizedTiles = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        var resizedOptions = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        Assert.Equal(30, resizedTiles.TileSize);
        Assert.Equal(80d * resizedTiles.RowPitch / oldRowPitch, resizedTiles.ScrollPosition, 4);
        var retainedAnchor = Assert.Single(resizedTiles.Elements.Cast<GuiElementRecipeBookTile>(),
            tile => tile.Selection?.Output.Id == before.AnchorOutput);
        AssertPartiallyVisible(retainedAnchor, resizedTiles.InsideClipBounds);
        Assert.Equal(selectedOutput, session.SelectedOutput);
        Assert.True(resizedOptions.IsOpened);
        Assert.True(resizedOptions.SizeInput.HasFocus);
        Assert.Equal("30", resizedOptions.SizeInput.GetText());
        Assert.Equal(30, savedSettings[RecipeBookSystem.TileSizeSetting]);

        static void AssertPartiallyVisible(GuiElementRecipeBookTile tile, ElementBounds viewport)
        {
            double top = tile.PixelBounds.renderY;
            double bottom = top + tile.PixelBounds.InnerHeight;
            Assert.True(tile.IsInViewport);
            Assert.True(top < viewport.renderY);
            Assert.True(bottom > viewport.renderY);
            Assert.True(bottom < viewport.renderY + viewport.InnerHeight);
        }
    }

    [Theory]
    [InlineData("29", true)]
    [InlineData("29", false)]
    [InlineData("91", true)]
    [InlineData("91", false)]
    public void InvalidTileSizeIsNotPersistedAndIsRestoredWithLocalizedError(string text, bool commitOnEnter)
    {
        // Arrange
        var savedSettings = new Dictionary<string, int> { [RecipeBookSystem.TileSizeSetting] = 57 };
        using var f = new MenuFixture(savedIntSettings: savedSettings);
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = f.OpenOptions(book);
        FocusSizeInput(f, book, options);
        options.SizeInput.SetValue(text);
        Assert.Equal(57, savedSettings[RecipeBookSystem.TileSizeSetting]);

        // Act
        if (commitOnEnter)
        {
            var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
            book.OnKeyDown(enter);
            Assert.True(enter.Handled);
            Assert.True(options.IsOpened);
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
        Assert.Equal("57", options.SizeInput.GetText());
        Assert.Equal(57, savedSettings[RecipeBookSystem.TileSizeSetting]);
        f.Lifecycle.Api.Verify(api => api.TriggerIngameError(It.IsAny<object>(),
            "recipebook-tile-size", "Localized invalid tile-size message."), Times.Once);
    }

    [Fact]
    public void RecipeBookOptionsWheelChangesTileSizeWithoutScrollingRecipesAndPassesThroughOutsideOptions()
    {
        // Arrange
        var savedSettings = new Dictionary<string, int>();
        using var f = new MenuFixture(includeRecipes: true, savedIntSettings: savedSettings);
        for (int i = 0; i < 80; i++)
            f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-" + i));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(book.SingleComposer.GetElement("tiles"));
        var options = f.OpenOptions(book);
        Assert.Equal(0, tiles.ScrollPosition);

        // The native number input checks modifier slots even when no key modifiers are pressed.
        Mock.Get(f.Lifecycle.Api.Object.Input)
            .SetupGet(input => input.KeyboardKeyStateRaw).Returns(new bool[5]);
        int inputX = (int)(options.SizeInput.Bounds.absX + options.SizeInput.Bounds.OuterWidth / 2);
        int inputY = (int)(options.SizeInput.Bounds.absY + options.SizeInput.Bounds.OuterHeight / 2);
        Assert.True(options.SizeInput.Bounds.PointInside(inputX, inputY));
        f.SetMouse(inputX, inputY);
        var sizeWheel = new MouseWheelEventArgs { deltaPrecise = 1 };

        // Act
        book.OnMouseWheel(sizeWheel);

        // Assert
        Assert.True(sizeWheel.IsHandled);
        Assert.Equal("46", options.SizeInput.GetText());
        Assert.Equal(46, savedSettings[RecipeBookSystem.TileSizeSetting]);
        Assert.Equal(0, tiles.ScrollPosition);

        // The left side of the viewport stays outside the right-aligned options popup.
        int listX = (int)(tiles.InsideClipBounds.absX + 5);
        int listY = (int)(tiles.InsideClipBounds.absY + tiles.InsideClipBounds.InnerHeight / 2);
        Assert.True(tiles.InsideClipBounds.PointInside(listX, listY));
        Assert.False(options.Bounds.PointInside(listX, listY));
        Assert.False(options.PopupBounds.PointInside(listX, listY));
        f.SetMouse(listX, listY);
        var listWheel = new MouseWheelEventArgs { deltaPrecise = -1 };

        // Act
        book.OnMouseWheel(listWheel);

        // Assert
        Assert.True(listWheel.IsHandled);
        Assert.True(tiles.ScrollPosition > 0);
        Assert.True(book.SingleComposer.GetScrollbar("scroll").CurrentYPosition > 0);
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
    public void RecipeBookOptionsTabRoutesBetweenControlsAndExitsInBothDirections()
    {
        // Arrange
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var composer = book.SingleComposer;
        var options = Assert.IsType<GuiElementRecipeBookOptions>(composer.GetElement("options"));
        // Resolve the prior focusable element through the composer because it can reserve non-focusable indices.
        GuiElement? expectedPrevious = null;
        for (int tabIndex = options.TabIndex - 1; tabIndex >= 0 && expectedPrevious == null; tabIndex--)
            if (composer.FocusElement(tabIndex)) expectedPrevious = composer.CurrentTabIndexElement;
        Assert.NotNull(expectedPrevious);
        Assert.NotSame(options, expectedPrevious);
        Assert.True(composer.FocusElement(options.TabIndex));
        var expectedNext = composer.FirstTabbableElement;
        options = f.OpenOptions(book);
        Assert.True(options.HasFocus);
        Assert.True(options.Checkbox.HasFocus);

        // Act
        PressTab();
        Assert.True(options.SizeInput.HasFocus);
        PressTab(shift: true);
        Assert.True(options.Checkbox.HasFocus);
        PressTab();
        Assert.True(options.SizeInput.HasFocus);
        PressTab();

        // Assert
        AssertFocusExited(expectedNext);
        Assert.False(options.IsOpened);
        Assert.False(options.Checkbox.HasFocus);
        Assert.False(options.SizeInput.HasFocus);

        // Reopening starts at the checkbox again; reverse traversal must leave at the prior outer control.
        options = f.OpenOptions(book);
        Assert.True(options.HasFocus);
        Assert.True(options.Checkbox.HasFocus);

        // Act
        PressTab();
        Assert.True(options.SizeInput.HasFocus);
        PressTab(shift: true);
        Assert.True(options.Checkbox.HasFocus);
        PressTab(shift: true);

        // Assert
        AssertFocusExited(expectedPrevious!);
        Assert.False(options.IsOpened);
        Assert.False(options.Checkbox.HasFocus);
        Assert.False(options.SizeInput.HasFocus);

        void PressTab(bool shift = false)
        {
            var args = new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = shift };
            book.OnKeyDown(args);
            Assert.True(args.Handled);
        }

        void AssertFocusExited(GuiElement expected)
        {
            var focused = book.SingleComposer.CurrentTabIndexElement;
            Assert.NotNull(focused);
            Assert.NotSame(options, focused);
            Assert.Same(expected, focused);
        }
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
