using Cairo;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookIconRendererTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookIconCacheTests
{
    [Fact]
    public void ScrollingRetainsRecentPreviewsAndReloadsRebuildOwnedResources()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 30)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        for (int i = 0; i < 10; i++)
        {
            tiles.Scroll(i * 50);
            graphics.Warm(tiles, 3);
            Assert.Equal((i + 1) * 3, graphics.Allocations.Count);
        }
        int bakes = graphics.Bakes.Count;
        tiles.Scroll(0);
        graphics.Warm(tiles, 3);
        Assert.Equal(bakes, graphics.Bakes.Count);
        var firstProgram = Assert.Single(graphics.Programs);
        graphics.Events.Raise(value => value.ReloadTextures += null);
        Assert.Empty(graphics.Allocations);
        graphics.Warm(tiles, 3);
        Assert.Equal(bakes + 3, graphics.Bakes.Count);
        graphics.Events.Raise(value => value.ReloadShader += null);
        firstProgram.Verify(value => value.Dispose(), Times.Once);
        Assert.Equal(2, graphics.Programs.Count);
        int afterReload = graphics.Bakes.Count;
        tiles.RenderInteractiveElements(0);
        Assert.Equal(afterReload, graphics.Bakes.Count);
        tiles.Dispose();
        tiles.Dispose();
        Assert.Equal(3, graphics.Allocations.Count);
        graphics.Cache.Dispose();
        Assert.Empty(graphics.Allocations);
        graphics.Programs[1].Verify(value => value.Dispose(), Times.Once);
        Assert.True(Assert.Single(graphics.Meshes).Object.Disposed);
        graphics.Events.VerifyRemove(value => value.ReloadTextures -= It.IsAny<Action>(), Times.Exactly(2));
        graphics.Events.VerifyRemove(value => value.ReloadShader -= It.IsAny<ActionBoolReturn>(), Times.Once);
    }

    [Fact]
    public void DeferredPreviewsSurviveMenuClosureAndAreReleasedAtWorldExit()
    {
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = false };
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 9)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        var tiles = CreateTiles(graphics, session);
        graphics.Frame(tiles);
        var program = Assert.Single(graphics.Programs);
        program.VerifySet(value => value.HasIcon = false, Times.Exactly(3));
        program.VerifySet(value => value.IconTexture = It.IsAny<int>(), Times.Never);
        program.VerifySet(value => value.TextureBounds = It.IsAny<System.Numerics.Vector4>(), Times.Never);
        Assert.Empty(graphics.Bakes);
        graphics.Frame(tiles);
        tiles.Scroll(50);
        graphics.Warm(tiles, 2);
        Assert.Equal(4, graphics.Bakes.Count);
        graphics.Complete(graphics.Bakes[0]);
        Assert.Single(graphics.Allocations);
        graphics.Warm(tiles, 2);
        tiles.Dispose();
        Assert.Equal(5, graphics.Bakes.Count);
        foreach (var bake in graphics.Bakes.Skip(1)) graphics.Complete(bake);
        Assert.Equal(5, graphics.Allocations.Count);
        graphics.Cache.Dispose();
        Assert.Empty(graphics.Allocations);
    }

    [Fact]
    public void BakingIsLimitedPerFrameAndPendingTilesStillRenderTheirStates()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 10)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session, rows: 3);
        foreach (var (submitted, ready) in new[] { (0, 0), (4, 0), (4, 4), (8, 4), (8, 8), (9, 8), (9, 9) })
        {
            if (graphics.Programs.Count > 0) graphics.Programs[0].Invocations.Clear();
            graphics.Render.Invocations.Clear();
            graphics.Frame(tiles);
            Assert.Equal(submitted, graphics.Bakes.Count);
            Assert.Equal(submitted, graphics.Allocations.Count);
            var program = Assert.Single(graphics.Programs);
            program.VerifySet(value => value.HasIcon = true, Times.Exactly(ready));
            program.VerifySet(value => value.HasIcon = false, Times.Exactly(9 - ready));
            Assert.Equal(Enumerable.Repeat(1, 9), States(program));
            program.Verify(value => value.Use(), Times.Once);
            Assert.Equal(9, graphics.Render.Invocations.Count(call => call.Method.Name == nameof(IRenderAPI.RenderMesh)));
        }
        tiles.RenderInteractiveElements(0);
        Assert.Equal(9, graphics.Bakes.Count);
        tiles.Scroll(50);
        graphics.Warm(tiles, 3);
        Assert.Equal(10, graphics.Bakes.Count);
    }

    [Fact]
    public void AllVisitedPreviewsSurviveLongScrollsAndReturns()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        const int count = 300;
        var recipes = Enumerable.Range(1, count)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        for (int row = 0; row < count / 3; row++)
        {
            tiles.Scroll(row * 50);
            graphics.Warm(tiles, 3);
            Assert.Equal((row + 1) * 3, graphics.Allocations.Count);
        }
        Assert.Equal(count, graphics.Bakes.Count);
        Assert.All(graphics.Bakes, bake => Assert.Contains(bake.Id, graphics.Allocations));
        for (int row = count / 3 - 1; row >= 0; row--)
        {
            tiles.Scroll(row * 50);
            graphics.Warm(tiles, 3);
            Assert.Equal(count, graphics.Bakes.Count);
        }
        tiles.Dispose();
        Assert.Equal(count, graphics.Allocations.Count);
        graphics.Cache.Dispose();
        Assert.Empty(graphics.Allocations);
    }

    [Fact]
    public void ClosingAndReopeningTheMenuReusesTheWorldsBakedIcons()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 3)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var catalogue = f.Catalogue.Build(recipes);
        RecipeBookSession Open()
        {
            var session = new RecipeBookSession(catalogue, f.Allocator,
                () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
            session.Refresh();
            return session;
        }
        using (var first = CreateTiles(graphics, Open())) graphics.Warm(first, 3);
        Assert.Equal(3, graphics.Allocations.Count);
        using var reopened = CreateTiles(graphics, Open());
        reopened.RenderInteractiveElements(0);
        Assert.Equal(3, graphics.Bakes.Count);
        Assert.Equal(3, graphics.Allocations.Count);
        graphics.Programs[^1].VerifySet(value => value.HasIcon = true, Times.Exactly(3));
    }

    [Fact]
    public void GuiScaleChangesRetainBothSizesAndReturningToTheOldScaleDoesNotRebake()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using (var first = CreateTiles(graphics, session)) graphics.Warm(first, 3);
        RuntimeEnv.GUIScale = 1.5f;
        using (var scaled = CreateTiles(graphics, session)) graphics.Warm(scaled, 3);
        Assert.Equal(new[] { 44, 64 }, graphics.Bakes.Select(bake => bake.Size));
        RuntimeEnv.GUIScale = 1;
        using var restored = CreateTiles(graphics, session);
        restored.RenderInteractiveElements(0);
        Assert.Equal(2, graphics.Bakes.Count);
        Assert.Equal(2, graphics.Allocations.Count);
    }

    [Fact]
    public void ReloadedDeferredCompletionsDoNotUnblockTheNewFourRequestBatch()
    {
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = false };
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 6)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session, rows: 2);
        graphics.Warm(tiles, 2);
        Assert.Equal(4, graphics.Bakes.Count);
        graphics.Events.Raise(value => value.ReloadTextures += null);
        graphics.Warm(tiles, 2);
        Assert.Equal(8, graphics.Bakes.Count);
        foreach (var bake in graphics.Bakes.Take(4)) graphics.Complete(bake);
        Assert.Empty(graphics.Allocations);
        graphics.Frame(tiles);
        Assert.Equal(8, graphics.Bakes.Count);
        graphics.Complete(graphics.Bakes[4]);
        graphics.Warm(tiles, 2);
        Assert.Equal(9, graphics.Bakes.Count);
        tiles.Dispose();
        graphics.Cache.Dispose();
        foreach (var bake in graphics.Bakes.Skip(5)) graphics.Complete(bake);
        Assert.Empty(graphics.Allocations);
    }

    [Fact]
    public void AtlasReloadDoesNotFreeReusedSlotsOwnedByOtherCallers()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        graphics.Warm(tiles, 3);
        int foreignId = Assert.Single(graphics.Allocations);
        graphics.ReplaceAtlasPosition(foreignId);
        graphics.Events.Raise(value => value.ReloadTextures += null);
        Assert.Contains(foreignId, graphics.Allocations);
        graphics.Warm(tiles, 3);
        Assert.Equal(2, graphics.Allocations.Count);
        tiles.Dispose();
        graphics.Cache.Dispose();
        Assert.Equal(foreignId, Assert.Single(graphics.Allocations));
    }

    [Fact]
    public void NativeSubmissionFailurePropagatesWithoutTouchingGuiStateAndCanRetry()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        var clip = ElementBounds.Fixed(0, 0, 144, 48);
        graphics.Clips.Push(clip);
        int attempts = 0;
        graphics.OnBake = () =>
        {
            attempts++;
            throw new InvalidOperationException("submission failed");
        };
        tiles.RenderInteractiveElements(0);
        Assert.Throws<InvalidOperationException>(() => graphics.BeforeFrame());
        tiles.RenderInteractiveElements(0);
        Assert.Throws<InvalidOperationException>(() => graphics.BeforeFrame());
        Assert.Equal(2, attempts);
        Assert.Same(clip, Assert.Single(graphics.Clips));
        graphics.Render.Verify(value => value.GlScissorFlag(It.IsAny<bool>()), Times.Never);
        Assert.Empty(graphics.Allocations);
        graphics.OnBake = null;
        tiles.RenderInteractiveElements(0);
        graphics.BeforeFrame();
        graphics.EndFrame();
        Assert.Single(graphics.Bakes);
        Assert.Single(graphics.Allocations);
        Assert.NotNull(graphics.Cache.Get(Assert.Single(session.Rows).Recipe, 44, false, out _));
    }

    [Fact]
    public void GuiRequestsOnlyQueueAndDoNotTouchTheRenderApi()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var entry = Assert.Single(f.Catalogue.Build(recipe).Recipes);
        Assert.Null(graphics.Cache.Get(entry, 46, true, out bool queued));
        Assert.True(queued);
        Assert.Null(graphics.Cache.Get(entry, 46, true, out queued));
        Assert.False(queued);
        graphics.Cache.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Empty(graphics.Render.Invocations);
        graphics.BeforeFrame();
        Assert.Single(graphics.Bakes);
        Assert.Null(graphics.Cache.Get(entry, 46, true, out queued));
        Assert.False(queued);
        graphics.EndFrame();
        Assert.NotNull(graphics.Cache.Get(entry, 46, false, out queued));
        Assert.False(queued);
        graphics.Events.Verify(value => value.RegisterRenderer(graphics.Cache, EnumRenderStage.Before,
            "crafting-icon-requests"), Times.Once);
    }

    [Fact]
    public void RepeatedPreparationSharesOneFourOutstandingRequestBudget()
    {
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = false };
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 8)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var entries = f.Catalogue.Build(recipes).Recipes;
        int queued = 0;
        for (int pass = 0; pass < 3; pass++)
            foreach (var entry in entries)
            {
                Assert.Null(graphics.Cache.Get(entry, 46, true, out bool added));
                if (added) queued++;
            }
        Assert.Equal(4, queued);
        Assert.Empty(graphics.Bakes);
        graphics.BeforeFrame();
        Assert.Equal(4, graphics.Bakes.Count);
        foreach (var entry in entries)
        {
            Assert.Null(graphics.Cache.Get(entry, 46, true, out bool added));
            Assert.False(added);
        }
        graphics.BeforeFrame();
        Assert.Equal(4, graphics.Bakes.Count);
        graphics.Complete(graphics.Bakes[0]);
        Assert.Null(graphics.Cache.Get(entries[4], 46, true, out bool next));
        Assert.True(next);
        Assert.Null(graphics.Cache.Get(entries[5], 46, true, out next));
        Assert.False(next);
        graphics.BeforeFrame();
        Assert.Equal(5, graphics.Bakes.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AtlasSubmissionsRunBeforeGuiDrawingAndPublishOnlyOnNativeCompletion(bool completeOnFrame)
    {
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = completeOnFrame };
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        var calls = new List<string>();
        graphics.OnBake = () => calls.Add("submit");
        graphics.Render.Setup(value => value.RenderMesh(It.IsAny<MeshRef>())).Callback(() => calls.Add("draw"));
        graphics.Frame(tiles);
        Assert.Equal(new[] { "draw" }, calls);
        calls.Clear();
        graphics.Frame(tiles);
        Assert.Equal(new[] { "submit", "draw" }, calls);
        Assert.Single(graphics.Programs).VerifySet(value => value.HasIcon = false, Times.Exactly(2));
        if (!completeOnFrame) graphics.Complete(Assert.Single(graphics.Bakes));
        calls.Clear();
        graphics.Frame(tiles);
        Assert.Equal(new[] { "draw" }, calls);
        Assert.Single(graphics.Programs).VerifySet(value => value.HasIcon = true, Times.Once);
        graphics.Render.Verify(value => value.GlScissorFlag(It.IsAny<bool>()), Times.Never);
        graphics.Render.Verify(value => value.GetEngineShader(It.IsAny<EnumShaderProgram>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadAndDisposalDiscardRequestsNotYetSubmittedToTheNativeQueue(bool dispose)
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var entry = Assert.Single(f.Catalogue.Build(recipe).Recipes);
        Assert.Null(graphics.Cache.Get(entry, 46, true, out bool queued));
        Assert.True(queued);
        if (dispose) graphics.Cache.Dispose();
        else graphics.Events.Raise(value => value.ReloadTextures += null);
        graphics.BeforeFrame();
        graphics.Cache.OnRenderFrame(0, EnumRenderStage.Before);
        Assert.Empty(graphics.Bakes);
        if (dispose)
        {
            graphics.Events.Verify(value => value.UnregisterRenderer(graphics.Cache, EnumRenderStage.Before), Times.Once);
            Assert.Throws<ObjectDisposedException>(() => graphics.Cache.Get(entry, 46, true, out _));
        }
        else
        {
            Assert.Null(graphics.Cache.Get(entry, 46, true, out queued));
            Assert.True(queued);
            graphics.BeforeFrame();
            Assert.Single(graphics.Bakes);
        }
    }
}
