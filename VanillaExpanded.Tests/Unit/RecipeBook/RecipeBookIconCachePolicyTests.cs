using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaExpanded.Tests;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookIconRendererTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookIconCachePolicyTests
{
    [Fact]
    public void WorldExitReleasesDeferredIconCompletions()
    {
        // Arrange pending atlas work without constructing a tile or a text resource.
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = false };
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 9)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var entries = f.Catalogue.Build(recipes).Recipes;
        foreach (var entry in entries)
            graphics.Cache.Get(entry, 46, allowQueue: true, out _);

        // Act: submit the outstanding work, then dispose its world-owned cache before callbacks arrive.
        graphics.BeforeFrame();
        Assert.Equal(4, graphics.Bakes.Count);
        graphics.Cache.Dispose();

        // Assert deferred atlas callbacks release allocations instead of publishing orphaned entries.
        foreach (var bake in graphics.Bakes) graphics.Complete(bake);
        Assert.Empty(graphics.Allocations);
    }

    [Fact]
    public void BakingBudgetAndPendingAllocationsDoNotDuplicateRequests()
    {
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = true };
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 10)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var entries = f.Catalogue.Build(recipes).Recipes;
        // Arrange ten distinct keys so more than one bounded request batch is required.
        int ready = 0;
        while (ready < entries.Length)
        {
            // Act: repeatedly request every entry while the current batch is outstanding.
            int queued = 0;
            foreach (var entry in entries)
            {
                graphics.Cache.Get(entry, 46, allowQueue: true, out bool wasQueued);
                if (wasQueued) queued++;
            }
            int expectedBatch = Math.Min(RecipeBookIconCache.BakesPerFrame, entries.Length - ready);
            Assert.Equal(expectedBatch, queued);

            // Act: submit the outstanding batch and observe its completion.
            graphics.BeforeFrame();
            Assert.Equal(ready + expectedBatch, graphics.Bakes.Count);
            foreach (var entry in entries)
            {
                graphics.Cache.Get(entry, 46, allowQueue: true, out bool wasQueued);
                Assert.False(wasQueued);
            }
            graphics.EndFrame();

            ready += expectedBatch;
            Assert.Equal(ready, graphics.Allocations.Count);
            Assert.Equal(ready, entries.Count(entry =>
                graphics.Cache.Get(entry, 46, allowQueue: false, out _) != null));
        }
    }

    [Fact]
    public void AllRequestedPreviewsRemainCachedAfterLargeBatchesAndReturns()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        const int count = 40;
        // Arrange enough distinct entries to exercise several cache request batches.
        var recipes = Enumerable.Range(1, count)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var entries = f.Catalogue.Build(recipes).Recipes;

        // Act: request every entry over multiple bounded atlas submissions.
        int ready = 0;
        while (ready < count)
        {
            foreach (var entry in entries)
                graphics.Cache.Get(entry, 46, allowQueue: true, out _);
            int expectedReady = Math.Min(ready + RecipeBookIconCache.BakesPerFrame, count);

            // Act: submit one bounded batch.
            graphics.BeforeFrame();
            Assert.Equal(expectedReady, graphics.Bakes.Count);
            graphics.EndFrame();
            ready = expectedReady;

            // Assert only completed submissions own atlas allocations.
            Assert.Equal(ready, graphics.Allocations.Count);
        }

        // Act: revisit every previously requested entry without allowing new submissions.
        foreach (var entry in entries)
        {
            Assert.NotNull(graphics.Cache.Get(entry, 46, allowQueue: false, out bool queued));
            Assert.False(queued);
        }
        Assert.Equal(count, graphics.Bakes.Count);

        // Assert cache disposal remains the sole owner that releases the published allocations.
        graphics.Cache.Dispose();
        Assert.Empty(graphics.Allocations);
    }

    [Fact]
    public void DistinctPreviewSizesRetainSeparateCacheEntriesAndReusePriorSizes()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var entry = Assert.Single(f.Catalogue.Build(recipe).Recipes);

        // Arrange two caller-selected size keys.
        // Act: request and complete each size independently.
        Assert.Null(graphics.Cache.Get(entry, 31, allowQueue: true, out bool firstQueued));
        Assert.True(firstQueued);
        graphics.BeforeFrame();
        graphics.EndFrame();
        var first = graphics.Cache.Get(entry, 31, allowQueue: false, out _);
        Assert.NotNull(first);

        Assert.Null(graphics.Cache.Get(entry, 59, allowQueue: true, out bool secondQueued));
        Assert.True(secondQueued);
        graphics.BeforeFrame();
        graphics.EndFrame();
        var second = graphics.Cache.Get(entry, 59, allowQueue: false, out _);
        Assert.NotNull(second);

        // Assert distinct size keys remain independently addressable, and revisiting either does not rebake.
        Assert.Equal(2, graphics.Bakes.Count);
        Assert.Equal(2, graphics.Allocations.Count);
        Assert.NotSame(first, second);
        Assert.Same(first, graphics.Cache.Get(entry, 31, allowQueue: true, out bool queued));
        Assert.False(queued);
        Assert.Same(second, graphics.Cache.Get(entry, 59, allowQueue: true, out queued));
        Assert.False(queued);
        Assert.Equal(2, graphics.Bakes.Count);
        Assert.Equal(2, graphics.Allocations.Count);
    }

    [Fact]
    public void ReloadedDeferredCompletionsDoNotUnblockTheNewFourRequestBatch()
    {
        // Arrange a first four-request batch whose atlas callbacks remain deferred.
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = false };
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 6)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        var entries = f.Catalogue.Build(recipes).Recipes;

        // Arrange a pending first batch directly at the cache boundary.
        foreach (var entry in entries)
            graphics.Cache.Get(entry, 46, allowQueue: true, out _);
        graphics.BeforeFrame();
        Assert.Equal(4, graphics.Bakes.Count);

        // Act: invalidate that request generation, then fill a fresh batch after atlas reload.
        graphics.Events.Raise(value => value.ReloadTextures += null);
        foreach (var entry in entries)
            graphics.Cache.Get(entry, 46, allowQueue: true, out _);
        graphics.BeforeFrame();
        Assert.Equal(8, graphics.Bakes.Count);

        // Assert late callbacks from the old generation cannot allocate slots or free new capacity.
        foreach (var bake in graphics.Bakes.Take(4)) graphics.Complete(bake);
        Assert.Empty(graphics.Allocations);
        foreach (var entry in entries)
            graphics.Cache.Get(entry, 46, allowQueue: true, out _);
        graphics.BeforeFrame();
        Assert.Equal(8, graphics.Bakes.Count);

        // Act: complete a current-generation request and verify its released slot can be reused.
        graphics.Complete(graphics.Bakes[4]);
        foreach (var entry in entries)
            graphics.Cache.Get(entry, 46, allowQueue: true, out _);
        graphics.BeforeFrame();
        Assert.Equal(9, graphics.Bakes.Count);
        // Act: dispose the cache before remaining current-generation callbacks arrive.
        graphics.Cache.Dispose();
        foreach (var bake in graphics.Bakes.Skip(5)) graphics.Complete(bake);

        // Assert every late completion is released instead of becoming an orphaned allocation.
        Assert.Empty(graphics.Allocations);
    }

    [Fact]
    public void AtlasReloadDoesNotFreeReusedSlotsOwnedByOtherCallers()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var entry = Assert.Single(f.Catalogue.Build(recipe).Recipes);

        // Arrange a completed allocation and replace its atlas position with a foreign owner.
        Assert.Null(graphics.Cache.Get(entry, 46, allowQueue: true, out bool queued));
        Assert.True(queued);
        graphics.BeforeFrame();
        graphics.EndFrame();
        int foreignId = Assert.Single(graphics.Allocations);
        graphics.ReplaceAtlasPosition(foreignId);

        // Act: reload and request the same cache entry again.
        graphics.Events.Raise(value => value.ReloadTextures += null);
        Assert.Contains(foreignId, graphics.Allocations);
        Assert.Null(graphics.Cache.Get(entry, 46, allowQueue: true, out queued));
        Assert.True(queued);
        graphics.BeforeFrame();
        graphics.EndFrame();
        Assert.Equal(2, graphics.Allocations.Count);
        graphics.Cache.Dispose();
        // Assert cleanup releases only the cache-owned replacement allocation.
        Assert.Equal(foreignId, Assert.Single(graphics.Allocations));
    }

    [Fact]
    public void AtlasSubmissionFailurePropagatesWithoutChangingGuiStateAndAllowsRetry()
    {
        // Arrange a cache-owned request whose mocked atlas submission fails once.
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var entry = Assert.Single(f.Catalogue.Build(recipe).Recipes);
        var clip = ElementBounds.Fixed(0, 0, 144, 48);
        graphics.Clips.Push(clip);
        int attempts = 0;
        graphics.OnBake = () =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("submission failed");
        };

        // Act: submit the queued request and then explicitly queue its failed entry again.
        Assert.Null(graphics.Cache.Get(entry, 46, allowQueue: true, out bool queued));
        Assert.True(queued);
        Assert.Throws<InvalidOperationException>(() => graphics.BeforeFrame());
        Assert.Same(clip, Assert.Single(graphics.Clips));
        graphics.Render.Verify(value => value.GlScissorFlag(It.IsAny<bool>()), Times.Never);
        Assert.Null(graphics.Cache.Get(entry, 46, allowQueue: true, out queued));
        Assert.True(queued);
        graphics.BeforeFrame();
        Assert.Empty(graphics.Allocations);
        graphics.EndFrame();

        // Assert the retry was submitted and published after the original failure.
        Assert.Equal(2, attempts);
        Assert.Same(clip, Assert.Single(graphics.Clips));
        graphics.Render.Verify(value => value.GlScissorFlag(It.IsAny<bool>()), Times.Never);
        Assert.Single(graphics.Bakes);
        Assert.Single(graphics.Allocations);
        Assert.NotNull(graphics.Cache.Get(entry, 46, false, out _));
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
