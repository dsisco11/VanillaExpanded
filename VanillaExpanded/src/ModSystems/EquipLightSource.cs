using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded;
internal record struct SlotId(string InvClassName, int SlotIndex);
public class EquipLightSource : ModSystem
{
    #region Fields
    private ICoreClientAPI? api;
    /// <summary> Tracks the last inventory slot that was swapped out for a light source. </summary>
    internal ItemSlot? previousSlot = null;
    #endregion

    #region Accessors
    public ILogger Logger => api!.Logger;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        RegisterInputHandlers();
    }
    #endregion

    #region Input Handling
    protected void RegisterInputHandlers()
    {
        if (api is null)         
        {
            throw new InvalidOperationException("Cannot register input handlers: ICoreClientAPI is null.");
        }

        api.Input.RegisterHotKey("ve.equipLightSourceToOffhand", Lang.Get($"{this.Mod.Info.ModID}:ve-hotkey-equiplightsource-offhand"), GlKeys.F, HotkeyType.InventoryHotkeys);
        api.Input.SetHotKeyHandler("ve.equipLightSourceToOffhand", HandleKeyBind_EquipLightSourceToOffhand);

        api.Input.RegisterHotKey("ve.equipLightSourceToHotbar", Lang.Get($"{this.Mod.Info.ModID}:ve-hotkey-equiplightsource-hotbar"), GlKeys.F, HotkeyType.InventoryHotkeys, shiftPressed: true);
        api.Input.SetHotKeyHandler("ve.equipLightSourceToHotbar", HandleKeyBind_EquipLightSourceToHotbar);
    }

    private bool HandleKeyBind_EquipLightSourceToOffhand(KeyCombination t1)
    {
        return OnHotKeyPressed(true);
    }

    private bool HandleKeyBind_EquipLightSourceToHotbar(KeyCombination t1)
    {
        return OnHotKeyPressed(false);
    }

    internal bool OnHotKeyPressed(bool useOffhand)
    {
        if (api is null)
            return false;

        // Figure out where we want to move the light source.
        /** Priorities:
         * 1. If light is in left hand and we want to equip to left hand, then move it back to previous slot (if any).
         * 2. If light is in right hand and we want to equip to right hand, then move it back to previous slot (if any).
         * 3. If light is in left hand and we want to equip to right hand (or vice versa), swap it.
         * 4. Otherwise, find the best light source in hotbar/backpack and equip it to the desired hand.
         */

        ItemSlot desiredHand = useOffhand ? api!.World.Player.Entity.LeftHandItemSlot : api!.World.Player.InventoryManager.ActiveHotbarSlot;
        ItemSlot? sourceSlot = null;
        ItemSlot? targetSlot = null;

        // There are 2 scenarios, either we are moving the light into our desired hand, or we are moving it back out of our hand.
        bool isEquipping = !IsLightSource(desiredHand);// lightSourceSlot != desiredHand;
        if (isEquipping)
        {
            targetSlot = desiredHand;
            // Find best light source to equip
            ItemSlot? lightSourceSlot = ResolveLightSourceSlot(api!.World.Player, out bool isInLeftHand, out bool isInRightHand);
            if (lightSourceSlot?.Itemstack is null)
            {// player has no light sources
                api.Logger.Debug("[EquipLightSource] no light source found in inventory.");
                return false;
            }
            sourceSlot = lightSourceSlot;
        }
        else
        {// Moving light source back out of hand
            sourceSlot = desiredHand;
            // First, try and find an empty slot in the backpack/hotbar to move it to.
            IEnumerable<IInventory> validInventories = api!.World.Player.InventoryManager.GetBagInventories();
            IEnumerable<WeightedSlot> bestSlots = validInventories.Select(inv => inv.GetBestSuitedSlot(sourceSlot));
            WeightedSlot? bestOverallSlot = bestSlots.Where(static ws => ws?.slot is not null)
                                                     .MaxBy(static ws => ws!.weight);
            if (bestOverallSlot is not null)
            {
                // Record this slot as the previous slot for future swaps.
                targetSlot = previousSlot = bestOverallSlot.slot;
            }
            else
            {// No empty slots available, so just move it to the previous slot if it is still valid.
                if (previousSlot is null)
                {
                    api.Logger.Debug("[EquipLightSource] cannot move light source out of hand, no previous slot recorded and no empty slots in inventory.");
                    return false;
                }
                if (!previousSlot.CanHold(sourceSlot))
                {
                    api.Logger.Debug("[EquipLightSource] cannot move light source out of hand, previous slot cannot hold light source.");
                    return false;
                }
                targetSlot = previousSlot;
            }
        }

        if (sourceSlot is null)
        {
            api.Logger.Warning("[EquipLightSource] cannot swap item-slots, source-slot is null.");
            return false;
        }

        if (targetSlot is null)
        {
            api.Logger.Warning("[EquipLightSource] cannot swap item-slots, target-slot is null.");
            return false;
        }

        var player = api.World.Player;
        Debug.Assert(player.InventoryManager.OpenedInventories.Contains(targetSlot.Inventory), "Target inventory is not opened.");
        Debug.Assert(player.InventoryManager.OpenedInventories.Contains(sourceSlot.Inventory), "Source inventory is not opened.");

        int targetSlotId = targetSlot.Inventory.GetSlotId(targetSlot);
        ItemStackMoveOperation op = new(api!.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, sourceSlot.StackSize);
        var packet = player.InventoryManager.TryTransferTo(sourceSlot, targetSlot, ref op);
        if (packet is not null)
        {
            api.Logger.Audit("[EquipLightSource] swapped light source '{0}' into {1} hand.", sourceSlot?.Itemstack?.GetName(), useOffhand ? "left" : "right");
            api.Network.SendPacketClient(packet);
        }
        else
        {
            api.Logger.Warning("[EquipLightSource] failed to create flip-items packet for swapping light source.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the most relevant light source item for the player according to a list of priorities.
    /// </summary>
    protected static ItemSlot? ResolveLightSourceSlot(in IClientPlayer player, out bool isInLeftHand, out bool isInRightHand)
    {
        IPlayerInventoryManager playerInventory = player.InventoryManager;
        isInLeftHand = false;
        isInRightHand = false;

        // Check offhand slot first
        ItemSlot offhandSlot = player.Entity.LeftHandItemSlot;
        if (!offhandSlot.Empty)
        {
            if (IsLightSource(offhandSlot))
            {
                isInLeftHand = true;
                return offhandSlot;
            }
        }

        // Check active hotbar slot next
        ItemSlot activeHotbarSlot = playerInventory.ActiveHotbarSlot;
        if (!activeHotbarSlot.Empty)
        {
            if (IsLightSource(activeHotbarSlot))
            {
                isInRightHand = true;
                return activeHotbarSlot;
            }
        }

        // Find brightest light source in inventory
        IEnumerable<ItemSlot> backpackAndHotbarSlots = GetPlayerBagSlots(player);
        ItemSlot? brightestSlot = backpackAndHotbarSlots.Where(static slot => !slot.Empty && IsLightSource(slot))
                                                        .MaxBy(static slot => slot.Itemstack.Collectible.LightHsv[2]);
        if (!brightestSlot?.Empty ?? false)
        {
            return brightestSlot;
        }

        return null;
    }
    #endregion

    #region Private Methods

    private static IEnumerable<ItemSlot> GetPlayerBagSlots(in IClientPlayer player)
    {
        IEnumerable<IEnumerable<ItemSlot>> inventories = player.InventoryManager.GetBagInventories();
        // Flatten the inventories into a single sequence of item slots
        return inventories.SelectMany(static inv => inv);
    }

    /// <summary>
    /// Determines if the given item slot contains a light source.
    /// </summary>
    private static bool IsLightSource(in ItemSlot? slot)
    {
        if (slot?.Empty ?? true) return false;
        if (slot?.Itemstack?.Collectible is null) return false;
        CollectibleObject item = slot.Itemstack.Collectible;
        return item.LightHsv[2] > 0;
    }

    /// <summary>
    /// Determines if the given collectible object is a light source.
    /// </summary>
    private static bool IsLightSource(in CollectibleObject? item)
    {
        return (item?.LightHsv[2] ?? 0) > 0;
    }
    #endregion
}
