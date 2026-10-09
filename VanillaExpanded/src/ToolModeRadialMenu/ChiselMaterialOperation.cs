using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Stages one material through native inventory packets and the existing chisel tool-mode operation.</summary>
internal static class ChiselMaterialOperation
{
    #region Public API
    /// <summary>Applies a local prediction and returns its known remainder, without claiming server acceptance.</summary>
    internal static bool TryApply(ICoreClientAPI api, ItemChisel chisel, ItemSlot toolSlot, IClientPlayer player,
        BlockSelection selection, BlockEntityChisel target, ChiselMaterialCandidate candidate, Func<bool> contextCurrent,
        Func<bool> worldCurrent)
    {
        ItemSlot? source = Resolve(player.InventoryManager, candidate);
        ItemSlot cursor = player.InventoryManager.MouseItemSlot;
        if (!contextCurrent() || source is null || !cursor.Empty
            || !ReferenceEquals(source.Itemstack, candidate.SourceStack)
            || !Matches(api, source.Itemstack, candidate.Stack, candidate.Stack.StackSize)
            || !ChiselMaterialCandidates.IsEligible(api, player, selection.Position, target, source.Itemstack!)) return false;
        var expected = candidate.Stack.Clone();
        expected.StackSize = 1;
        ItemStack? staged = null;
        ItemStack? remaining = candidate.Stack.StackSize > 1 ? candidate.SourceStack : null;
        int remainingCount = candidate.Stack.StackSize - 1;
        bool applied = false;
        bool returned = false;
        bool returnAttempted = false;
        try
        {
            if (!contextCurrent() || !cursor.Empty || !ReferenceEquals(source.Itemstack, candidate.SourceStack)
                || !Matches(api, source.Itemstack, candidate.Stack, candidate.Stack.StackSize)) return false;
            var stage = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
            object? packet = player.InventoryManager.TryTransferTo(source, cursor, ref stage);
            if (packet is null) return false;
            if (stage.MovedQuantity == 1 && Matches(api, cursor.Itemstack, expected, 1)) staged = cursor.Itemstack;
            api.Network.SendPacketClient(packet);
            if (staged is null) return false;
            // Inventory callbacks can change the tool, target or cursor. Never fall through to an empty-cursor mode change.
            if (contextCurrent() && ReferenceEquals(cursor.Itemstack, staged)
                && ReferenceEquals(source.Itemstack, remaining) && source.StackSize == remainingCount
                && (remaining is null || Matches(api, remaining, candidate.Stack, remainingCount)))
            {
                SkillItem[]? modes = chisel.GetToolModes(toolSlot, player, selection);
                int addMaterial = modes is null ? -1 : Array.FindIndex(modes, mode => mode?.Code?.Path == "addmat");
                if (addMaterial >= 0 && ChiselMaterialCandidates.IsEligible(api, player, selection.Position, target, staged!)
                    && contextCurrent() && ReferenceEquals(cursor.Itemstack, staged) && Matches(api, staged, expected, 1))
                {
                    ToolModeSelection.Apply(api, chisel, toolSlot, player, selection, addMaterial);
                    applied = true;
                }
            }
        }
        catch (Exception exception)
        {
            api.Logger.Warning("Chisel material operation stopped: {0}", exception.Message);
        }
        finally
        {
            // Complete only the known synchronous remainder return, even if the picker closed during a callback.
            // A changed cursor or a departed world belongs to native correction, never guessed replacement stacks.
            if (staged is not null && worldCurrent() && ReferenceEquals(cursor.Itemstack, staged)
                && Matches(api, staged, expected, 1) && ReferenceEquals(Resolve(player.InventoryManager, candidate), source)
                && ReferenceEquals(source.Itemstack, remaining) && source.StackSize == remainingCount
                && (remaining is null || Matches(api, remaining, candidate.Stack, remainingCount)))
            {
                try
                {
                    returnAttempted = true;
                    var restore = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
                    object? packet = player.InventoryManager.TryTransferTo(cursor, source, ref restore);
                    if (packet is not null) api.Network.SendPacketClient(packet);
                    returned = packet is not null && restore.MovedQuantity == 1 && cursor.Empty;
                }
                catch (Exception exception)
                {
                    api.Logger.Warning("Chisel material return stopped: {0}", exception.Message);
                }
            }
            // If the source cannot accept the item, use the owning game's drop operation for this one known remainder.
            if (!returned && staged is not null && worldCurrent() && ReferenceEquals(cursor.Itemstack, staged)
                && Matches(api, staged, expected, 1))
            {
                try
                {
                    returnAttempted = true;
                    if (player.InventoryManager.DropMouseSlotItems(false) && cursor.Empty)
                        api.TriggerIngameError(typeof(ChiselMaterialOperation), "chisel-material-dropped",
                            Lang.Get("vanillaexpanded:chisel-material-dropped"));
                }
                catch (Exception exception)
                {
                    api.Logger.Warning("Chisel material drop stopped: {0}", exception.Message);
                }
            }
        }
        return applied && cursor.Empty && (!returnAttempted || returned);
    }
    #endregion

    #region Private
    /// <summary>Resolves an address only within current player-owned ordinary storage or the physical offhand.</summary>
    private static ItemSlot? Resolve(IPlayerInventoryManager manager, ChiselMaterialCandidate candidate)
    {
        bool hotbar = ReferenceEquals(candidate.Inventory, manager.GetOwnInventory(GlobalConstants.hotBarInvClassName));
        bool backpack = ReferenceEquals(candidate.Inventory, manager.GetOwnInventory(GlobalConstants.backpackInvClassName));
        if ((!hotbar && !backpack) || candidate.SlotIndex < 0 || candidate.SlotIndex >= candidate.Inventory.Count) return null;
        ItemSlot slot = candidate.Inventory[candidate.SlotIndex];
        if (!ReferenceEquals(slot.Inventory, candidate.Inventory)) return null;
        return backpack ? slot is ItemSlotBagContent ? slot : null
            : slot.GetType() == typeof(ItemSlotSurvival)
                || slot is ItemSlotOffhand && ReferenceEquals(slot, manager.OffhandHotbarSlot) ? slot : null;
    }

    /// <summary>Checks exact stack attributes and expected quantity, including composite material arrays.</summary>
    private static bool Matches(ICoreClientAPI api, ItemStack? current, ItemStack snapshot, int quantity)
        => current is not null && current.StackSize == quantity && current.Equals(api.World, snapshot, Array.Empty<string>())
            && current.Attributes.Equals(api.World, snapshot.Attributes);
    #endregion
}
