using Moq;
using VanillaExpanded.CraftingMenu;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public class CraftingCandidateTests
{
    [Fact]
    public void CandidateUnionIgnoresQuantityButDoesNotClaimCraftability()
    {
        var fixture = new CatalogueFixture();
        var a = fixture.Recipe("a");
        var b = fixture.Recipe("b");
        fixture.Register(a); fixture.Register(b);
        var catalogue = fixture.Build(a, b);
        var oneStick = fixture.Stack(1, "game:stick");
        var candidates = catalogue.FindCandidates(new[] { oneStick, oneStick.Clone() });

        Assert.Equal(2, candidates.Length);
        Assert.All(new CraftingCatalogueView(catalogue).Search(""), selection => {
            Assert.False(selection.CanPlace);
            Assert.Null(selection.Evaluation.Relevance);
        });
        Assert.Equal(1, oneStick.StackSize);
        Assert.Equal(2, a.ResolvedIngredients![0]!.Quantity);
    }

    [Fact]
    public void RefreshRecomputesFromScratchAndDoesNotRetainOldMatches()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("stick");
        fixture.Register(recipe);
        var catalogue = fixture.Build(recipe);

        Assert.Single(catalogue.FindCandidates(new[] { fixture.Stack(1, "game:stick") }));
        Assert.Empty(catalogue.FindCandidates(new[] { fixture.Stack(2, "game:stone") }));
        Assert.Empty(catalogue.FindCandidates(Array.Empty<ItemStack>()));
        Assert.Single(new CraftingCatalogueView(catalogue).Search(""));
    }

    [Fact]
    public void SameCollectibleWithDifferentAttributesIsNotCollapsed()
    {
        var fixture = new CatalogueFixture();
        var red = fixture.Stack(1, "game:paint"); red.Attributes.SetString("color", "red");
        var blue = fixture.Stack(1, "game:paint"); blue.Attributes.SetString("color", "blue");
        var recipe = fixture.Recipe("blue");
        recipe.ResolvedIngredients![0]!.ResolvedItemStack = blue.Clone();
        recipe.ResolvedIngredients[0]!.Code = blue.Collectible.Code;
        fixture.Register(recipe);
        var catalogue = fixture.Build(recipe);

        Assert.Empty(catalogue.FindCandidates(new[] { red }));
        Assert.Single(catalogue.FindCandidates(new[] { red, blue }));
    }

    [Fact]
    public void WildcardCandidateLookupRespectsAllowedAndSkippedVariants()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("boards");
        recipe.ResolvedIngredients = new CraftingRecipeIngredient?[] {
            new() { Type = EnumItemClass.Item, Code = new AssetLocation("game:board-*"),
                MatchingType = EnumRecipeMatchType.Wildcard, AllowedVariants = new[] { "oak", "pine" },
                SkipVariants = new[] { "pine" }, Quantity = 4 }
        };
        fixture.Register(recipe);
        var catalogue = fixture.Build(recipe);

        Assert.Single(catalogue.FindCandidates(new[] { fixture.Stack(2, "game:board-oak") }));
        Assert.Empty(catalogue.FindCandidates(new[] { fixture.Stack(3, "game:board-pine") }));
        Assert.Empty(catalogue.FindCandidates(new[] { fixture.Stack(4, "game:board-birch") }));
    }

    [Fact]
    public void IndexIsRestrictedToEligibleCatalogueAndRetainsRecipesWithoutIngredients()
    {
        var fixture = new CatalogueFixture();
        var included = fixture.Recipe("included");
        var excluded = fixture.Recipe("excluded"); excluded.Enabled = false;
        var free = fixture.Recipe("free"); free.ResolvedIngredients = Array.Empty<CraftingRecipeIngredient?>();
        fixture.Register(included); fixture.Register(excluded);
        var catalogue = fixture.Build(included, excluded, free);

        Assert.Equal(2, catalogue.FindCandidates(new[] { fixture.Stack(1, "game:stick") }).Length);
        Assert.Same(free, Assert.Single(catalogue.FindCandidates(Array.Empty<ItemStack>())).Recipe);
    }

    [Fact]
    public void UnindexedNativeRecipeUsesItsOwnPredicates()
    {
        var fixture = new CatalogueFixture();
        var catalogue = fixture.Build(fixture.Recipe("unindexed"));
        Assert.Single(catalogue.FindCandidates(new[] { fixture.Stack(1, "game:stick") }));
        Assert.Empty(catalogue.FindCandidates(new[] { fixture.Stack(2, "game:stone") }));
    }

    [Fact]
    public void UniqueStackChecksAreSharedAcrossIndexedRecipes()
    {
        var fixture = new CatalogueFixture();
        var a = fixture.Recipe("a"); var b = fixture.Recipe("b");
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        fixture.Index.Add(predicate.Object, new List<IRecipeBase> { a, b });
        var catalogue = fixture.Build(a, b);
        var stack = fixture.Stack(1, "game:stick");

        Assert.Equal(2, catalogue.FindCandidates(new[] { stack, stack.Clone(), stack.Clone() }).Length);
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Once);
    }

    [Fact]
    public void CandidateSnapshotsPreserveTemporaryAttributesUsedByNativePredicates()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("temporary");
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false))
            .Returns((ItemStack stack, bool _) => stack.TempAttributes.GetString("state") == "ready");
        fixture.Index.Add(predicate.Object, new List<IRecipeBase> { recipe });
        var catalogue = fixture.Build(recipe);
        var cold = fixture.Stack(1, "game:stick"); cold.TempAttributes.SetString("state", "cold");
        var ready = fixture.Stack(1, "game:stick"); ready.TempAttributes.SetString("state", "ready");

        Assert.Single(catalogue.FindCandidates(new[] { cold, ready }));
        Assert.Equal("cold", cold.TempAttributes.GetString("state"));
        Assert.Equal("ready", ready.TempAttributes.GetString("state"));
    }

    [Fact]
    public void TemporaryAttributeDeduplicationPreservesNativeAttributeTypes()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("typed");
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false))
            .Returns((ItemStack stack, bool _) => stack.TempAttributes["typed"] is LongAttribute);
        fixture.Index.Add(predicate.Object, new List<IRecipeBase> { recipe });
        var catalogue = fixture.Build(recipe);
        var integer = fixture.Stack(1, "game:stick"); integer.TempAttributes.SetInt("typed", 1);
        var longInteger = fixture.Stack(1, "game:stick"); longInteger.TempAttributes.SetLong("typed", 1);

        Assert.Equal(integer.TempAttributes.ToJsonToken(), longInteger.TempAttributes.ToJsonToken());
        Assert.Single(catalogue.FindCandidates(new[] { integer, longInteger }));
    }

    [Fact]
    public void CustomPredicateIsDetectedBeforeNativeCloneErasesItsSubclass()
    {
        var fixture = new CatalogueFixture();
        var recipe = fixture.Recipe("custom");
        recipe.ResolvedIngredients = new CraftingRecipeIngredient?[] {
            new CustomIngredient { Type = EnumItemClass.Item, Code = new AssetLocation("game:stone"),
                ResolvedItemStack = fixture.Stack(9, "game:stone") }
        };
        fixture.Register(recipe);
        var catalogue = fixture.Build(recipe);

        Assert.IsType<CraftingRecipeIngredient>(catalogue.Recipes[0].Ingredients[0]);
        Assert.True(catalogue.Recipes[0].HasCustomIngredientPredicates);
        Assert.Single(catalogue.FindCandidates(new[] { fixture.Stack(1, "game:stick") }));
    }

    private sealed class CustomIngredient : CraftingRecipeIngredient
    {
        public override bool SatisfiesAsIngredient(ItemStack inputStack, bool checkStackSize = true) => true;
    }
}
