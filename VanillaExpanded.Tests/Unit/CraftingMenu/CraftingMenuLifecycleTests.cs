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
        Assert.False(f.ToggleButton.Visible);
        Assert.False(f.ToggleButton.Enabled);
        f.SetEnabled(true);
        f.Lifecycle.FinalizeWorld();
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.False(f.ToggleButton.Visible);
        Assert.False(f.ToggleButton.Enabled);
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
        f.Parent.TryClose();
        f.Parent.TryOpen();
        Assert.True(Assert.Single(f.Menus).IsOpened());
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("escape")]
    [InlineData("titlebar")]
    public void ExplicitlyClosingTheMenuKeepsItClosedWhenTheParentReopens(string action)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        if (action == "toggle") f.Toggle();
        else if (action == "escape") Assert.True(menu.OnEscapePressed());
        else f.ClickCloseButton(menu);
        Assert.Empty(f.Menus);
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
        f.Lifecycle.Drain();
        var restored = Assert.Single(f.Menus);
        Assert.NotSame(first, restored);
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
        private readonly CraftingRenderFixture graphics = new();
        internal readonly LifecycleFixture Lifecycle;
        internal readonly ParentDialog Parent;
        private readonly List<GuiDialog> loaded = new();
        private readonly List<GuiComposer> composers = new();
        private readonly string originalLocale = Lang.CurrentLocale;
        private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        internal IEnumerable<GuiDialogCraftingMenu> Menus => loaded.OfType<GuiDialogCraftingMenu>();
        internal GuiElementTextButton ToggleButton =>
            (GuiElementTextButton)Parent.Composers["maininventory"].GetElement("vanillaexpanded-crafting-toggle");

        internal MenuFixture(bool enabled = true)
        {
            Lifecycle = new LifecycleFixture(enabled);
            var translations = new Mock<ITranslationService>();
            translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key);
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
            api.SetupGet(value => value.Assets).Returns(assets.Object);
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
            var button = Parent.Composers["maininventory"].GetElement("vanillaexpanded-crafting-toggle");
            var args = new MouseEvent((int)(button.Bounds.absX + 5), (int)(button.Bounds.absY + 5),
                EnumMouseButton.Left, 0);
            button.OnMouseDownOnElement(Lifecycle.Api.Object, args);
            button.OnMouseUpOnElement(Lifecycle.Api.Object, args);
            Assert.True(args.Handled);
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
