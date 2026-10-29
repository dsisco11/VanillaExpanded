using Moq;
using VanillaExpanded.CraftingMenu;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public class CraftingCatalogueTests
{
    [Fact]
    public void Build_FiltersProcessesVisibilityAccessAndGridFit()
    {
        var fixture = new CatalogueFixture();
        var included = fixture.Recipe("othermod:valid");
        var disabled = fixture.Recipe("disabled"); disabled.Enabled = false;
        var hidden = fixture.Recipe("hidden"); hidden.ShowInCreatedBy = false;
        var denied = fixture.Recipe("denied"); denied.RequiresTrait = "unavailable";
        var wide = fixture.Recipe("wide"); wide.Width = 4;
        var tall = fixture.Recipe("tall"); tall.Height = 4;
        var shapeless = fixture.Recipe("shapeless"); shapeless.Shapeless = true;
        var invalid = fixture.Recipe("invalid"); invalid.Width = 0;
        var catalogue = fixture.Build(new IRecipeBase[] {
            new Mock<IRecipeBase>().Object, included, disabled, hidden, denied, wide, tall, shapeless, invalid
        }, recipe => recipe.RequiresTrait == null);

        Assert.Equal(new[] { included, shapeless }, catalogue.Recipes.Select(entry => entry.Recipe));
    }

    [Fact]
    public void Build_UsesResolvedIngredientsWithoutAuthoringData()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("resolved", quantity: 4);
        recipe.Ingredients = null;
        recipe.IngredientPattern = null;
        var entry = Assert.Single(fixture.Build(recipe).Recipes);

        Assert.Equal(4, entry.OutputQuantity);
        Assert.Equal(4, entry.GetPreview()!.StackSize);
        Assert.Single(entry.Ingredients);
        Assert.NotSame(recipe.ResolvedIngredients![0], entry.Ingredients[0]);
        Assert.Same(recipe, entry.Recipe);
    }

    [Fact]
    public void Build_GroupsSameOutputRegardlessOfQuantityAndAttributeInsertionOrder()
    {
        var fixture = new CatalogueFixture();
        var outputA = fixture.Stack(10, "game:board");
        outputA.Attributes.SetString("wood", "oak");
        outputA.Attributes.SetInt("grade", 2);
        var outputB = fixture.Stack(10, "game:board");
        outputB.Attributes.SetInt("grade", 2);
        outputB.Attributes.SetString("wood", "oak");
        var a = fixture.Recipe("a", outputA, 2);
        var b = fixture.Recipe("b", outputB, 8);

        var group = Assert.Single(fixture.Build(a, b).Groups);
        Assert.Equal(new[] { 2, 8 }, group.Alternatives.Select(entry => entry.OutputQuantity));
    }

    [Fact]
    public void Build_KeepsAttributeSensitiveOutputsSeparate()
    {
        var fixture = new CatalogueFixture();
        var oak = fixture.Stack(10, "game:board"); oak.Attributes.SetString("wood", "oak");
        var pine = fixture.Stack(10, "game:board"); pine.Attributes.SetString("wood", "pine");
        var catalogue = fixture.Build(fixture.Recipe("oak", oak), fixture.Recipe("pine", pine));

        Assert.Equal(2, catalogue.Groups.Length);
    }

    [Fact]
    public void Build_DuplicateNamesAndRecipeIdsStillHaveDistinctStableLocalIdentities()
    {
        var fixture = new CatalogueFixture();
        var a = fixture.Recipe("same");
        var b = fixture.Recipe("same");
        a.RecipeId = b.RecipeId = 0;
        var first = fixture.Build(a, b, a);
        var second = fixture.Build(a, b, a);

        Assert.Equal(2, first.Recipes.Length);
        Assert.Equal(2, first.Recipes.Select(entry => entry.StableId).Distinct().Count());
        Assert.Equal(first.Recipes.Select(entry => entry.StableId), second.Recipes.Select(entry => entry.StableId));
    }

    [Fact]
    public void Build_DynamicPreviewIsGenericAndNeverInvokesOutputGeneration()
    {
        var fixture = new CatalogueFixture();
        var recipe = new ThrowingOutputRecipe {
            Name = new AssetLocation("mod:dynamic"), Width = 1, Height = 1,
            Output = fixture.Recipe("base").Output,
            ResolvedIngredients = fixture.Recipe("inputs").ResolvedIngredients,
            CopyAttributesFrom = "A"
        };
        var entry = Assert.Single(fixture.Build(recipe).Recipes);

        Assert.True(entry.IsGenericPreview);
        Assert.NotNull(entry.GetPreview());
        Assert.False(new CraftingCatalogueView(fixture.Build(recipe)).GetSelection(0).CanPlace);
    }

    [Fact]
    public void Build_UnresolvedOutputRemainsSearchableUsingRecipeIdentity()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("mod:unresolved");
        recipe.Output = null;
        var catalogue = fixture.Build(recipe);
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [catalogue.Recipes[0].Id] = CatalogueFixture.Full()
        });

        var selection = Assert.Single(view.Search("unresolved"));
        Assert.Null(selection.Recipe.GetPreview());
        Assert.False(selection.CanPlace);
        Assert.Null(selection.Evaluation.Relevance);
        Assert.Contains("unresolved-output", selection.Evaluation.UnsupportedReason);
        Assert.Empty(selection.Evaluation.Missing);
    }

    [Fact]
    public void Build_UnresolvedIngredientsCannotBeMadeFillableByScores()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("unresolved");
        recipe.ResolvedIngredients = null;
        var catalogue = fixture.Build(recipe);
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [catalogue.Recipes[0].Id] = CatalogueFixture.Full()
        });
        Assert.False(view.GetSelection(0).CanPlace);
        Assert.Contains("unresolved-ingredients", view.GetSelection(0).Evaluation.UnsupportedReason);
    }

    [Fact]
    public void PreviewIsDefensivelyCopied()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("preview", quantity: 4);
        var entry = Assert.Single(fixture.Build(recipe).Recipes);
        entry.GetPreview()!.Attributes.SetString("changed", "yes");
        entry.GetPreview()!.StackSize = 99;

        Assert.False(entry.GetPreview()!.Attributes.HasAttribute("changed"));
        Assert.False(recipe.Output!.ResolvedItemStack!.Attributes.HasAttribute("changed"));
        Assert.Equal(4, entry.GetPreview()!.StackSize);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(-1, 3)]
    public void Build_RejectsInvalidGridDimensions(int width, int height)
    {
        var fixture = new CatalogueFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => CraftingCatalogue.Build(
            Array.Empty<IRecipeBase>(), fixture.World.Object, width, height, _ => true));
    }

    private sealed class ThrowingOutputRecipe : GridRecipe
    {
        public override void GenerateOutputStack(ItemSlot[] inputSlots, ItemSlot outputSlot)
            => throw new InvalidOperationException("Output generation must not be used by catalogue construction.");
    }
}

