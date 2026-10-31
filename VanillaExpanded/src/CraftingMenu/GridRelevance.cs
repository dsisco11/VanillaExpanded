using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class GridRelevance
{
    private readonly List<List<ItemStack>> kinds = [];

    internal GridRelevance(IWorldAccessor world, ImmutableArray<CraftingStackSnapshot> inputs)
    {
        foreach (var input in inputs)
        {
            var stack = input.GetStack();
            stack.StackSize = 1;
            var kind = kinds.FirstOrDefault(kind => kind[0].Equals(world, stack));
            if (kind == null) kinds.Add([stack]);
            else kind.Add(stack);
        }
    }

    internal int Count(GridRecipe recipe, ImmutableArray<CraftingRequirement> requirements)
    {
        if (kinds.Count == 0 || requirements.IsEmpty) return 0;
        var ingredients = requirements.Select(requirement => requirement.GetIngredient()).ToArray();
        var matches = new bool[kinds.Count, requirements.Length];
        for (int kind = 0; kind < kinds.Count; kind++)
            for (int position = 0; position < requirements.Length; position++)
                matches[kind, position] = kinds[kind].Any(stack =>
                    CraftingIngredientMatch.Matches(stack, recipe, ingredients[position],
                        requirements[position].Quantity, out _));

        // Augmenting paths let restricted ingredients displace earlier wildcard matches.
        var assigned = Enumerable.Repeat(-1, requirements.Length).ToArray();
        bool Assign(int kind, bool[] visited)
        {
            for (int position = 0; position < requirements.Length; position++)
            {
                if (!matches[kind, position] || visited[position]) continue;
                visited[position] = true;
                if (assigned[position] >= 0 && !Assign(assigned[position], visited)) continue;
                assigned[position] = kind;
                return true;
            }
            return false;
        }

        int count = 0;
        for (int kind = 0; kind < kinds.Count; kind++)
            if (Assign(kind, new bool[requirements.Length])) count++;
        return count;
    }
}

internal static class CraftingIngredientMatch
{
    internal static bool Matches(ItemStack source, GridRecipe recipe, CraftingRecipeIngredient ingredient, int quantity,
        out ItemStack stack)
    {
        stack = CraftingStackSnapshot.Copy(source);
        stack.StackSize = quantity;
        return ingredient.SatisfiesAsIngredient(stack, false)
            && stack.Collectible.MatchesForCrafting(stack, recipe, ingredient);
    }
}
