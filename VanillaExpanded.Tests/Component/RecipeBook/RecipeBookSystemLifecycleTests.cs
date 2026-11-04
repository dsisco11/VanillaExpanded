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
public sealed class RecipeBookSystemLifecycleTests
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
        // Arrange a world whose system was never started because the feature was disabled at load.
        using var scope = new NativeScope();
        using var f = new LifecycleFixture(false);
        using var parent = new ParentDialog(f.Api.Object);
        using var composer = (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { f.Api.Object, ElementBounds.Fixed(0, 0, 1920, 1080).WithEmptyParent(), "disabled-inventory" },
            null)!;
        Assert.False(f.Loaded);
        Assert.False(f.System.ShouldLoad(EnumAppSide.Server));
        Assert.Null(f.System.Catalogue);
        Assert.Null(f.System.IconCache);
        Assert.Equal(0, f.Builds);
        f.System.Attach(parent, composer);
        Assert.Null(composer.GetElement("vanillaexpanded-crafting-toggle"));
        f.Events.VerifyAdd(value => value.LevelFinalize += It.IsAny<Action>(), Times.Never);
        f.Events.VerifyAdd(value => value.LeaveWorld += It.IsAny<Action>(), Times.Never);

        // Act: enabling the setting mid-world cannot retroactively load or compose the system.
        VanillaExpandedModSystem.Config.EnableRecipeBook = true;
        f.FinalizeWorld();
        f.System.Attach(parent, composer);
        Assert.Equal(0, f.Builds);
        Assert.Null(f.System.Catalogue);
        Assert.Null(composer.GetElement("vanillaexpanded-crafting-toggle"));

        // Act: represent a world reload with a newly constructed system.
        using var reloaded = new LifecycleFixture();

        // Assert only the reloaded system reaches the world-finalize build step.
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
        // Arrange an enabled, open dialog; optionally queue restoration before disabling.
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        if (queuedRestore) f.Recompose();

        // Act: disable at runtime and drain any queued restoration work.
        f.SetEnabled(false);
        f.Lifecycle.Drain();

        // Assert disposal closes the dialog and releases world-owned services and toggle.
        Assert.False(menu.IsOpened());
        Assert.Empty(f.Menus);
        Assert.Null(f.Lifecycle.System.Catalogue);
        Assert.Null(f.Lifecycle.System.IconCache);
        f.AssertToggleHidden();

        // Act: re-enable in the same world and try its remaining lifecycle entry points.
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

        // Assert a fresh system after reload can initialize normally.
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
}
