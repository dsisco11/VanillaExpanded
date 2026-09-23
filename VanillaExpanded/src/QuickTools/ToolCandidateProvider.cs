using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.QuickTools;

/// <summary>Finds the highest-tier, most worn usable tool in one supported category.</summary>
public sealed class ToolCandidateProvider(EnumTool category, Action<string>? diagnostic = null) : IQuickToolCandidateProvider
{
    /// <summary>Gets the fixed category identifier.</summary>
    public string EntryId { get; } = QuickToolLayout.GetToolId(category) ?? throw new ArgumentOutOfRangeException(nameof(category));

    #region Selection
    /// <summary>Scans only supported player-owned ordinary hotbar and bag-content slots.</summary>
    public QuickToolCandidate? Resolve(IPlayerInventoryManager manager, ItemSlot offhand)
    {
        QuickToolCandidate? best = null;
        Scan(manager.GetOwnInventory(GlobalConstants.hotBarInvClassName), false, ref best);
        Scan(manager.GetOwnInventory(GlobalConstants.backpackInvClassName), true, ref best);
        return best;
    }

    /// <summary>Inspects one inventory in stable slot order, retaining the earlier slot on exact ties.</summary>
    private void Scan(IInventory? inventory, bool backpack, ref QuickToolCandidate? best)
    {
        if (inventory is null) return;
        for (int index = 0; index < inventory.Count; index++)
        {
            ItemSlot? slot = inventory[index];
            if (slot is null) continue;
            if (backpack ? slot is not ItemSlotBagContent : slot.GetType() != typeof(ItemSlotSurvival)) continue;
            if (slot.Empty) continue;
            try
            {
                ItemStack? stack = slot.Itemstack;
                if (stack?.Collectible is not CollectibleObject collectible) continue;
                if (collectible.GetTool(slot) != category) continue;
                int tier = collectible.GetToolTier(slot);
                int maxDurability = collectible.GetMaxDurability(stack);
                int remaining = maxDurability <= 0 ? int.MaxValue : collectible.GetRemainingDurability(stack);
                if (maxDurability > 0 && remaining <= 0) continue;
                // Enumeration is hotbar then backpack, both ascending, so exact ties retain the first.
                if (best is null || tier > best.Tier || (tier == best.Tier && remaining < best.RemainingDurability))
                    best = new QuickToolCandidate(EntryId, inventory, index, slot, stack, tier, remaining);
            }
            catch (Exception exception)
            {
                // Modded metadata can fail; exclude that slot without fabricating a comparable rank.
                diagnostic?.Invoke($"Unable to rank {EntryId} at {index}: {exception.Message}");
            }
        }
    }
    #endregion
}
