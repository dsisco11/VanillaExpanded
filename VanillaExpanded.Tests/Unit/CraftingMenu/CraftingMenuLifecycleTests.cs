using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.ModSystems;
using VanillaExpanded.CraftingMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
[Collection("Crafting native GUI")]
public sealed class CraftingMenuLifecycleTests
{
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
        double counterWidth = Math.Ceiling(CairoFont.WhiteSmallText().GetTextExtents("1/1*").Width / scale + 1);
        double footerWidth = new[] { "previous", "auto", "next" }
            .Sum(key => originalComposer.GetElement(key).Bounds.OuterWidth / scale) + 6 + 4 + counterWidth + 16;
        double titleWidth = GuiStyle.ElementToDialogPadding + title.Font.GetTextExtents(title.GetText()).Width / scale
            + 8 + 2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20;
        double minimumWidth = Math.Ceiling(Math.Max(footerWidth, titleWidth));
        double minimumHeight = Math.Ceiling(GuiStyle.TitleBarHeight + 8 + search.Bounds.OuterHeight / scale
            + 8 + GuiElementCraftingTiles.TileSize + 10
            + originalComposer.GetElement("auto").Bounds.OuterHeight / scale + 8);
        search.SetValue("preserved search");
        f.ResizePanelSpace(minimumWidth, minimumHeight);
        menu.TryOpen();
        Assert.NotSame(originalComposer, menu.SingleComposer);
        Assert.Equal(minimumWidth, menu.SingleComposer.Bounds.fixedWidth, 4);
        Assert.Equal(minimumHeight, menu.SingleComposer.Bounds.fixedHeight, 4);
        Assert.Equal("preserved search", menu.SingleComposer.GetTextInput("search").GetText());
        AssertLayout(menu);
        foreach (string key in new[] { "previous", "auto", "next" }) f.Click(menu.SingleComposer.GetElement(key));
        f.ResizePanelSpace(Math.Max(320, minimumWidth), 420);
        menu.TryOpen();
        Assert.Equal("preserved search", menu.SingleComposer.GetTextInput("search").GetText());
        AssertLayout(menu);

        static void AssertLayout(GuiDialogCraftingMenu menu)
        {
            var composer = menu.SingleComposer;
            var panel = composer.Bounds;
            var title = composer.GetTitleBar("titlebar");
            var search = composer.GetTextInput("search").Bounds;
            var viewport = composer.GetElement("tiles").InsideClipBounds;
            var scroll = composer.GetScrollbar("scroll").Bounds;
            var empty = composer.GetDynamicText("empty").Bounds;
            var previous = composer.GetElement("previous").Bounds;
            var auto = composer.GetElement("auto").Bounds;
            var next = composer.GetElement("next").Bounds;
            var counter = composer.GetDynamicText("alternative").Bounds;
            double scale = GuiElement.scaled(1);
            Assert.True(title.Bounds.absY + title.Bounds.OuterHeight + 8 * scale <= search.absY + .001);
            Assert.True(search.absY + search.OuterHeight + 8 * scale <= viewport.absY + .001);
            Assert.True(viewport.InnerHeight + .001 >= GuiElementCraftingTiles.TileSize * scale);
            Assert.True(viewport.absX + viewport.OuterWidth + 4 * scale <= scroll.absX + .001);
            Assert.Equal(viewport.absY, scroll.absY, 4);
            Assert.Equal(viewport.OuterHeight, scroll.OuterHeight, 4);
            Assert.True(viewport.absY + viewport.OuterHeight + 10 * scale <= previous.absY + .001);
            Assert.True(previous.absX + previous.OuterWidth + 3 * scale <= auto.absX + .001);
            Assert.True(auto.absX + auto.OuterWidth + 3 * scale <= next.absX + .001);
            Assert.True(next.absX + next.OuterWidth + 4 * scale <= counter.absX + .001);
            double titleTextRight = title.Bounds.absX + GuiElement.scaled(GuiStyle.ElementToDialogPadding)
                + title.Font.GetTextExtents(title.GetText()).Width;
            double titleMenuLeft = title.Bounds.absX + title.Bounds.OuterWidth
                - GuiElement.scaled(2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20);
            Assert.True(titleTextRight + 8 * scale <= titleMenuLeft + .001);
            foreach (var bounds in new[] { search, viewport, scroll, previous, auto, next, counter })
            {
                Assert.True(bounds.absX >= panel.absX + 8 * scale - .001);
                Assert.True(bounds.absX + bounds.OuterWidth <= panel.absX + panel.InnerWidth - 8 * scale + .001);
                Assert.True(bounds.absY + bounds.OuterHeight <= panel.absY + panel.InnerHeight - 8 * scale + .001);
            }
            Assert.True(empty.absX >= viewport.absX);
            Assert.True(empty.absY >= viewport.absY);
            Assert.True(empty.absX + empty.OuterWidth <= viewport.absX + viewport.InnerWidth + .001);
            Assert.True(empty.absY + empty.OuterHeight <= viewport.absY + viewport.InnerHeight + .001);
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
    [InlineData("escape")]
    [InlineData("titlebar")]
    public void ExplicitlyClosingTheMenuKeepsItClosedWhenTheParentReopens(string action)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        if (action == "escape") Assert.True(menu.OnEscapePressed());
        else f.ClickCloseButton(menu);
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

        internal MenuFixture(bool enabled = true, float scale = 1, bool longLabels = false)
        {
            graphics = new CraftingRenderFixture(scale);
            Lifecycle = new LifecycleFixture(enabled);
            var translations = new Mock<ITranslationService>();
            translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key switch
                {
                    "vanillaexpanded:craftingmenu-title" => longLabels ? "Available crafting recipes" : "Crafting recipes",
                    "vanillaexpanded:craftingmenu-auto" => longLabels ? "Automatic selection" : "Auto",
                    "vanillaexpanded:craftingmenu-empty" => "No matching recipes.",
                    "vanillaexpanded:craftingmenu-toggle" => "Recipes",
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
            var manager = (ClientPlayerInventoryManager)Lifecycle.F.Player.InventoryManager;
            manager.Inventories.Clear();
            var grid = new InventoryCraftingGrid("craftinggrid-test", null!) { Api = Lifecycle.F.Api.Object };
            grid.InvNetworkUtil.Api = Lifecycle.F.Api.Object;
            grid.Open(Lifecycle.F.Player);
            manager.Inventories.Add(grid.InventoryID, grid);
            Lifecycle.Recipes.Clear();
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

        internal void Click(GuiElement button)
        {
            var args = new MouseEvent((int)(button.Bounds.absX + 5), (int)(button.Bounds.absY + 5),
                EnumMouseButton.Left, 0);
            button.OnMouseDownOnElement(Lifecycle.Api.Object, args);
            button.OnMouseUpOnElement(Lifecycle.Api.Object, args);
            Assert.True(args.Handled);
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
        public override string ToggleKeyCombinationCode => "";
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
