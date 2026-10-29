using System.Reflection;
using Cairo;
using Moq;
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
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Input).Returns(Mock.Of<IInputAPI>());
        var clips = new Stack<ElementBounds>();
        render.Setup(value => value.PushScissor(It.IsAny<ElementBounds>(), It.IsAny<bool>()))
            .Callback<ElementBounds, bool>((bounds, stacking) =>
            {
                Assert.True(stacking);
                Assert.Equal(46 * scale, bounds.InnerWidth, 4);
                Assert.Equal(46 * scale, bounds.InnerHeight, 4);
                clips.Push(bounds);
            });
        render.Setup(value => value.PopScissor()).Callback(() =>
        {
            Assert.Single(clips);
            clips.Pop();
        });
        render.Setup(value => value.RenderItemstackToGui(It.IsAny<ItemSlot>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<int>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Callback<ItemSlot, double, double, double, float, int, bool, bool, bool>(
                (slot, x, y, z, size, color, shading, rotate, showStackSize) =>
                {
                    var bounds = Assert.Single(clips);
                    Assert.Equal(bounds.renderX + bounds.InnerWidth / 2, x, 4);
                    Assert.Equal(bounds.renderY + bounds.InnerHeight / 2, y, 4);
                    Assert.Equal(46 * scale * GuiElementPassiveItemSlot.unscaledItemSize
                        / GuiElementPassiveItemSlot.unscaledSlotSize, size, 4);
                    Assert.True(showStackSize);
                });
        gui.SetupGet(value => value.TextTexture).Returns(new TextTextureUtil(api.Object));
        int nextTexture = 1;
        var liveTextures = new HashSet<int>();
        gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(),
            ref It.Ref<LoadedTexture>.IsAny)).Callback(new Upload((ImageSurface image, bool linear, ref LoadedTexture texture) =>
        {
            if (texture.TextureId == 0)
            {
                texture.TextureId = nextTexture++;
                liveTextures.Add(texture.TextureId);
            }
            texture.Width = image.Width;
            texture.Height = image.Height;
        }));
        gui.Setup(value => value.DeleteTexture(It.IsAny<int>())).Callback<int>(id => Assert.True(liveTextures.Remove(id)));
        var viewport = ElementBounds.Fixed(0, 0, 96, 96).WithEmptyParent();
        viewport.CalcWorldBounds();
        using var surface = new ImageSurface(Format.Argb32, 96, 144);
        using var ctx = new Context(surface);
        for (int cycle = 0; cycle < 5; cycle++)
        {
            int inspected = 0;
            using var tiles = new GuiElementCraftingTiles(api.Object,
                ElementBounds.Fixed(0, 0, 96, 144).WithParent(viewport), viewport, session,
                _ => inspected++, (_, _) => { });
            tiles.BeforeCalcBounds();
            tiles.Bounds.CalcWorldBounds();
            tiles.ComposeElements(ctx, surface);
            Assert.Equal(28, liveTextures.Count);
            for (int frame = 0; frame < 3; frame++)
            {
                render.Invocations.Clear();
                tiles.RenderInteractiveElements(0);
                Assert.Equal(4, render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderItemstackToGui)));
                Assert.Empty(clips);
            }
            tiles.Scroll(24 * scale);
            render.Invocations.Clear();
            tiles.RenderInteractiveElements(0);
            Assert.Equal(6, render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderItemstackToGui)));
            Assert.Empty(clips);
            Assert.Equal(1, captures);
            tiles.Dispose();
            Assert.Empty(liveTextures);
            int afterClose = inspected;
            session.Search("");
            Assert.Equal(afterClose, inspected);
        }
    }

    private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);

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
                f.System.Catalogue!, f.F.Allocator);
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

    private sealed class ParentDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => "";
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
        internal int Builds;

        internal LifecycleFixture()
        {
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
            System.StartClientSide(Api.Object);
        }

        internal void FinalizeWorld() => Events.Raise(value => value.LevelFinalize += null);
        internal void Drain() { while (Queue.TryDequeue(out var action)) action(); }

        public void Dispose() => System.Dispose();
    }
}
