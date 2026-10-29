using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

// Lives for one inventory evaluation, never across inventory changes.
internal sealed class CraftingIngredientLookup
{
    private readonly KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>>[] index;
    private readonly Dictionary<string, IRecipeBase[]> matches = new(StringComparer.Ordinal);

    internal CraftingIngredientLookup(KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>>[] index) =>
        this.index = index;

    internal IEnumerable<IRecipeBase> Match(ItemStack stack)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        stack.ToBytes(writer);
        stack.TempAttributes.ToBytes(writer);
        string identity = Convert.ToBase64String(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        if (matches.TryGetValue(identity, out var found)) return found;
        var result = new List<IRecipeBase>();
        var seen = new HashSet<IRecipeBase>(ReferenceEqualityComparer.Instance);
        foreach (var pair in index)
            if (pair.Key.SatisfiesAsIngredient(stack, false))
                foreach (var recipe in pair.Value)
                    if (seen.Add(recipe)) result.Add(recipe);
        found = result.ToArray();
        matches.Add(identity, found);
        return found;
    }

}
