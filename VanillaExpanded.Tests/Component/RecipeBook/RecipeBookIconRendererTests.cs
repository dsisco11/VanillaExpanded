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
public sealed class RecipeBookIconRendererTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MixedStatesShareOneShaderActivationAndDoNotRebakeOnStatusChanges(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 2);
        var complete = f.Recipe(f.Ingredient(board));
        var partial = f.Recipe(f.Ingredient(board, 4));
        var missing = f.Recipe(f.Ingredient(f.Stack(2, "stone", 1)));
        var unsupported = f.Recipe(f.Ingredient(f.Stack(3, "clay", 1)));
        unsupported.CopyAttributesFrom = "A";
        f.Register(complete, partial, missing, unsupported);
        var snapshot = f.Snapshot(board);
        var session = new RecipeBookSession(f.Catalogue.Build(complete, partial, missing, unsupported),
            f.Allocator, () => snapshot, (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session, rows: 2);
        graphics.Warm(tiles, 4);
        Assert.Single(graphics.Programs).Invocations.Clear();
        graphics.GuiProgram.Invocations.Clear();
        tiles.RenderInteractiveElements(0);
        var program = Assert.Single(graphics.Programs);
        Assert.Equal(new[] { 0, 3, 1, 2 }, States(program));
        program.Verify(value => value.Use(), Times.Once);
        program.Verify(value => value.Stop(), Times.Once);
        program.VerifySet(value => value.IconTexture = 77, Times.Once);
        program.VerifySet(value => value.ProjectionMatrix = It.IsAny<float[]>(), Times.Once);
        program.VerifySet(value => value.ModelViewMatrix = It.IsAny<float[]>(), Times.Once);
        program.VerifySet(value => value.GuiScale = scale, Times.Once);
        program.VerifySet(value => value.TileBounds = It.IsAny<System.Numerics.Vector4>(), Times.Exactly(4));
        program.VerifySet(value => value.TextureBounds = new System.Numerics.Vector4(.1f, .2f, .3f, .4f),
            Times.Exactly(4));
        program.VerifySet(value => value.HasIcon = true, Times.Exactly(4));
        graphics.GuiProgram.Verify(value => value.Use(), Times.Once);
        Assert.Equal(4, graphics.Bakes.Count);
        Assert.Equal(4, graphics.Allocations.Count);
        Assert.Same(graphics.GuiProgram.Object, graphics.Active);
        graphics.Render.Verify(value => value.RenderItemstackToGui(It.IsAny<ItemSlot>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<int>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        Assert.Empty(graphics.Textures);

        snapshot = f.Snapshot();
        session.Refresh();
        program.Invocations.Clear();
        tiles.RenderInteractiveElements(0);
        Assert.Equal(new[] { 1, 1, 1, 2 }, States(program));
        Assert.Equal(4, graphics.Bakes.Count);
        program.Verify(value => value.Use(), Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void IngredientAvailabilityTransitionsUpdateBordersWithoutRebaking(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 4);
        var recipe = f.Recipe(f.Ingredient(board, 4));
        f.Register(recipe);
        var snapshot = f.Snapshot();
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => snapshot, (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        graphics.Warm(tiles, 3);
        foreach (var (quantity, status) in new[] {
            (0, CraftingTileStatus.MissingIngredients),
            (1, CraftingTileStatus.PartiallyAvailable),
            (3, CraftingTileStatus.PartiallyAvailable),
            (4, CraftingTileStatus.Available),
            (5, CraftingTileStatus.Available),
            (0, CraftingTileStatus.MissingIngredients)
        })
        {
            var available = board.Clone();
            available.StackSize = quantity;
            snapshot = quantity == 0 ? f.Snapshot() : f.Snapshot(available);
            session.Refresh();
            if (graphics.Programs.Count > 0) graphics.Programs[0].Invocations.Clear();
            tiles.RenderInteractiveElements(0);
            Assert.Equal(new[] { (int)status }, States(Assert.Single(graphics.Programs)));
            Assert.Equal(Math.Min(quantity, 4), Assert.Single(session.Rows).Evaluation.Allocated);
            Assert.Single(graphics.Bakes);
            Assert.Single(graphics.Allocations);
        }
    }

    [Fact]
    public void AlternativeRecipesShareTheSameIconWhileTheirQuantityLabelsRemainIndependent()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var first = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        first.Output!.Quantity = 4;
        var originalOutput = Assert.IsType<ItemStack>(first.Output.ResolvedItemStack);
        originalOutput.StackSize = 6;
        var second = f.Recipe(f.Ingredient(f.Stack(2, "stone", 1)));
        second.Output = first.Output.Clone();
        second.Output.Quantity = 8;
        f.Register(first, second);
        var catalogue = f.Catalogue.Build(first, second);
        var session = new RecipeBookSession(catalogue, f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        graphics.Warm(tiles, 3);
        Assert.Equal(4, Assert.Single(session.Rows).Recipe.OutputQuantity);
        var bake = Assert.Single(graphics.Bakes);
        Assert.Equal(1, bake.Stack.StackSize);
        var oldLabels = graphics.Textures.ToArray();
        session.Select(Assert.Single(session.Rows).Output.Id);
        session.ChooseAlternative(catalogue.Recipes.Single(entry => entry.Recipe == second).Id);
        tiles.RenderInteractiveElements(0);
        Assert.Equal(8, Assert.Single(session.Rows).Recipe.OutputQuantity);
        Assert.Single(graphics.Bakes);
        Assert.Single(graphics.Allocations);
        Assert.Single(graphics.Textures);
        Assert.Single(oldLabels.Except(graphics.Textures));
        Assert.Equal(6, originalOutput.StackSize);
        Assert.Equal(catalogue.Recipes[0].PreviewId, catalogue.Recipes[1].PreviewId);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 1)]
    public void OnlyOutputQuantitiesAreDrawnForRecipesWithMultipleAlternatives(int quantity, int expectedOverlays)
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var first = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        first.Output!.Quantity = quantity;
        var second = f.Recipe(f.Ingredient(f.Stack(2, "stone", 1)));
        second.Output = first.Output;
        f.Register(first, second);
        var session = new RecipeBookSession(f.Catalogue.Build(first, second), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        Assert.Equal(2, Assert.Single(session.Rows).Output.Alternatives.Length);
        using var tiles = CreateTiles(graphics, session);
        tiles.RenderInteractiveElements(0);
        Assert.Equal(expectedOverlays, graphics.Textures.Count);
        graphics.Render.Verify(value => value.Render2DTexturePremultipliedAlpha(It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            221, It.IsAny<Vec4f>()), Times.Exactly(expectedOverlays));
        tiles.Dispose();
        Assert.Empty(graphics.Textures);
    }

    [Fact]
    public void QuantityRenderingFailureRestoresHostShaderAndScissors()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        recipe.Output!.Quantity = 4;
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        var host = ElementBounds.Fixed(0, 0, 200, 200).WithEmptyParent();
        graphics.Clips.Push(host);
        graphics.Render.Setup(value => value.Render2DTexturePremultipliedAlpha(It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            221, It.IsAny<Vec4f>())).Throws<InvalidOperationException>();
        Assert.Throws<InvalidOperationException>(() => tiles.RenderInteractiveElements(0));
        Assert.Same(host, Assert.Single(graphics.Clips));
        Assert.Same(graphics.GuiProgram.Object, graphics.Active);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void AtlasBakingAndPartialRowsCannotEscapeTheViewportOrTileClips(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 9)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        foreach (var recipe in recipes) recipe.Output!.Quantity = 4;
        f.Register(recipes);
        var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session, rows: 2);
        tiles.Scroll(12 * scale);
        var viewport = tiles.InsideClipBounds;
        var host = ElementBounds.Fixed(-100, -100, 500, 500).WithEmptyParent();
        host.CalcWorldBounds();
        graphics.Clips.Push(host);
        (double Left, double Top, double Right, double Bottom)? clip = null;
        var drawn = new List<(double Left, double Top, double Right, double Bottom)>();
        int labels = 0;
        graphics.Render.Setup(value => value.GlScissorFlag(false)).Callback(() => clip = null);
        graphics.Render.Setup(value => value.PushScissor(It.IsAny<ElementBounds>(), It.IsAny<bool>()))
            .Callback<ElementBounds, bool>((bounds, stacking) =>
            {
                Assert.True(stacking);
                var previous = graphics.Clips.Peek();
                clip = (Math.Max(bounds.renderX, previous.renderX),
                    Math.Max(bounds.renderY, previous.renderY),
                    Math.Min(bounds.renderX + bounds.InnerWidth, previous.renderX + previous.InnerWidth),
                    Math.Min(bounds.renderY + bounds.InnerHeight, previous.renderY + previous.InnerHeight));
                graphics.Clips.Push(bounds);
            });
        graphics.Render.Setup(value => value.PopScissor()).Callback(() =>
        {
            graphics.Clips.Pop();
            var previous = graphics.Clips.Peek();
            clip = (previous.renderX, previous.renderY,
                previous.renderX + previous.InnerWidth, previous.renderY + previous.InnerHeight);
        });
        graphics.Render.Setup(value => value.Render2DTexturePremultipliedAlpha(It.IsAny<int>(),
            It.IsAny<ElementBounds>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback(() => AssertViewportClip());
        graphics.Render.Setup(value => value.RenderMesh(It.IsAny<MeshRef>())).Callback(() =>
        {
            AssertViewportClip();
            Assert.Same(viewport, graphics.Clips.ToArray()[1]);
            drawn.Add(clip!.Value);
        });
        graphics.Render.Setup(value => value.Render2DTexturePremultipliedAlpha(It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            221, It.IsAny<Vec4f>())).Callback(() =>
        {
            AssertViewportClip();
            Assert.Same(viewport, graphics.Clips.ToArray()[1]);
            Assert.Contains(clip!.Value, drawn);
            labels++;
        });
        tiles.RenderInteractiveElements(0);
        Assert.Equal(9, drawn.Count);
        Assert.Equal(9, labels);
        Assert.Contains(drawn, bounds => bounds.Top == viewport.renderY);
        Assert.Contains(drawn, bounds => bounds.Bottom == viewport.renderY + viewport.InnerHeight);
        Assert.Same(host, Assert.Single(graphics.Clips));

        void AssertViewportClip()
        {
            Assert.NotNull(clip);
            Assert.True(clip.Value.Left >= viewport.renderX);
            Assert.True(clip.Value.Top >= viewport.renderY);
            Assert.True(clip.Value.Right <= viewport.renderX + viewport.InnerWidth);
            Assert.True(clip.Value.Bottom <= viewport.renderY + viewport.InnerHeight);
        }
    }

    [Fact]
    public void RenderingFailureRestoresHostShaderAndScissors()
    {
        using var graphics = new RecipeBookRenderFixture();
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        graphics.Render.Setup(value => value.RenderMesh(It.IsAny<MeshRef>())).Throws<InvalidOperationException>();
        Assert.Throws<InvalidOperationException>(() => tiles.RenderInteractiveElements(0));
        Assert.Empty(graphics.Clips);
        Assert.Same(graphics.GuiProgram.Object, graphics.Active);
        Assert.Single(graphics.Programs).Verify(value => value.Stop(), Times.Once);
    }

    [Theory]
    [InlineData(.75f)]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void TileIconAndScissorBoundsMatchNativeButtonPixelsAtFractionalPositions(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        using var tile = new GuiElementRecipeBookTile(graphics.Api.Object,
            ElementBounds.Fixed(3.37, 5.81, 46, 46).WithEmptyParent(), (_, _) => { }, _ => { }, () => { });
        tile.Bounds.CalcWorldBounds();
        var pixels = tile.PixelBounds;
        Assert.Equal((int)tile.Bounds.renderX, pixels.renderX);
        Assert.Equal((int)tile.Bounds.renderY, pixels.renderY);
        Assert.Equal(Math.Round(tile.Bounds.OuterWidth, MidpointRounding.AwayFromZero), pixels.InnerWidth);
        Assert.Equal(Math.Round(tile.Bounds.OuterHeight, MidpointRounding.AwayFromZero), pixels.InnerHeight);
        tile.Bounds.fixedY -= 13.17;
        tile.Bounds.MarkDirtyRecursive();
        tile.Bounds.CalcWorldBounds();
        Assert.Same(pixels, tile.PixelBounds);
        Assert.Equal((int)tile.Bounds.renderY, pixels.renderY);
        Assert.Equal(Math.Truncate(pixels.renderX), pixels.renderX);
        Assert.Equal(Math.Truncate(pixels.renderY), pixels.renderY);
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        tile.Selection = Assert.Single(session.Rows);
        using var renderer = new RecipeBookIconRenderer(graphics.Api.Object, graphics.Cache, graphics.CreateShader);
        renderer.Prepare([tile]);
        renderer.Render([tile]);
        Assert.Single(graphics.Programs).VerifySet(value => value.TileBounds =
            new System.Numerics.Vector4((float)pixels.renderX, (float)pixels.renderY,
                (float)pixels.InnerWidth, (float)pixels.InnerHeight), Times.Once);
        var scissors = graphics.Render.Invocations.Where(call => call.Method.Name == nameof(IRenderAPI.PushScissor));
        Assert.Equal(2, scissors.Count());
        Assert.All(scissors, call => Assert.Same(pixels, call.Arguments[0]));
    }

    [Fact]
    public void ShaderCompilationFailureIsReportedAndReleasesTheFailedProgram()
    {
        using var graphics = new RecipeBookRenderFixture { CompileShaders = false };
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        Assert.Throws<InvalidOperationException>(() => tiles.RenderInteractiveElements(0));
        Assert.Single(graphics.Programs).Verify(value => value.Dispose(), Times.Once);
        Mock.Get(graphics.Api.Object.Logger).Verify(value =>
            value.Error("[VanillaExpanded] Could not compile the crafting icon shader."), Times.Once);
        Assert.Empty(graphics.Meshes);
        Assert.Empty(graphics.Allocations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void AvailabilitySelectionNativeFocusAndQuantitiesRemainIndependent(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 2);
        var recipes = new[] {
            f.Recipe(f.Ingredient(board)),
            f.Recipe(f.Ingredient(board, 4)),
            f.Recipe(f.Ingredient(f.Stack(2, "stone", 1))),
            f.Recipe(f.Ingredient(f.Stack(3, "clay", 1)))
        };
        recipes[3].CopyAttributesFrom = "A";
        foreach (var recipe in recipes) recipe.Output!.Quantity = 12;
        f.Register(recipes);
        using var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(board), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session, rows: 2);
        graphics.Warm(tiles, 4);
        int bakes = graphics.Bakes.Count;
        var program = Assert.Single(graphics.Programs);
        for (int selected = -1; selected < 4; selected++)
        {
            if (selected >= 0) session.Select(session.Rows[selected].Output.Id);
            for (int focused = -1; focused < 4; focused++)
            {
                tiles.OnFocusLost();
                if (focused >= 0)
                {
                    tiles.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Tab });
                    tiles.OnFocusGained();
                    tiles.FocusElement(focused);
                }
                program.Invocations.Clear();
                graphics.Render.Invocations.Clear();
                tiles.RenderInteractiveElements(0);
                Assert.Equal(new[] { 0, 3, 1, 2 }, States(program));
                Assert.Equal(Enumerable.Range(0, 4).Select(index => index == selected),
                    program.Invocations.Where(call => call.Method.Name == "set_Selected")
                        .Select(call => (bool)call.Arguments[0]));
                program.VerifySet(value => value.PreviewInset = 1, Times.Once);
                var focusCalls = graphics.Render.Invocations.Where(call => call.Method.Name == nameof(IRenderAPI.RenderRectangle)).ToArray();
                Assert.Equal(focused >= 0 ? 1 : 0, focusCalls.Length);
                if (focused >= 0)
                {
                    var bounds = tiles.Elements[focused].Bounds;
                    var call = Assert.Single(focusCalls);
                    Assert.Equal((int)bounds.renderX, Convert.ToInt32(call.Arguments[0]));
                    Assert.Equal((int)bounds.renderY, Convert.ToInt32(call.Arguments[1]));
                    Assert.Equal(800f, Convert.ToSingle(call.Arguments[2]));
                    Assert.Equal(bounds.OuterWidthInt, Convert.ToInt32(call.Arguments[3]));
                    Assert.Equal(bounds.OuterHeightInt, Convert.ToInt32(call.Arguments[4]));
                }
                var counts = graphics.Render.Invocations.Where(call => call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
                    && call.Arguments.Count > 5 && Convert.ToSingle(call.Arguments[5]) == 221).ToArray();
                Assert.Equal(4, counts.Length);
                for (int index = 0; index < counts.Length; index++)
                {
                    var bounds = ((GuiElementRecipeBookTile)tiles.Elements[index]).PixelBounds;
                    Assert.Equal(bounds.renderX + bounds.InnerWidth - Convert.ToDouble(counts[index].Arguments[3]) - 2 * scale,
                        Convert.ToDouble(counts[index].Arguments[1]), 4);
                    Assert.Equal(bounds.renderY + bounds.InnerHeight - Convert.ToDouble(counts[index].Arguments[4]) - 2 * scale,
                        Convert.ToDouble(counts[index].Arguments[2]), 4);
                }
                Assert.Equal(bakes, graphics.Bakes.Count);
                Assert.Empty(graphics.Clips);
            }
        }
    }
}
