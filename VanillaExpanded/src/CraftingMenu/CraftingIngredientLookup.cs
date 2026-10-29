using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

// Lives for one inventory evaluation, never across inventory changes.
internal sealed class CraftingIngredientLookup
{
    private readonly KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>>[] index;
    private readonly IWorldAccessor world;
    private readonly Dictionary<(EnumItemClass Class, int Id),
        List<(ItemStack Stack, IRecipeBase[] Recipes)>> matches = new();

    internal CraftingIngredientLookup(IWorldAccessor world, KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>>[] index)
    {
        this.world = world;
        this.index = index;
    }

    internal IEnumerable<IRecipeBase> Match(ItemStack stack)
    {
        var key = (stack.Class, stack.Id);
        if (matches.TryGetValue(key, out var bucket))
            foreach (var entry in bucket)
                if (entry.Stack.Equals(world, stack)) return entry.Recipes;
        var snapshot = CraftingStackSnapshot.Copy(stack);
        var result = new List<IRecipeBase>();
        var seen = new HashSet<IRecipeBase>(ReferenceEqualityComparer.Instance);
        foreach (var pair in index)
            if (pair.Key.SatisfiesAsIngredient(stack, false))
                foreach (var recipe in pair.Value)
                    if (seen.Add(recipe)) result.Add(recipe);
        var found = result.ToArray();
        if (bucket == null) matches.Add(key, bucket = new());
        bucket.Add((snapshot, found));
        return found;
    }

}
