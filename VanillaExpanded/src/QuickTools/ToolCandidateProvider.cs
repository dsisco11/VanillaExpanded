using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.QuickTools;

/// <summary>Finds the highest-tier, most worn usable tool in one supported category.</summary>
public sealed class ToolCandidateProvider : IQuickToolCandidateProvider
{
    private readonly EnumTool? category;
    private readonly Action<string>? diagnostic;
    private readonly TagSet categoryTags;
    private readonly bool hasCategoryTags;

    /// <summary>Gets the fixed category identifier.</summary>
    public string EntryId { get; }

    /// <summary>Creates a provider for one supported category, optionally recognizing matching collectible tags.</summary>
    public ToolCandidateProvider(EnumTool category, Action<string>? diagnostic = null, ITagRegistry<TagSet>? tagRegistry = null)
    {
        this.category = category;
        this.diagnostic = diagnostic;
        EntryId = QuickToolLayout.GetToolId(category) ?? throw new ArgumentOutOfRangeException(nameof(category));
        hasCategoryTags = tagRegistry?.TryCreateTagSet(out categoryTags,
            ["tool", $"tool-{category.ToString().ToLowerInvariant()}"]) == TagRegistryError.None;
    }

    /// <summary>Creates a provider for one discovered tool tag without requiring an EnumTool value.</summary>
    public ToolCandidateProvider(string toolTag, Action<string>? diagnostic = null, ITagRegistry<TagSet>? tagRegistry = null)
    {
        if (!QuickToolLayout.TryGetToolTag(toolTag, out string normalizedTag)) throw new ArgumentOutOfRangeException(nameof(toolTag));
        this.diagnostic = diagnostic;
        EntryId = QuickToolLayout.GetToolId(normalizedTag);
        hasCategoryTags = tagRegistry?.TryCreateTagSet(out categoryTags, ["tool", normalizedTag]) == TagRegistryError.None;
    }

    #region Selection
    /// <summary>Scans only supported player-owned ordinary hotbar and bag-content slots.</summary>
    public QuickToolCandidate? Resolve(IPlayerInventoryManager manager, ItemSlot offhand, ItemSlot? excludedHand = null)
    {
        QuickToolCandidate? best = null;
        Scan(manager.GetOwnInventory(GlobalConstants.hotBarInvClassName), false, excludedHand, ref best);
        Scan(manager.GetOwnInventory(GlobalConstants.backpackInvClassName), true, excludedHand, ref best);
        return best;
    }

    /// <summary>Inspects one inventory in stable slot order, retaining the earlier slot on exact ties.</summary>
    private void Scan(IInventory? inventory, bool backpack, ItemSlot? excludedHand, ref QuickToolCandidate? best)
    {
        if (inventory is null) return;
        for (int index = 0; index < inventory.Count; index++)
        {
            ItemSlot? slot = inventory[index];
            if (slot is null || ReferenceEquals(slot, excludedHand)) continue;
            if (backpack ? slot is not ItemSlotBagContent : slot.GetType() != typeof(ItemSlotSurvival)) continue;
            if (slot.Empty) continue;
            try
            {
                ItemStack? stack = slot.Itemstack;
                if (stack?.Collectible is not CollectibleObject collectible) continue;
                if (!MatchesCategory(collectible, stack, slot)) continue;
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

    /// <summary>Accepts matching tool tags first, retaining legacy metadata as a fallback for untagged definitions.</summary>
    private bool MatchesCategory(CollectibleObject collectible, ItemStack stack, ItemSlot slot)
        => hasCategoryTags && categoryTags.IsFullyContainedIn(collectible.GetTags(stack))
            || category is EnumTool legacyCategory && collectible.GetTool(slot) == legacyCategory;
    #endregion
}
