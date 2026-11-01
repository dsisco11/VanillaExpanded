using Cairo;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookIconRendererTests
{
    [Fact]
    public void DedicatedShaderUsesTheNativeBaseAndModOwnedStages()
    {
        var api = new Mock<ICoreClientAPI>();
        var shaders = new Mock<IShaderAPI>();
        var vertex = new Shader();
        var fragment = new Shader();
        shaders.Setup(value => value.NewShader(EnumShaderType.VertexShader)).Returns(vertex);
        shaders.Setup(value => value.NewShader(EnumShaderType.FragmentShader)).Returns(fragment);
        api.SetupGet(value => value.Shader).Returns(shaders.Object);

        var program = new RecipeBookIconShaderProgram(api.Object);

        Assert.IsAssignableFrom<ShaderProgram>(program);
        Assert.Equal(Constants.ModId, program.AssetDomain);
        Assert.Same(vertex, program.VertexShader);
        Assert.Same(fragment, program.FragmentShader);
        shaders.Verify(value => value.NewShaderProgram(), Times.Never);
    }

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
            tiles.Scroll(i * 48);
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
        tiles.Scroll(48);
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
        tiles.Scroll(48);
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
            tiles.Scroll(row * 48);
            graphics.Warm(tiles, 3);
            Assert.Equal((row + 1) * 3, graphics.Allocations.Count);
        }
        Assert.Equal(count, graphics.Bakes.Count);
        Assert.All(graphics.Bakes, bake => Assert.Contains(bake.Id, graphics.Allocations));
        for (int row = count / 3 - 1; row >= 0; row--)
        {
            tiles.Scroll(row * 48);
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
        Assert.Equal(new[] { 46, 70 }, graphics.Bakes.Select(bake => bake.Size));
        RuntimeEnv.GUIScale = 1;
        using var restored = CreateTiles(graphics, session);
        restored.RenderInteractiveElements(0);
        Assert.Equal(2, graphics.Bakes.Count);
        Assert.Equal(2, graphics.Allocations.Count);
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

    [Fact]
    public void PreviewSharingUsesNativeEqualityForAttributesAndQuantity()
    {
        var f = new AllocationFixture();
        var output = f.Stack(99, "output", 4);
        output.Attributes.SetString("variant", "red");
        output.TempAttributes.SetString("renderVariant", "smooth");
        var first = f.Catalogue.Recipe("first", output, 4);
        var same = f.Catalogue.Recipe("same", CraftingStackSnapshot.Copy(output), 8);
        var persistentOutput = CraftingStackSnapshot.Copy(output);
        persistentOutput.Attributes.SetString("variant", "blue");
        var persistent = f.Catalogue.Recipe("persistent", persistentOutput);
        var temporaryOutput = CraftingStackSnapshot.Copy(output);
        temporaryOutput.TempAttributes.SetString("renderVariant", "rough");
        var temporary = f.Catalogue.Recipe("temporary", temporaryOutput);
        var entries = f.Catalogue.Build(first, same, persistent, temporary).Recipes;
        Assert.Equal(entries[0].PreviewId, entries[1].PreviewId);
        Assert.NotEqual(entries[0].PreviewId, entries[2].PreviewId);
        Assert.Equal(entries[0].PreviewId, entries[3].PreviewId);
        Assert.Equal("smooth", entries[0].GetPreview()!.TempAttributes.GetString("renderVariant"));
        Assert.Equal("rough", entries[3].GetPreview()!.TempAttributes.GetString("renderVariant"));
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
    public void NativeDropdownPopupRendersAboveOutputQuantityLabels()
    {
        string originalLocale = Lang.CurrentLocale;
        var originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        Lang.AvailableLanguages["en"] = Mock.Of<ITranslationService>();
        Lang.ChangeLanguage("en");
        try { AssertPopupDepth(); }
        finally
        {
            if (originalEnglish == null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = originalEnglish;
            Lang.ChangeLanguage(originalLocale);
        }
    }

    private static void AssertPopupDepth()
    {
        using var graphics = new RecipeBookRenderFixture();
        graphics.Render.SetupGet(value => value.FrameHeight).Returns(1080);
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        recipe.Output!.Quantity = 4;
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        using var popup = new GuiElementListMenu(graphics.Api.Object, ["availability", "name"],
            ["Availability", "Name"], 0, (_, _) => { },
            ElementBounds.Fixed(0, -30, 144, 30).WithEmptyParent(), CairoFont.WhiteSmallText(), false);
        popup.Bounds.CalcWorldBounds();
        popup.ComposeDynamicElements();
        popup.Open();
        graphics.Render.Invocations.Clear();
        tiles.RenderInteractiveElements(0);
        var label = Assert.Single(graphics.Render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Count == 7 && call.Arguments[1] is double);
        popup.RenderInteractiveElements(0);
        var background = Assert.Single(graphics.Render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexture) && call.Arguments.Count == 7);
        Assert.True(Convert.ToSingle(label.Arguments[5]) < Convert.ToSingle(background.Arguments[5]));
        var popupClip = Assert.IsAssignableFrom<ElementBounds>(graphics.Render.Invocations.Last(call =>
            call.Method.Name == nameof(IRenderAPI.PushScissor)).Arguments[0]);
        Assert.True(popupClip.renderY <= (double)label.Arguments[2]);
        Assert.True(popupClip.renderY + popupClip.InnerHeight >= (double)label.Arguments[2] + (double)label.Arguments[4]);
        Assert.Empty(graphics.Clips);
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
        Assert.NotNull(graphics.Cache.Get(Assert.Single(session.Rows).Recipe, 46, false, out _));
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
        Assert.Equal(tile.Bounds.OuterWidthInt, pixels.InnerWidth);
        Assert.Equal(tile.Bounds.OuterHeightInt, pixels.InnerHeight);
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

    private static int[] States(Mock<IRecipeBookIconShaderProgram> program) => program.Invocations
        .Where(call => call.Method.Name == "set_" + nameof(IRecipeBookIconShaderProgram.RecipeState))
        .Select(call => (int)(CraftingTileStatus)call.Arguments[0]).ToArray();

    private static GuiElementRecipeBookTiles CreateTiles(RecipeBookRenderFixture graphics, RecipeBookSession session, int rows = 1)
    {
        var viewport = ElementBounds.Fixed(0, 0, 144, rows * 48).WithEmptyParent();
        viewport.CalcWorldBounds();
        var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
            ElementBounds.Fixed(0, 0, 144, (rows + 1) * 48).WithParent(viewport), viewport, session, _ => { }, (_, _) => { },
            graphics.Cache, graphics.CreateShader);
        tiles.BeforeCalcBounds();
        tiles.Bounds.CalcWorldBounds();
        return tiles;
    }
}

internal sealed class RecipeBookRenderFixture : IDisposable
{
    internal sealed record Bake(int Id, ItemStack Stack, int Size, float Scale, Action<int> Callback);
    internal readonly Mock<ICoreClientAPI> Api = new();
    internal readonly Mock<IGuiAPI> Gui = new();
    internal readonly Mock<IRenderAPI> Render = new();
    internal readonly Mock<IClientEventAPI> Events = new();
    internal readonly Mock<IShaderProgram> GuiProgram = new();
    internal readonly RecipeBookIconCache Cache;
    internal readonly List<Mock<IRecipeBookIconShaderProgram>> Programs = new();
    internal readonly List<Mock<MeshRef>> Meshes = new();
    internal readonly List<Bake> Bakes = new();
    internal readonly HashSet<int> Allocations = new();
    internal readonly HashSet<int> Textures = new();
    internal readonly Stack<ElementBounds> Clips = new();
    internal IShaderProgram? Active;
    internal bool CompleteOnFrame = true;
    internal bool CompileShaders = true;
    internal Action? OnBake;
    private EnumRenderStage stage = EnumRenderStage.Ortho;
    private readonly HashSet<IRenderer> renderers = new();
    private readonly HashSet<Bake> completed = new();
    private readonly TextureAtlasPosition[] positions = new TextureAtlasPosition[4096];
    private readonly float oldScale = RuntimeEnv.GUIScale;
    private readonly int oldThread = RuntimeEnv.MainThreadId;
    private int nextTexture;

    internal RecipeBookRenderFixture(float scale = 1)
    {
        Cache = new RecipeBookIconCache(Api.Object);
        RuntimeEnv.GUIScale = scale;
        RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;
        var atlas = new Mock<IItemTextureAtlasAPI>();
        atlas.SetupGet(value => value.Positions).Returns(positions);
        atlas.Setup(value => value.FreeTextureSpace(It.IsAny<int>())).Callback<int>(id => Assert.True(Allocations.Remove(id)));
        Api.SetupGet(value => value.ItemTextureAtlas).Returns(atlas.Object);
        Api.SetupGet(value => value.Gui).Returns(Gui.Object);
        Api.SetupGet(value => value.Render).Returns(Render.Object);
        Api.SetupGet(value => value.Event).Returns(Events.Object);
        Events.Setup(value => value.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before, It.IsAny<string>()))
            .Callback<IRenderer, EnumRenderStage, string>((renderer, _, _) => Assert.True(renderers.Add(renderer)));
        Events.Setup(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before))
            .Callback<IRenderer, EnumRenderStage>((renderer, _) => Assert.True(renderers.Remove(renderer)));
        Api.SetupGet(value => value.Input).Returns(Mock.Of<IInputAPI>());
        Api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        Gui.SetupGet(value => value.TextTexture).Returns(new TextTextureUtil(Api.Object));
        Gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(),
            ref It.Ref<LoadedTexture>.IsAny)).Callback(new Upload((ImageSurface image, bool linear, ref LoadedTexture texture) =>
        {
            if (texture.TextureId == 0) { texture.TextureId = ++nextTexture; Textures.Add(texture.TextureId); }
            texture.Width = image.Width;
            texture.Height = image.Height;
        }));
        Gui.Setup(value => value.DeleteTexture(It.IsAny<int>())).Callback<int>(id => Assert.True(Textures.Remove(id)));
        var shaders = new Mock<IShaderAPI>();
        shaders.Setup(value => value.NewShader(It.IsAny<EnumShaderType>())).Returns(() => Mock.Of<IShader>());
        Api.SetupGet(value => value.Shader).Returns(shaders.Object);
        GuiProgram.Setup(value => value.Use()).Callback(() => Active = GuiProgram.Object);
        GuiProgram.Setup(value => value.Stop()).Callback(() => Active = null);
        Active = GuiProgram.Object;
        Render.SetupGet(value => value.CurrentActiveShader).Returns(() => Active!);
        Render.SetupGet(value => value.CurrentProjectionMatrix).Returns(Mat4f.Create());
        Render.SetupGet(value => value.CurrentModelviewMatrix).Returns(Mat4f.Create());
        Render.SetupGet(value => value.ScissorStack).Returns(Clips);
        Render.Setup(value => value.PushScissor(It.IsAny<ElementBounds>(), It.IsAny<bool>()))
            .Callback<ElementBounds, bool>((bounds, _) => Clips.Push(bounds));
        Render.Setup(value => value.PopScissor()).Callback(() => { Assert.NotEmpty(Clips); Clips.Pop(); });
        Render.Setup(value => value.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
        {
            var mesh = new Mock<MeshRef> { CallBase = true };
            Meshes.Add(mesh);
            return mesh.Object;
        });
        Render.Setup(value => value.RenderItemStackToAtlas(It.IsAny<ItemStack>(), It.IsAny<ITextureAtlasAPI>(),
            It.IsAny<int>(), It.IsAny<Action<int>>(), It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns((ItemStack stack, ITextureAtlasAPI _, int size, Action<int> callback, int color, float sepia, float scale) =>
            {
                Assert.Equal(EnumRenderStage.Before, stage);
                OnBake?.Invoke();
                var bake = new Bake(Bakes.Count + 1, stack, size, scale, callback);
                Bakes.Add(bake);
                return false;
            });
    }

    internal IRecipeBookIconShaderProgram CreateShader()
    {
        var program = new Mock<IRecipeBookIconShaderProgram>();
        program.Setup(value => value.Compile()).Returns(() => CompileShaders);
        program.Setup(value => value.Use()).Callback(() => Active = program.Object);
        program.Setup(value => value.Stop()).Callback(() => Active = null);
        Programs.Add(program);
        return program.Object;
    }

    internal void Warm(GuiElementRecipeBookTiles tiles, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            int before = Bakes.Count;
            Frame(tiles);
            Assert.InRange(Bakes.Count - before, 0, RecipeBookIconCache.BakesPerFrame);
        }
    }

    internal void BeforeFrame(IRenderer? renderer = null)
    {
        stage = EnumRenderStage.Before;
        if (renderer != null) renderer.OnRenderFrame(0, stage);
        else foreach (var registered in renderers.ToArray()) registered.OnRenderFrame(0, stage);
    }

    internal void EndFrame()
    {
        stage = EnumRenderStage.Ortho;
        if (CompleteOnFrame)
            foreach (var bake in Bakes.Where(bake => !completed.Contains(bake)).ToArray()) Complete(bake);
    }

    internal void Frame(GuiElementRecipeBookTiles tiles)
    {
        BeforeFrame();
        stage = EnumRenderStage.Ortho;
        tiles.RenderInteractiveElements(0);
        EndFrame();
    }

    internal void Complete(Bake bake)
    {
        Assert.True(completed.Add(bake));
        positions[bake.Id] = new TextureAtlasPosition { atlasTextureId = 77, x1 = .1f, y1 = .2f, x2 = .3f, y2 = .4f };
        Allocations.Add(bake.Id);
        bake.Callback(bake.Id);
    }

    internal void ReplaceAtlasPosition(int id) => positions[id] = new TextureAtlasPosition { reloadIteration = 1 };

    private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);

    public void Dispose()
    {
        Cache.Dispose();
        RuntimeEnv.GUIScale = oldScale;
        RuntimeEnv.MainThreadId = oldThread;
    }
}
