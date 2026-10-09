using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Evaluates policy against engine contents without acquiring sessions, mutating slots or preparing workspaces.</summary>
internal static class AutoStashAssessor
{
    #region Public API
    /// <summary>Uses available live slots for automatic capacity, or explicitly reports unknown capacity for contents-only views.</summary>
    public static AutoStashAssessment Assess(AutoStashPolicy policy, IInventory? target, IInventory? backpack, IInventory? hotbar)
    {
        HashSet<int> candidates = [];
        HashSet<int> available = [];
        foreach (AutoStashSourcePass pass in policy.GetSourcePasses(backpack, hotbar))
        {
            foreach (ItemSlot source in pass.Inventory)
            {
                if (source.Empty || source.Itemstack?.Collectible is null) continue;
                int id = source.Itemstack.Collectible.Id;
                if (policy.IsCandidate(source.Itemstack, pass)) candidates.Add(id);
                if (target is not null && AutoStashPlanner.CanTransfer(target, policy, source, pass, includeDirect: false))
                    available.Add(id);
            }
        }

        AutoStashCapacity capacity = target is null ? AutoStashCapacity.Unknown
            : available.Count > 0 ? AutoStashCapacity.Available : AutoStashCapacity.Unavailable;
        HashSet<int> displayIds = target is null ? candidates : available;
        HashSet<int> represented = [];
        List<ItemStack> display = [];
        // Help order is source order, not bloomery's ore/fuel execution passes. The first stack of each available ID
        // remains the representative even when another stack of that ID supplied the capacity evidence.
        foreach (ItemSlot source in (backpack ?? Enumerable.Empty<ItemSlot>()).Concat(hotbar ?? Enumerable.Empty<ItemSlot>()))
        {
            if (!source.Empty && source.Itemstack?.Collectible is not null
                && displayIds.Contains(source.Itemstack.Collectible.Id) && represented.Add(source.Itemstack.Collectible.Id))
                display.Add(source.Itemstack.Clone());
        }
        return new AutoStashAssessment(candidates, available, capacity, [.. display]);
    }
    #endregion
}
