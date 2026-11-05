using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

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
