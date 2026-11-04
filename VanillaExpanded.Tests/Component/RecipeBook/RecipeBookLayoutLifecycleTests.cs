using System.Reflection;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookLayoutLifecycleTests
{
    /// <summary>Verifies the first open lays out before refresh and 1,000 settled native render calls skip shell work.</summary>
    [Fact]
    public void InitialOpenAndThousandUnchangedFixedAndMovableFramesSkipLayoutWork()
    {
        // Arrange
        using var fixture = new MenuFixture(scale: 1.25f, includeRecipes: true);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(countBounds: true,
            createIconShader: fixture.CreateIconShader);

        // Act
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        Assert.True(book.IsOpened());
        Assert.Equal(1, calls.MeasurementCalls);
        Assert.Equal(1, calls.FixedCalls);
        Assert.Equal(0, calls.MovableCalls);
        Assert.Equal(1, calls.CompositionCalls);
        Assert.Equal(1, calls.RefreshCalls);
        Assert.Equal(new[] { "measure", "fixed", "compose", "refresh" }, calls.Operations);

        var render = Mock.Get(fixture.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        book.OnRenderGUI(0);
        Assert.Contains(render.Invocations,
            invocation => invocation.Method.Name == nameof(IRenderAPI.Render2DTexture));

        var root = calls.GetRootBounds(book);
        var child = calls.GetChildBounds(book);
        int rootCalculations = root.Calculations;
        int childCalculations = child.Calculations;
        int measurementCalls = calls.MeasurementCalls;
        int fixedCalls = calls.FixedCalls;
        int movableCalls = calls.MovableCalls;
        int compositionCalls = calls.CompositionCalls;

        // Assert
        for (int frame = 0; frame < 1_000; frame++) book.OnRenderGUI(0);

        Assert.Equal(measurementCalls, calls.MeasurementCalls);
        Assert.Equal(fixedCalls, calls.FixedCalls);
        Assert.Equal(movableCalls, calls.MovableCalls);
        Assert.Equal(compositionCalls, calls.CompositionCalls);
        Assert.Equal(rootCalculations, root.Calculations);
        Assert.Equal(childCalculations, child.Calculations);
        Assert.Same(root, book.SingleComposer.Bounds);

        // A mode transition gets one normal warm-up render before measuring settled Movable frames.
        fixture.SelectMovement(book, movable: true);
        book.OnRenderGUI(0);
        Assert.True(book.SingleComposer.GetTitleBar("titlebar").Movable);
        Assert.Equal(1, calls.FixedCalls);
        Assert.Equal(1, calls.MovableCalls);
        Assert.Equal(compositionCalls, calls.CompositionCalls);
        rootCalculations = root.Calculations;
        childCalculations = child.Calculations;
        measurementCalls = calls.MeasurementCalls;
        movableCalls = calls.MovableCalls;

        // Act and assert: native rendering remains real, while the shell lifecycle is idle for another 1,000 frames.
        for (int frame = 0; frame < 1_000; frame++) book.OnRenderGUI(0);

        Assert.Equal(measurementCalls, calls.MeasurementCalls);
        Assert.Equal(fixedCalls, calls.FixedCalls);
        Assert.Equal(movableCalls, calls.MovableCalls);
        Assert.Equal(compositionCalls, calls.CompositionCalls);
        Assert.Equal(rootCalculations, root.Calculations);
        Assert.Equal(childCalculations, child.Calculations);
    }

    /// <summary>Verifies tile-size and scale changes each remeasure and recompose once before the lifecycle settles.</summary>
    [Fact]
    public void ChangedTileSizeAndGuiScaleRecomposeOnceThenSettle()
    {
        // Arrange
        var savedSettings = new Dictionary<string, int>();
        using var fixture = new MenuFixture(savedIntSettings: savedSettings);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);

        // Act: change a validated setting, then independently change the live scale.
        var firstComposer = book.SingleComposer;
        savedSettings[RecipeBookSystem.TileSizeSetting] = 60;
        Assert.True(book.TryOpen());

        // Assert
        Assert.NotSame(firstComposer, book.SingleComposer);
        Assert.Equal(60, Assert.IsType<GuiElementRecipeBookTiles>(
            book.SingleComposer.GetElement("tiles")).TileSize);
        Assert.Equal(2, calls.MeasurementCalls);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(2, calls.CompositionCalls);

        firstComposer = book.SingleComposer;
        RuntimeEnv.GUIScale = 1.5f;
        Assert.True(book.TryOpen(withFocus: false));
        Assert.NotSame(firstComposer, book.SingleComposer);
        Assert.Equal(3, calls.MeasurementCalls);
        Assert.Equal(3, calls.FixedCalls);
        Assert.Equal(3, calls.CompositionCalls);

        int measurements = calls.MeasurementCalls;
        int placements = calls.FixedCalls;
        int compositions = calls.CompositionCalls;
        book.OnRenderGUI(0);
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(placements, calls.FixedCalls);
        Assert.Equal(compositions, calls.CompositionCalls);
    }

    /// <summary>Verifies Fixed parent translation preserves the composed controls while Follow updates only the root.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    public void FixedParentTranslationRetainsComposerAndRecalculatesOnlyItsRoot(float scale)
    {
        // Arrange
        using var fixture = new MenuFixture(scale: scale, includeRecipes: true);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(countBounds: true,
            createIconShader: fixture.CreateIconShader);
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        book.OnRenderGUI(0);
        var composer = book.SingleComposer;
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
        var search = composer.GetTextInput("search");
        Assert.True(composer.FocusElement(search.TabIndex));
        var view = tiles.CaptureView();
        var root = calls.GetRootBounds(book);
        var child = calls.GetChildBounds(book);
        int rootCalculations = root.Calculations;
        int childCalculations = child.Calculations;
        var parent = fixture.Parent.Composers["maininventory"].Bounds;

        // Act
        parent.fixedX += 20;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        book.OnRenderGUI(0);

        // Assert
        Assert.Same(composer, book.SingleComposer);
        Assert.Same(tiles, book.SingleComposer.GetElement("tiles"));
        Assert.True(search.HasFocus);
        Assert.Equal(view, tiles.CaptureView());
        Assert.Equal(rootCalculations + 1, root.Calculations);
        Assert.Equal(childCalculations, child.Calculations);
        Assert.Equal(1, calls.MeasurementCalls);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(1, calls.CompositionCalls);
        Assert.Equal(parent.absX + parent.OuterWidth + 2, composer.Bounds.absX, 4);
    }

    /// <summary>Verifies parent-space and window changes invalidate placement, while a same-sized window replacement follows natively.</summary>
    [Fact]
    public void FixedAvailableSpaceAndWindowChangesRecomposeOnlyWhenDimensionsChange()
    {
        // Arrange
        using var fixture = new MenuFixture(scale: 1.25f);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(countBounds: true);
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        var initialComposer = book.SingleComposer;
        double minimumWidth = ReadDouble(book, "minimumWidth");
        double initialWidth = initialComposer.Bounds.fixedWidth;
        Assert.True(initialWidth > minimumWidth,
            "The fixture must start wider than its measured minimum to exercise placement-limited width.");

        // Act: move the inventory so the first docking region has a smaller, still-valid width.
        double scale = GuiElement.scaled(1);
        var parent = fixture.Parent.Composers["maininventory"].Bounds;
        var originalWindow = fixture.Lifecycle.Api.Object.Gui.WindowBounds;
        double availableWidth = (minimumWidth + initialWidth) / 2;
        parent.fixedX = originalWindow.InnerWidth / scale - parent.OuterWidth / scale
            - availableWidth - 2 / scale;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        book.OnRenderGUI(0);

        // Assert: the smaller placement is valid and recomposition follows the changed result dimensions.
        var limitedComposer = book.SingleComposer;
        Assert.NotSame(initialComposer, limitedComposer);
        Assert.Equal(availableWidth, limitedComposer.Bounds.fixedWidth, 3);
        Assert.True(limitedComposer.Bounds.absX >= parent.absX + parent.OuterWidth);
        Assert.True(limitedComposer.Bounds.absX + limitedComposer.Bounds.OuterWidth
            <= originalWindow.absX + originalWindow.InnerWidth);
        Assert.Equal(1, calls.MeasurementCalls);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(2, calls.CompositionCalls);

        // Replacing the window object with an equal-sized root changes the native parent identity, not layout size.
        var replacementWindow = ElementBounds.Fixed(0, 0,
            originalWindow.fixedWidth, originalWindow.fixedHeight).WithEmptyParent();
        replacementWindow.CalcWorldBounds();
        Mock.Get(fixture.Lifecycle.Api.Object.Gui)
            .SetupGet(api => api.WindowBounds).Returns(replacementWindow);
        var root = calls.GetRootBounds(book);
        var child = calls.GetChildBounds(book);
        int rootCalculations = root.Calculations;
        int childCalculations = child.Calculations;
        limitedComposer = book.SingleComposer;

        // Act
        book.OnRenderGUI(0);

        // Assert
        Assert.Same(limitedComposer, book.SingleComposer);
        Assert.Same(replacementWindow, limitedComposer.Bounds.ParentBounds);
        Assert.Equal(rootCalculations + 1, root.Calculations);
        Assert.Equal(childCalculations, child.Calculations);
        Assert.Equal(3, calls.FixedCalls);
        Assert.Equal(2, calls.CompositionCalls);

        // A window resize changes the fitting width and triggers one conditional recomposition.
        replacementWindow.fixedWidth -= 100;
        replacementWindow.MarkDirtyRecursive();
        replacementWindow.CalcWorldBounds();
        book.OnRenderGUI(0);
        Assert.NotSame(limitedComposer, book.SingleComposer);
        Assert.Equal(4, calls.FixedCalls);
        Assert.Equal(3, calls.CompositionCalls);
        Assert.Equal(1, calls.MeasurementCalls);
    }

    /// <summary>Verifies native Movable dragging and clamping reuse the size placement until the user returns to Fixed.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    public void MovableDragAndClampReusePlacementThenFixedModeRedocks(float scale)
    {
        // Arrange
        using var fixture = new MenuFixture(scale: scale);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(countBounds: true);
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        book.OnRenderGUI(0);
        var composer = book.SingleComposer;
        var child = calls.GetChildBounds(book);

        // The native title-bar mode transition is independent; the next render caches Movable size placement.
        fixture.SelectMovement(book, movable: true);
        book.OnRenderGUI(0);
        Assert.Same(composer, book.SingleComposer);
        Assert.True(composer.GetTitleBar("titlebar").Movable);
        Assert.Equal(1, calls.FixedCalls);
        Assert.Equal(1, calls.MovableCalls);
        Assert.Equal(1, calls.CompositionCalls);

        var titlebar = composer.GetTitleBar("titlebar");
        int startX = (int)(titlebar.Bounds.absX + GuiElement.scaled(30));
        int startY = (int)(titlebar.Bounds.absY + GuiElement.scaled(12));
        var down = new MouseEvent(startX, startY, EnumMouseButton.Left, 0);
        book.OnMouseDown(down);
        Assert.True(down.Handled);
        book.OnMouseMove(new MouseEvent(-500, -400, EnumMouseButton.None, 0));
        book.OnMouseUp(new MouseEvent(-500, -400, EnumMouseButton.Left, 0));
        Assert.True(composer.Bounds.fixedX < 0);
        int childCalculationsAfterNativeDrag = child.Calculations;
        var root = calls.GetRootBounds(book);
        int rootCalculationsAfterNativeDrag = root.Calculations;

        // Act
        book.OnRenderGUI(0);

        // Assert
        Assert.Same(composer, book.SingleComposer);
        Assert.Equal(0, composer.Bounds.fixedX, 4);
        Assert.Equal(0, composer.Bounds.fixedY, 4);
        Assert.True(composer.Bounds.absX + composer.Bounds.OuterWidth
            <= fixture.Lifecycle.Api.Object.Gui.WindowBounds.absX
                + fixture.Lifecycle.Api.Object.Gui.WindowBounds.InnerWidth + .001);
        Assert.Equal(rootCalculationsAfterNativeDrag + 1, root.Calculations);
        Assert.Equal(childCalculationsAfterNativeDrag, child.Calculations);
        Assert.Equal(1, calls.MovableCalls);
        Assert.Equal(1, calls.CompositionCalls);

        // Act: switching back to Fixed causes one docking search without discarding the composed shell.
        fixture.SelectMovement(book, movable: false);
        book.OnRenderGUI(0);

        // Assert
        Assert.False(book.SingleComposer.GetTitleBar("titlebar").Movable);
        Assert.Same(composer, book.SingleComposer);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(1, calls.MovableCalls);
        Assert.Equal(1, calls.CompositionCalls);
    }

    /// <summary>Verifies manual Movable size changes recompose even when the new placement matches the live root.</summary>
    [Fact]
    public void MovableManualSizeChangeRecomposesOnceWhenPlacementMatchesLiveBoundsThenSettles()
    {
        // Arrange
        using var fixture = new MenuFixture(scale: 1.25f);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        book.OnRenderGUI(0);
        fixture.SelectMovement(book, movable: true);
        book.OnRenderGUI(0);

        var originalComposer = book.SingleComposer;
        var originalSearch = originalComposer.GetTextInput("search");
        var window = fixture.Lifecycle.Api.Object.Gui.WindowBounds;
        double scale = GuiElement.scaled(1);
        double targetWidth = Math.Max(originalComposer.Bounds.fixedWidth + 40, ReadDouble(book, "minimumWidth") + 10);
        double targetHeight = Math.Max(originalComposer.Bounds.fixedHeight + 40, ReadDouble(book, "minimumHeight") + 10);

        // Changing the window invalidates placement; the manual dimensions remain valid within its new geometry.
        window.fixedWidth -= 200;
        window.fixedHeight -= 100;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        Assert.True(window.InnerWidth / scale > targetWidth);
        Assert.True(window.InnerHeight / scale > targetHeight);
        originalComposer.Bounds.fixedWidth = targetWidth;
        originalComposer.Bounds.fixedHeight = targetHeight;
        int measurements = calls.MeasurementCalls;
        int fixedPlacements = calls.FixedCalls;
        int movablePlacements = calls.MovableCalls;
        int compositions = calls.CompositionCalls;

        // Act
        book.OnRenderGUI(0);

        // Assert
        var resizedComposer = book.SingleComposer;
        Assert.NotSame(originalComposer, resizedComposer);
        Assert.Equal(targetWidth, resizedComposer.Bounds.fixedWidth, 4);
        Assert.Equal(targetHeight, resizedComposer.Bounds.fixedHeight, 4);
        Assert.True(resizedComposer.GetTextInput("search").Bounds.fixedWidth > originalSearch.Bounds.fixedWidth);
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(fixedPlacements, calls.FixedCalls);
        Assert.Equal(movablePlacements + 1, calls.MovableCalls);
        Assert.Equal(compositions + 1, calls.CompositionCalls);

        // Act: the recomposed size and placement are now the settled cache inputs.
        book.OnRenderGUI(0);

        // Assert
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(fixedPlacements, calls.FixedCalls);
        Assert.Equal(movablePlacements + 1, calls.MovableCalls);
        Assert.Equal(compositions + 1, calls.CompositionCalls);
    }

    /// <summary>Verifies an oversized Movable root is repaired to the available size even when that is the cached size.</summary>
    [Fact]
    public void OversizedMovableRootIsRecomposedAtCachedSizeAfterWindowClamp()
    {
        // Arrange
        using var fixture = new MenuFixture(scale: 1.25f);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(countBounds: true);
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        book.OnRenderGUI(0);
        fixture.SelectMovement(book, movable: true);
        book.OnRenderGUI(0);

        var originalComposer = book.SingleComposer;
        double cachedWidth = originalComposer.Bounds.fixedWidth;
        double cachedHeight = originalComposer.Bounds.fixedHeight;
        double scale = GuiElement.scaled(1);
        var window = fixture.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = cachedWidth;
        window.fixedHeight = cachedHeight;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        double availableWidth = window.InnerWidth / scale;
        double availableHeight = window.InnerHeight / scale;
        Assert.Equal(cachedWidth, availableWidth, 4);
        Assert.Equal(cachedHeight, availableHeight, 4);
        Assert.True(availableWidth >= ReadDouble(book, "minimumWidth"));
        Assert.True(availableHeight >= ReadDouble(book, "minimumHeight"));

        // The live root is now larger than both the window and its last composed dimensions.
        originalComposer.Bounds.fixedWidth = cachedWidth * 2;
        originalComposer.Bounds.fixedHeight = cachedHeight * 2;
        int measurements = calls.MeasurementCalls;
        int fixedPlacements = calls.FixedCalls;
        int movablePlacements = calls.MovableCalls;
        int compositions = calls.CompositionCalls;

        // Act
        book.OnRenderGUI(0);

        // Assert: repair the root despite the placement returning to the previously composed size.
        Assert.NotSame(originalComposer, book.SingleComposer);
        var repairedRoot = calls.GetRootBounds(book);
        Assert.Equal(availableWidth, repairedRoot.fixedWidth, 4);
        Assert.Equal(availableHeight, repairedRoot.fixedHeight, 4);
        Assert.True(repairedRoot.fixedWidth <= window.InnerWidth / scale);
        Assert.True(repairedRoot.fixedHeight <= window.InnerHeight / scale);
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(fixedPlacements, calls.FixedCalls);
        Assert.Equal(movablePlacements + 1, calls.MovableCalls);
        Assert.Equal(compositions + 1, calls.CompositionCalls);

        // Act: the repaired bounds settle without another size placement or recomposition.
        book.OnRenderGUI(0);

        // Assert
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(fixedPlacements, calls.FixedCalls);
        Assert.Equal(movablePlacements + 1, calls.MovableCalls);
        Assert.Equal(compositions + 1, calls.CompositionCalls);
    }

    /// <summary>Verifies search, category, sort, craftability, and inventory refresh update content without invalidating the shell.</summary>
    [Fact]
    public void RecipeContentChangesReuseTheMeasuredAndComposedShell()
    {
        // Arrange
        using var fixture = new MenuFixture(includeRecipes: true);
        var recipe = Assert.Single(fixture.Lifecycle.Recipes);
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        fixture.Lifecycle.FinalizeWorld();
        fixture.Recompose();
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter(createIconShader: fixture.CreateIconShader);
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        var composer = book.SingleComposer;
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(composer.GetElement("tiles"));
        var session = GetSession(book);

        // Act: update presentation and filtering inputs, then refresh from a changed crafting grid.
        var tabs = Assert.IsType<GuiElementIconTabs>(composer.GetElement("categories"));
        tabs.SetValue(Array.FindIndex(tabs.tabs, tab => tab.Name == "tools"));
        composer.GetTextInput("search").SetValue("output");
        session.Sort.Select("name");
        fixture.ToggleCraftable(book);
        Assert.Empty(session.Rows);

        var grid = Assert.Single(((ClientPlayerInventoryManager)fixture.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        grid[0].Itemstack = recipe.ResolvedIngredients![0]!.ResolvedItemStack!.Clone();
        book.NativeGridDirty();
        fixture.Lifecycle.Drain();
        book.OnRenderGUI(0);

        // Assert
        Assert.Equal("tools", session.Category);
        Assert.Equal("name", session.Sort.Selected.Id);
        Assert.True(session.CraftableOnly.Enabled);
        Assert.Single(session.Rows);
        Assert.True(session.Rows[0].Evaluation.CanCraft);
        Assert.Same(composer, book.SingleComposer);
        Assert.Same(tiles, book.SingleComposer.GetElement("tiles"));
        Assert.Equal(1, calls.MeasurementCalls);
        Assert.Equal(1, calls.FixedCalls);
        Assert.Equal(1, calls.CompositionCalls);
    }

    /// <summary>Verifies attachment failures close before layout work and release the dialog exactly once.</summary>
    [Theory]
    [InlineData("replace")]
    [InlineData("craftinggrid")]
    [InlineData("outputslot")]
    public void InvalidAttachmentClosesBeforeLayoutLifecycle(string invalidation)
    {
        // Arrange
        using var fixture = new MenuFixture();
        fixture.Parent.TryOpen();
        int textureBaseline = fixture.TextureCount;
        using var calls = new RecipeBookLayoutCallCounter();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        int disposed = 0;
        book.Disposed += () => disposed++;
        int measurements = calls.MeasurementCalls;
        int placements = calls.FixedCalls;
        int compositions = calls.CompositionCalls;
        GuiComposer? replacedComposer = null;

        // Act
        if (invalidation == "replace")
        {
            int beforeReplacement = fixture.TextureCount;
            replacedComposer = fixture.Parent.Composers["maininventory"];
            fixture.Parent.Composers["maininventory"] = GuiComposer.CreateEmpty(fixture.Lifecycle.Api.Object);
            textureBaseline += fixture.TextureCount - beforeReplacement;
        }
        else
        {
            var parentComposer = fixture.Parent.Composers["maininventory"];
            var elements = (IDictionary<string, GuiElement>)typeof(GuiComposer)
                .GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(parentComposer)!;
            Assert.True(elements.Remove(invalidation));
        }
        int? displacedComposerTexturesReleased = null;
        try
        {
            book.OnRenderGUI(0);
        }
        finally
        {
            // Parent.Dispose owns only the replacement now; release the displaced composer explicitly.
            if (replacedComposer != null)
            {
                int texturesBeforeDisposal = fixture.TextureCount;
                replacedComposer.Dispose();
                displacedComposerTexturesReleased = texturesBeforeDisposal - fixture.TextureCount;
            }
        }

        // Assert
        if (displacedComposerTexturesReleased is int releasedTextures)
        {
            Assert.True(releasedTextures > 0, "The displaced parent composer must release its tracked textures.");
            textureBaseline -= releasedTextures;
        }
        Assert.DoesNotContain(book, fixture.Menus);
        Assert.Equal(1, disposed);
        Assert.Equal(textureBaseline, fixture.TextureCount);
        Assert.False(book.TryOpen());
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(placements, calls.FixedCalls);
        Assert.Equal(compositions, calls.CompositionCalls);
    }

    /// <summary>Verifies insufficient space closes cleanly and a zero render surface skips attachment and layout work.</summary>
    [Fact]
    public void ZeroSizedSurfaceSkipsLifecycleAndInsufficientSpaceClosesWithoutComposing()
    {
        // Arrange
        using var fixture = new MenuFixture();
        fixture.Parent.TryOpen();
        int textureBaseline = fixture.TextureCount;
        using var calls = new RecipeBookLayoutCallCounter();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        int disposed = 0;
        book.Disposed += () => disposed++;
        var composer = fixture.Parent.Composers["maininventory"];
        fixture.Parent.Composers.Remove("maininventory");
        var render = Mock.Get(fixture.Lifecycle.Api.Object.Render);
        render.SetupGet(api => api.FrameWidth).Returns(0);
        render.Invocations.Clear();
        int measurements = calls.MeasurementCalls;
        int placements = calls.FixedCalls;
        int compositions = calls.CompositionCalls;

        try
        {
            // Act
            book.OnRenderGUI(0);

            // Assert
            Assert.Equal(0, disposed);
            Assert.DoesNotContain(render.Invocations, invocation =>
                invocation.Method.Name is nameof(IRenderAPI.Render2DTexture)
                    or nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
            Assert.Equal(measurements, calls.MeasurementCalls);
            Assert.Equal(placements, calls.FixedCalls);
            Assert.Equal(compositions, calls.CompositionCalls);
        }
        finally
        {
            // Parent.Dispose owns the attached composer, so restore it even when the zero-surface assertions fail.
            fixture.Parent.Composers["maininventory"] = composer;
        }

        // Make the nonzero window too small for the cached minimum after restoring the parent attachment.
        render.SetupGet(api => api.FrameWidth).Returns(() => fixture.Lifecycle.Api.Object.Gui.WindowBounds.OuterWidthInt);
        var window = fixture.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = 100;
        window.fixedHeight = 100;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        book.OnRenderGUI(0);

        // Assert
        Assert.Equal(1, disposed);
        Assert.DoesNotContain(book, fixture.Menus);
        Assert.Equal(textureBaseline, fixture.TextureCount);
        Assert.Equal(measurements, calls.MeasurementCalls);
        Assert.Equal(placements + 1, calls.FixedCalls);
        Assert.Equal(compositions, calls.CompositionCalls);
        fixture.Lifecycle.Api.Verify(api => api.TriggerIngameError(book,
            "recipebook", "vanillaexpanded:recipebook-screen-space"), Times.Once);
        Assert.False(book.TryOpen());
    }

    /// <summary>Verifies an options popup retains rejected numeric text and focus across layout recomposition.</summary>
    [Fact]
    public void OptionsPopupRetainsRejectedSizeFeedbackAcrossRecomposition()
    {
        // Arrange
        var savedSettings = new Dictionary<string, int>();
        using var fixture = new MenuFixture(savedIntSettings: savedSettings);
        fixture.Parent.TryOpen();
        using var calls = new RecipeBookLayoutCallCounter();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        var options = fixture.OpenOptions(book);
        Mock.Get(fixture.Lifecycle.Api.Object.Input)
            .SetupGet(input => input.KeyboardKeyStateRaw).Returns(new bool[256]);
        var inputBounds = options.SizeInput.Bounds;
        int x = (int)(inputBounds.absX + inputBounds.OuterWidth / 2);
        int y = (int)(inputBounds.absY + inputBounds.OuterHeight / 2);
        fixture.SetMouse(x, y);
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        book.OnMouseDown(down);
        Assert.True(down.Handled);
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        var sizeInput = Assert.IsType<GuiElementRecipeBookOptions.GuiElementRecipeBookSizeInput>(options.SizeInput);
        sizeInput.LoadValue(["0"]);
        sizeInput.RejectedEdit = true;
        Assert.True(options.RejectedSizeEdit);
        Assert.True(options.SizeInput.HasFocus);
        var oldComposer = book.SingleComposer;

        // Act
        savedSettings[RecipeBookSystem.TileSizeSetting] = 60;
        Assert.True(book.TryOpen());

        // Assert
        Assert.NotSame(oldComposer, book.SingleComposer);
        var restored = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        Assert.True(restored.IsOpened);
        Assert.True(restored.SizeInput.HasFocus);
        Assert.True(restored.RejectedSizeEdit);
        Assert.Equal("0", restored.SizeInput.GetText());
        Assert.True(Assert.IsType<GuiElementRecipeBookOptions.GuiElementRecipeBookSizeInput>(
            restored.SizeInput).RejectedEdit);
        Assert.Equal(2, calls.MeasurementCalls);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(2, calls.CompositionCalls);
    }

    /// <summary>Reads a private numeric field used to derive an independent cache-input scenario.</summary>
    private static double ReadDouble(GuiDialogRecipeBook book, string name) =>
        (double)typeof(GuiDialogRecipeBook).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;

    /// <summary>Gets the dialog session so content changes can be asserted independently from shell-layout state.</summary>
    private static RecipeBookSession GetSession(GuiDialogRecipeBook book) =>
        (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
}
