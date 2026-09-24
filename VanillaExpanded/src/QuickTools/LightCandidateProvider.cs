using System;
using VanillaExpanded.Lighting;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.QuickTools;

/// <summary>Adapts the shared light policy to one fixed virtual quick-tool entry.</summary>
public sealed class LightCandidateProvider : IQuickToolCandidateProvider
{
    /// <summary>Gets the fixed light identifier.</summary>
    public string EntryId => QuickToolLayout.LightId;

    #region Selection
    /// <summary>Resolves the legacy winner, then checks that its source is supported for quick-tool movement.</summary>
    public QuickToolCandidate? Resolve(IPlayerInventoryManager manager, ItemSlot offhand, ItemSlot? excludedHand = null)
    {
        ItemSlot? selected = LightSourceSelection.ResolveLightSourceSlot(manager, offhand, out _, out _, excludedHand);
        if (selected is null || selected.Empty) return null;
        IInventory? hotbar = manager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        IInventory? backpack = manager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (hotbar is not null)
        {
            for (int i = 0; i < hotbar.Count; i++)
            {
                if (!ReferenceEquals(hotbar[i], selected)) continue;
                if (ReferenceEquals(selected, offhand) ? selected is not ItemSlotOffhand : selected.GetType() != typeof(ItemSlotSurvival)) return null;
                return new QuickToolCandidate(EntryId, hotbar, i, selected, selected.Itemstack, 0, 0);
            }
        }
        if (backpack is not null)
        {
            for (int i = 0; i < backpack.Count; i++)
            {
                if (!ReferenceEquals(backpack[i], selected)) continue;
                return selected is ItemSlotBagContent
                    ? new QuickToolCandidate(EntryId, backpack, i, selected, selected.Itemstack, 0, 0) : null;
            }
        }
        return null;
    }
    #endregion
}
