using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingAllocator
{
    private const string Exceptional = "vanillaexpanded:craftingmenu-unsupported-semantics";
    private const string NativeMismatch = "vanillaexpanded:craftingmenu-native-mismatch";
    private readonly IWorldAccessor world;
    private readonly IPlayer player;
    private readonly System.Func<GridRecipe, string?> independentPolicy;
    // The policy handles recipe-output ambiguity; native matching invokes recipe events.
    internal CraftingAllocator(IWorldAccessor world, IPlayer player,
        System.Func<GridRecipe, string?> independentPolicy)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.player = player ?? throw new ArgumentNullException(nameof(player));
        this.independentPolicy = independentPolicy ?? throw new ArgumentNullException(nameof(independentPolicy));
    }

    internal ImmutableDictionary<int, CraftingAllocation> EvaluateCatalogue(
        CraftingCatalogue catalogue, CraftingInventorySnapshot snapshot)
    {
        CraftingInventorySnapshot.EnsureThread(snapshot.OwnerThreadId);
        var lookup = catalogue.CreateLookup();
        var candidates = catalogue.FindCandidates(snapshot.Stacks.Select(stack => stack.GetStack()), lookup)
            .Select(entry => entry.Id).ToHashSet();
        return catalogue.Recipes.ToImmutableDictionary(entry => entry.Id,
            entry => Evaluate(entry, snapshot, candidates.Contains(entry.Id), lookup));
    }

    internal CraftingAllocation Evaluate(CraftingRecipeEntry entry, CraftingInventorySnapshot snapshot)
    {
        CraftingInventorySnapshot.EnsureThread(snapshot.OwnerThreadId);
        return Evaluate(entry, snapshot, true, new(world.FastSearchRecipesByIngredient.ToArray()));
    }

    private CraftingAllocation Evaluate(CraftingRecipeEntry entry, CraftingInventorySnapshot snapshot, bool candidate,
        CraftingIngredientLookup lookup)
    {
        CraftingInventorySnapshot.EnsureThread(snapshot.OwnerThreadId);
        if (entry.UnresolvedReason != null) return CraftingAllocation.Unsupported(entry.UnresolvedReason);
        string? policyReason = independentPolicy(entry.Recipe);
        if (policyReason != null) return CraftingAllocation.Unsupported(policyReason);
        var recipe = entry.Recipe;
        if (recipe.GetType() != typeof(GridRecipe) || entry.HasCustomIngredientPredicates
            || recipe.CopyAttributesFrom != null || recipe.MergeAttributesFrom.Length > 0
            || recipe.ResolvedIngredients == null || recipe.ResolvedIngredients.Length != recipe.Width * recipe.Height
            || recipe.Width > snapshot.Width || recipe.Height > snapshot.Height
            || (recipe.Output!.ResolvedItemStack!.Collectible.TransitionableProps?.Length ?? 0) > 0)
            return CraftingAllocation.Unsupported(Exceptional);

        var requirements = ImmutableArray.CreateBuilder<CraftingRequirement>();
        for (int i = 0; i < recipe.ResolvedIngredients.Length; i++)
        {
            var ingredient = recipe.ResolvedIngredients[i];
            if (ingredient == null) continue;
            var consume = ingredient.GetConsumeProperties();
            if (ingredient.Quantity <= 0 || ingredient.RecipeAttributes != null || consume.DurabilityCost < 0
                || (!consume.Consume && (ingredient.Quantity != 1 || consume.DurabilityChange > 0))
                || (ingredient.MatchingType == EnumRecipeMatchType.Exact
                    && ingredient.ResolvedItemStack == null))
                return CraftingAllocation.Unsupported(Exceptional);
            int position = recipe.Shapeless ? requirements.Count : i / recipe.Width * snapshot.Width + i % recipe.Width;
            requirements.Add(new CraftingRequirement(position, $"{ingredient.Id ?? "ingredient"}@{i}", ingredient));
        }
        var needs = requirements.ToImmutable();
        if (needs.Sum(need => (long)need.Quantity) > int.MaxValue)
            return CraftingAllocation.Unsupported(Exceptional);
        if (!candidate && needs.Length > 0)
            return new(CraftingRecipeEvaluation.Supported(0, needs.Sum(need => need.Quantity), false,
                    needs.Select(need => new CraftingMissingRequirement(need.Id, need.Quantity, null)).ToImmutableArray()),
                needs, ImmutableArray<CraftingPlacement>.Empty, ImmutableArray<CraftingPlacement>.Empty, 0);
        var ingredients = needs.Select(need => need.GetIngredient()).ToArray();
        var groups = new List<StackGroup>();
        foreach (var source in candidate ? snapshot.Stacks : ImmutableArray<CraftingStackSnapshot>.Empty)
        {
            var stack = source.GetStack();
            if (!ingredients.Any(ingredient => ingredient.SatisfiesAsIngredient(stack, false))) continue;
            var group = groups.FirstOrDefault(group => SameStack(group.Stack, stack));
            if (group == null)
            {
                group = new StackGroup(stack);
                groups.Add(group);
            }
            group.Sources.Add(source);
            long capacity = snapshot.GridLimits.Sum(limit => (long)Math.Max(0,
                Math.Min(limit, stack.Collectible.MaxStackSize)));
            if (capacity > int.MaxValue) return CraftingAllocation.Unsupported(Exceptional);
            group.Quantity = (int)Math.Min(group.Quantity + (long)source.Quantity, capacity);
        }

        // Native merges may transform ignored attributes (temperature, transitions, etc.).
        // Do not assume that those transformations preserve ingredient predicates.
        for (int i = 0; i < groups.Count; i++)
            for (int j = i + 1; j < groups.Count; j++)
                if (CanMerge(groups[i].Stack, groups[j].Stack))
                    return CraftingAllocation.Unsupported("vanillaexpanded:craftingmenu-unsupported-merge");

        var options = new List<int>[needs.Length];
        var limits = new int[needs.Length, groups.Count];
        var durability = new int[needs.Length, groups.Count];
        for (int i = 0; i < needs.Length; i++)
        {
            options[i] = new List<int>();
            var ingredient = ingredients[i];
            for (int g = 0; g < groups.Count; g++)
            {
                var stack = CraftingStackSnapshot.Copy(groups[g].Stack);
                stack.StackSize = needs[i].Quantity;
                if (!ingredient.SatisfiesAsIngredient(stack, false)
                    || !stack.Collectible.MatchesForCrafting(stack, recipe, ingredient)) continue;
                int cap = Math.Min(stack.Collectible.MaxStackSize, snapshot.GridLimits[needs[i].GridPosition]);
                if (!needs[i].IsConsumed) cap = Math.Min(cap, 1);
                if (cap <= 0) continue;
                limits[i, g] = cap;
                int cost = ingredient.GetConsumeProperties().DurabilityCost;
                durability[i, g] = !needs[i].IsConsumed && cost > 0
                    ? stack.Collectible.GetRemainingDurability(stack) / cost : int.MaxValue;
                options[i].Add(g);
            }
            if (options[i].Count == 0) options[i].Add(-1);
        }

        var chosen = new int[needs.Length];
        var equivalentPrevious = Enumerable.Repeat(-1, needs.Length).ToArray();
        for (int i = 0; i < needs.Length; i++)
            for (int j = i - 1; j >= 0; j--)
                if (needs[i].Quantity == needs[j].Quantity && needs[i].IsConsumed == needs[j].IsConsumed
                    && options[i].SequenceEqual(options[j])
                    && Enumerable.Range(0, groups.Count).All(g =>
                        limits[i, g] == limits[j, g] && durability[i, g] == durability[j, g]))
                {
                    equivalentPrevious[i] = j;
                    break;
                }
        int EquivalentRoot(int index)
        {
            while (equivalentPrevious[index] >= 0) index = equivalentPrevious[index];
            return index;
        }
        var equivalentSets = Enumerable.Range(0, needs.Length).GroupBy(EquivalentRoot)
            .Select(set => set.ToArray()).Where(set => set.Length > 1).ToArray();
        Solution? ordinary = null;
        Solution? shift = null;
        int required = needs.Sum(need => need.Quantity);
        void Search(int index)
        {
            if (index == needs.Length)
            {
                // Search visits assignments in stable order; the first complete ordinary
                // allocation already wins its coverage and inventory-order objectives.
                if (ordinary == null || ordinary.Coverage < required)
                {
                    var one = Solve(false, needs, groups, chosen, limits, durability, equivalentSets);
                    if (ordinary == null || Better(one, ordinary, false, equivalentSets)) ordinary = one;
                }
                var many = Solve(true, needs, groups, chosen, limits, durability, equivalentSets);
                if (shift == null || Better(many, shift, true, equivalentSets)) shift = many;
                return;
            }
            foreach (int group in options[index])
            {
                // Exchangeable positions need only one canonical variant ordering.
                if (equivalentPrevious[index] is int previous && previous >= 0 && group < chosen[previous]) continue;
                chosen[index] = group;
                Search(index + 1);
            }
        }
        Search(0);
        var first = ordinary!;
        var bulk = shift!;
        var onePlacements = Place(needs, groups, first);
        var shiftPlacements = Place(needs, groups, bulk);
        if (first.Coverage == required && (!NativeAccepts(recipe, snapshot, onePlacements, lookup)
            || !NativeAccepts(recipe, snapshot, shiftPlacements, lookup)))
            return CraftingAllocation.Unsupported(NativeMismatch);

        var missing = needs.Select((need, index) => new CraftingMissingRequirement(
                need.Id, Math.Max(0, need.Quantity - first.Quantities[index]),
                !need.IsConsumed && options[index][0] < 0 && groups.Any(group =>
                    ingredients[index].SatisfiesAsIngredient(group.Stack, false)
                    && group.Stack.Collectible.GetRemainingDurability(group.Stack)
                        < ingredients[index].GetConsumeProperties().DurabilityCost)
                    ? "vanillaexpanded:craftingmenu-insufficient-durability" : null))
            .Where(need => need.Quantity > 0).ToImmutableArray();
        return new(CraftingRecipeEvaluation.Supported(first.Coverage, required,
                first.Coverage == required, missing),
            needs, onePlacements, shiftPlacements,
            bulk.Executions == int.MaxValue && ingredients.All(ingredient =>
                !ingredient.GetConsumeProperties().Consume && ingredient.GetConsumeProperties().DurabilityCost <= 0)
                ? null : bulk.Executions);
    }

    private bool NativeAccepts(GridRecipe selected, CraftingInventorySnapshot snapshot,
        ImmutableArray<CraftingPlacement> placements, CraftingIngredientLookup lookup)
    {
        var slots = Enumerable.Range(0, snapshot.Width * snapshot.Height)
            .Select(_ => new ItemSlot(null!)).ToArray();
        foreach (var placement in placements) slots[placement.GridPosition].Itemstack = placement.GetStack();
        // Native candidate order follows input slots, then ingredient-index order, not registry order.
        var candidates = new List<GridRecipe>();
        var seen = new HashSet<GridRecipe>(ReferenceEqualityComparer.Instance);
        foreach (var slot in slots)
        {
            if (slot.Itemstack == null) continue;
            foreach (var source in lookup.Match(slot.Itemstack))
                        if (source is GridRecipe { Enabled: true } recipe && seen.Add(recipe))
                            candidates.Add(recipe);
        }
        foreach (bool shapeless in new[] { false, true })
            foreach (var recipe in candidates)
                if (recipe.Shapeless == shapeless && recipe.Matches(player, world, slots, snapshot.Width))
                    return ReferenceEquals(recipe, selected);
        return false;
    }

    private static Solution Solve(bool shift, ImmutableArray<CraftingRequirement> needs,
        List<StackGroup> groups, int[] chosen, int[,] limits, int[,] durability, int[][] equivalentSets)
    {
        int coverage = 0;
        int executions = int.MaxValue;
        var quantities = new int[needs.Length];
        Span<int> positions = stackalloc int[needs.Length];
        Span<int> caps = stackalloc int[needs.Length];
        int required = 0;
        for (int i = 0; i < needs.Length; i++)
        {
            required += needs[i].Quantity;
            if (chosen[i] < 0) executions = 0;
        }
        for (int g = 0; g < groups.Count; g++)
        {
            int cover = 0, tools = 0, consumed = 0;
            for (int i = 0; i < needs.Length; i++)
            {
                if (chosen[i] != g) continue;
                cover += Math.Min(needs[i].Quantity, limits[i, g]);
                if (needs[i].IsConsumed) consumed += needs[i].Quantity;
                else tools++;
                executions = Math.Min(executions, needs[i].IsConsumed
                    ? limits[i, g] / needs[i].Quantity : durability[i, g]);
            }
            coverage += Math.Min(groups[g].Quantity, cover);
            if (groups[g].Quantity < tools) executions = 0;
            if (consumed > 0) executions = Math.Min(executions, Math.Max(0, groups[g].Quantity - tools) / consumed);
        }
        if (coverage != required) executions = 0;
        for (int g = 0; g < groups.Count; g++)
        {
            int count = 0;
            int available = groups[g].Quantity;
            for (int i = 0; i < needs.Length; i++)
            {
                if (chosen[i] != g) continue;
                positions[count++] = i;
                caps[i] = Math.Min(needs[i].Quantity, limits[i, g]);
                if (shift && executions > 0)
                    quantities[i] = needs[i].IsConsumed ? checked(needs[i].Quantity * executions) : 1;
                available -= quantities[i];
            }
            Fill(positions[..count], quantities, caps, ref available);
            if (shift)
            {
                foreach (int i in positions[..count]) caps[i] = limits[i, g];
                Fill(positions[..count], quantities, caps, ref available);
            }
        }
        var assignment = (int[])chosen.Clone();
        if (shift)
            foreach (var set in equivalentSets)
            {
                for (int a = 1; a < set.Length; a++)
                    for (int b = a; b > 0; b--)
                    {
                        int left = set[b - 1], right = set[b];
                        if (quantities[left] > quantities[right]
                            || (quantities[left] == quantities[right] && assignment[left] <= assignment[right])) break;
                        (assignment[left], assignment[right]) = (assignment[right], assignment[left]);
                        (quantities[left], quantities[right]) = (quantities[right], quantities[left]);
                    }
            }
        return new(assignment, quantities, coverage, executions);
    }

    private static void Fill(ReadOnlySpan<int> positions, int[] quantities, ReadOnlySpan<int> caps, ref int available)
    {
        if (available <= 0 || positions.Length == 0) return;
        int low = 0;
        int high = 0;
        foreach (int i in positions) high = Math.Max(high, caps[i]);
        while (low < high)
        {
            int middle = low + (int)(((long)high - low + 1) / 2);
            long cost = 0;
            foreach (int i in positions) cost += Math.Max(0, Math.Min(caps[i], middle) - quantities[i]);
            if (cost <= available) low = middle;
            else high = middle - 1;
        }
        foreach (int i in positions)
        {
            int add = Math.Max(0, Math.Min(caps[i], low) - quantities[i]);
            quantities[i] += add;
            available -= add;
        }
        while (available > 0)
        {
            int next = -1;
            foreach (int i in positions)
                if (quantities[i] < caps[i] && (next < 0 || quantities[i] < quantities[next])) next = i;
            if (next < 0) break;
            quantities[next]++;
            available--;
        }
    }

    private static bool Better(Solution candidate, Solution current, bool shift, int[][] equivalentSets)
    {
        if (candidate.Coverage != current.Coverage) return candidate.Coverage > current.Coverage;
        if (shift && candidate.Executions != current.Executions) return candidate.Executions > current.Executions;
        if (shift && candidate.Quantities.Sum() != current.Quantities.Sum())
            return candidate.Quantities.Sum() > current.Quantities.Sum();
        if (shift)
            foreach (var set in equivalentSets)
                foreach (int i in set.Reverse())
                    if (candidate.Quantities[i] != current.Quantities[i])
                        return candidate.Quantities[i] > current.Quantities[i];
        for (int i = 0; i < candidate.Groups.Length; i++)
        {
            int left = candidate.Quantities[i] == 0 ? int.MaxValue : candidate.Groups[i];
            int right = current.Quantities[i] == 0 ? int.MaxValue : current.Groups[i];
            if (left != right) return left < right;
        }
        return false;
    }

    private static ImmutableArray<CraftingPlacement> Place(ImmutableArray<CraftingRequirement> needs,
        List<StackGroup> groups, Solution solution)
    {
        var result = ImmutableArray.CreateBuilder<CraftingPlacement>();
        var used = new Dictionary<CraftingSlotId, int>();
        for (int i = 0; i < needs.Length; i++)
        {
            int amount = solution.Quantities[i];
            if (amount == 0) continue;
            var group = groups[solution.Groups[i]];
            var sources = ImmutableArray.CreateBuilder<CraftingSourceAllocation>();
            int remaining = amount;
            foreach (var source in group.Sources)
            {
                int taken = Math.Min(remaining, source.Quantity - used.GetValueOrDefault(source.Slot));
                if (taken <= 0) continue;
                sources.Add(new(source.Slot, taken));
                used[source.Slot] = used.GetValueOrDefault(source.Slot) + taken;
                remaining -= taken;
                if (remaining == 0) break;
            }
            if (remaining != 0) throw new InvalidOperationException("Allocation exceeds its snapshot sources.");
            var stack = CraftingStackSnapshot.Copy(group.Stack);
            stack.StackSize = amount;
            result.Add(new(needs[i].GridPosition, stack, sources.ToImmutable()));
        }
        return result.ToImmutable();
    }

    private bool SameStack(ItemStack left, ItemStack right) =>
        left.Class == right.Class && left.Id == right.Id && left.Attributes.Equals(world, right.Attributes)
        && left.TempAttributes.Equals(world, right.TempAttributes);

    private static bool CanMerge(ItemStack left, ItemStack right)
    {
        var sink = CraftingStackSnapshot.Copy(left);
        sink.StackSize = 0;
        return sink.Collectible.GetMergableQuantity(sink, right, EnumMergePriority.AutoMerge) > 0;
    }

    private sealed class StackGroup
    {
        internal StackGroup(ItemStack stack) => Stack = CraftingStackSnapshot.Copy(stack);
        internal ItemStack Stack { get; }
        internal int Quantity { get; set; }
        internal List<CraftingStackSnapshot> Sources { get; } = new();
    }

    private sealed record Solution(int[] Groups, int[] Quantities, int Coverage, int Executions);
}