internal sealed class CatalogueFixture
{
    internal readonly Mock<IWorldAccessor> World = new();
    internal readonly OrderedDictionary<IRecipeIngredientBase, List<IRecipeBase>> Index = new();
    private int nextId = 100;

    internal CatalogueFixture() => World.Setup(world => world.FastSearchRecipesByIngredient).Returns(Index);

    internal ItemStack Stack(int id, string code)
    {
        var item = MockItem.CreateNonLightSource(id);
        item.Code = new AssetLocation(code);
        return new ItemStack(item);
    }

    internal GridRecipe Recipe(string name, ItemStack? output = null, int quantity = 1)
    {
        output ??= Stack(nextId++, "mod:output-" + nextId);
        var ingredientStack = Stack(1, "game:stick");
        return new GridRecipe {
            Name = new AssetLocation(name), Width = 1, Height = 1,
            Output = new CraftingRecipeIngredient {
                Code = output.Collectible.Code, Type = output.Class, ResolvedItemStack = output, Quantity = quantity
            },
            ResolvedIngredients = new CraftingRecipeIngredient?[] {
                new() { Id = "A", Code = ingredientStack.Collectible.Code, Type = EnumItemClass.Item,
                    ResolvedItemStack = ingredientStack, Quantity = 2 }
            }
        };
    }

    internal CraftingCatalogue Build(params GridRecipe[] recipes) => Build(recipes, _ => true);

    internal CraftingCatalogue Build(IEnumerable<IRecipeBase> recipes, System.Func<GridRecipe, bool> canAccess) =>
        CraftingCatalogue.Build(recipes, World.Object, 3, 3, canAccess,
            stack => stack.Attributes.GetString("displayName") ?? stack.Collectible.Code.Path);

    internal void Register(GridRecipe recipe)
    {
        foreach (var ingredient in recipe.ResolvedIngredients!)
        {
            if (ingredient == null) continue;
            var packed = new FastSearchCraftingRecipeIngredient(ingredient) {
                MatchingType = ingredient.MatchingType, ResolvedItemStack = ingredient.ResolvedItemStack
            };
            if (!Index.TryGetValue(packed, out var recipes))
            {
                recipes = new List<IRecipeBase>();
                Index.Add(packed, recipes);
            }
            recipes.Add(recipe);
        }
    }

    internal static CraftingRecipeEvaluation Full() =>
        CraftingRecipeEvaluation.Supported(2, 2, true, System.Collections.Immutable.ImmutableArray<CraftingMissingRequirement>.Empty);
}
