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

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Trait(TestCapabilities.TraitName, TestCapabilities.Cairo)]
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
    public void DisposedTileRetainsDeferredIconCompletionUntilWorldCacheDisposal()
    {
        // Arrange
        // A real tile consumer whose atlas callback remains deferred after the tile is disposed.
        using var graphics = new RecipeBookRenderFixture { CompleteOnFrame = false };
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "input", 1)));
        f.Register(recipe);
        var catalogue = f.Catalogue.Build(recipe);
        var entry = Assert.Single(catalogue.Recipes);
        var session = new RecipeBookSession(catalogue, f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        int previewSize = RecipeBookTileGeometry.PreviewSize(GuiElement.scaled(1), tiles.TileSize);
        tiles.RenderInteractiveElements(0);
        graphics.BeforeFrame();
        Assert.Single(graphics.Bakes);

        // Act
        // Close the tile consumer while the world-owned icon cache remains alive, then complete atlas work.
        tiles.Dispose();
        foreach (var bake in graphics.Bakes) graphics.Complete(bake);

        // Assert
        // The cache retains the published allocation until its own world-lifetime disposal.
        Assert.Single(graphics.Allocations);
        Assert.NotNull(graphics.Cache.Get(entry, previewSize, allowQueue: false, out bool queued));
        Assert.False(queued);
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
        var entry = Assert.Single(session.Rows).Recipe;
        int requestSize = RecipeBookTileGeometry.PreviewSize(GuiElement.scaled(1), tiles.TileSize);
        var calls = new List<string>();
        bool? publishedDuringDraw = null;
        graphics.OnBake = () => calls.Add("submit");
        graphics.Render.Setup(value => value.RenderMesh(It.IsAny<MeshRef>())).Callback(() =>
        {
            calls.Add("draw");
            publishedDuringDraw = graphics.Cache.Get(entry, requestSize, allowQueue: false, out _) != null;
        });
        RenderFrame();
        Assert.Equal(new[] { "draw" }, calls);
        Assert.False(publishedDuringDraw);
        calls.Clear();
        publishedDuringDraw = null;
        RenderFrame();
        Assert.Equal(new[] { "submit", "draw" }, calls);
        Assert.False(publishedDuringDraw);
        if (!completeOnFrame) graphics.Complete(Assert.Single(graphics.Bakes));
        calls.Clear();
        publishedDuringDraw = null;
        RenderFrame();
        Assert.Equal(new[] { "draw" }, calls);
        Assert.True(publishedDuringDraw);

        void RenderFrame()
        {
            graphics.BeforeFrame();
            tiles.RenderInteractiveElements(0);
            graphics.EndFrame();
        }
    }
}
