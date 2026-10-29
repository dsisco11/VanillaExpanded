using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingClientPolicy
{
    internal const string Ambiguous = "vanillaexpanded:craftingmenu-ambiguous-output";
    private readonly HashSet<GridRecipe> ambiguous = new(ReferenceEqualityComparer.Instance);
    private readonly CharacterSystem characters;
    private readonly IPlayer player;

    internal CraftingClientPolicy(ICoreAPI api, IPlayer player)
    {
        this.player = player;
        characters = api.ModLoader.GetModSystem<CharacterSystem>();
        var all = api.World.GridRecipes.Where(recipe => recipe.Enabled).ToArray();
        if (all.Any(recipe => recipe.GetType() != typeof(GridRecipe)))
        {
            ambiguous.UnionWith(all);
            return;
        }
        var eligible = all.Where(recipe => recipe.ResolvedIngredients != null
            && recipe.Width <= 3 && recipe.Height <= 3).ToArray();
        var groups = eligible.Where(recipe => !recipe.Shapeless)
            .GroupBy(recipe => recipe.ResolvedIngredients!.Count(ingredient => ingredient != null));
        foreach (var group in groups)
        {
            var recipes = group.ToArray();
            for (int i = 0; i < recipes.Length; i++)
                for (int j = i + 1; j < recipes.Length; j++)
                    if (CouldOverlap(recipes[i], recipes[j]))
                    {
                        ambiguous.Add(recipes[i]);
                        ambiguous.Add(recipes[j]);
                    }
        }
        foreach (var shapeless in eligible.Where(recipe => recipe.Shapeless))
            foreach (var recipe in eligible)
                if (!ReferenceEquals(recipe, shapeless) && CouldOverlap(shapeless, recipe))
                {
                    ambiguous.Add(shapeless);
                    ambiguous.Add(recipe);
                }
    }

    internal bool CanAccess(GridRecipe recipe) =>
        recipe.RequiresTrait == null || characters.HasTrait(player, recipe.RequiresTrait);

    internal string? UnsupportedReason(GridRecipe recipe) =>
        ambiguous.Contains(recipe) ? Ambiguous : null;

    internal static bool CouldOverlap(GridRecipe left, GridRecipe right)
    {
        var a = left.ResolvedIngredients;
        var b = right.ResolvedIngredients;
        if (a == null || b == null || left.GetType() != typeof(GridRecipe) || right.GetType() != typeof(GridRecipe))
            return true;
        var first = a.Where(ingredient => ingredient != null).ToArray();
        var second = b.Where(ingredient => ingredient != null).ToArray();
        if (!left.Shapeless && !right.Shapeless)
        {
            if (first.Length != second.Length) return false;
            if (left.Width == right.Width && left.Height == right.Height
                && first.Length == a.Length && second.Length == b.Length)
                return a.Zip(b).All(pair => MayShareStack(pair.First!, pair.Second!));
        }
        // Native shapeless matching merges equivalent stacks/requirements, so counts need not agree.
        // With padding or shapeless layouts, only prove disjointness; do not guess a native winner.
        return first.All(ingredient => second.Any(other => MayShareStack(ingredient!, other!)))
            && second.All(ingredient => first.Any(other => MayShareStack(ingredient!, other!)));
    }

    private static bool MayShareStack(CraftingRecipeIngredient left, CraftingRecipeIngredient right) =>
        left.GetType() != typeof(CraftingRecipeIngredient) || right.GetType() != typeof(CraftingRecipeIngredient)
        || (left.Type == right.Type && (left.MatchingType != EnumRecipeMatchType.Exact
            || right.MatchingType != EnumRecipeMatchType.Exact || left.Code == null || right.Code == null
            || left.Code.Equals(right.Code)));
}
