using System;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded;

using VanillaExpanded.ModSystems;
using VanillaExpanded.Lighting;
internal record struct SlotId(string InvClassName, int SlotIndex);
public class EquipLightSource : ModSystem
{
    #region Fields
    private ICoreClientAPI? api;
    /// <summary> Tracks the last hotbar slot that was swapped out for a light source. </summary>
    internal ItemSlot? previousHotbarSlot = null;
    /// <summary> Tracks the last backpack slot that was swapped out for a light source. </summary>
    internal ItemSlot? previousBackpackSlot = null;
    #endregion

    #region Accessors
    public ILogger Logger => api!.Logger;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

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
        if (!VanillaExpandedModSystem.Config.EnableEquipLightHotkey) return false;
        return OnHotKeyPressed(true);
    }

    private bool HandleKeyBind_EquipLightSourceToHotbar(KeyCombination t1)
    {
        if (!VanillaExpandedModSystem.Config.EnableEquipLightHotkey) return false;
        return OnHotKeyPressed(false);
    }

    internal bool OnHotKeyPressed(bool useOffhand)
    {
        if (api is null)
        {
            return false;
        }

        IClientPlayer player = api.World.Player;
        return OnHotKeyPressed(player.InventoryManager, player.Entity.LeftHandItemSlot, useOffhand);
    }

    /// <summary>
    /// Equips or unequips a light source using the player's inventory and offhand slot.
    /// </summary>
    internal bool OnHotKeyPressed(IPlayerInventoryManager playerInventory, ItemSlot offhandSlot, bool useOffhand)
    {
        if (api is null)
        {
            return false;
        }

        ItemSlot? lightSourceSlot = LightSourceSelection.ResolveLightSourceSlot(playerInventory, offhandSlot, out bool isInLeftHand, out bool isInRightHand);
        if (lightSourceSlot is null)
        {// player has no light sources
            return false;
        }

        // Figure out where we want to move the light source.
        /** Priorities:
         * 1. If light is in left hand and we want to equip to left hand, then move it back to previous slot (if any).
         * 2. If light is in right hand and we want to equip to right hand, then move it back to previous slot (if any).
         * 3. If light is in left hand and we want to equip to right hand (or vice versa), swap it.
         * 4. Otherwise, find the best light source in hotbar/backpack and equip it to the desired hand.
         */

        ItemSlot desiredHand = useOffhand ? offhandSlot : playerInventory.ActiveHotbarSlot;
        ItemSlot? sourceSlot = lightSourceSlot;
        ItemSlot? targetSlot = null;

        // There are 2 scenarios, either we are moving the light into our desired hand, or we are moving it back out of our hand.
        bool isEquipping = lightSourceSlot != desiredHand;
        if (isEquipping)
        {
            targetSlot = desiredHand;
        }
        else
        {// Moving light source back out of hand
            // check which of our previous slots are still valid (i.e. it could still hold the light source), and nullify the invalid ones
            if (previousBackpackSlot is not null && !previousBackpackSlot.CanHold(lightSourceSlot))
            {
                previousBackpackSlot = null;
            }
            if (previousHotbarSlot is not null && !previousHotbarSlot.CanHold(lightSourceSlot))
            {
                previousHotbarSlot = null;
            }

            // Target slot is either the previous backpack/hotbar slot, or we need to find a new slot for it.
            // When unequipping, we want to move to an EMPTY slot first to avoid merging with existing stacks.
            ItemSlot? previousSlot = previousBackpackSlot ?? previousHotbarSlot;
            targetSlot = previousSlot;// set target slot to previous by default, override with a new value if needed according to additional logic below.
            if (previousSlot is null)
            {
                // Find the first EMPTY slot in backpack or hotbar (avoid GetBestSuitedSlot which prefers merging)
                IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
                
                // First try to find an empty slot that can hold the item
                targetSlot = FindEmptySlotThatCanHold(backpack, lightSourceSlot);
                
                if (targetSlot is null)
                {
                    IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
                    targetSlot = FindEmptySlotThatCanHold(hotbar, lightSourceSlot);
                }
                
                // If no empty slots found, fall back to GetBestSuitedSlot (which may merge)
                if (targetSlot is null)
                {
                    WeightedSlot? bpBestSlot = backpack?.GetBestSuitedSlot(lightSourceSlot);
                    if (bpBestSlot is not null)
                    {
                        targetSlot = bpBestSlot.slot;
                    }
                    else
                    {
                        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
                        WeightedSlot? hbBestSlot = hotbar?.GetBestSuitedSlot(lightSourceSlot);
                        if (hbBestSlot is not null)
                        {
                            targetSlot = hbBestSlot.slot;
                        }
                    }
                }
            }
            else
            {
                previousBackpackSlot = null;
                previousHotbarSlot = null;
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

        int targetSlotId = targetSlot.Inventory.GetSlotId(targetSlot);
        var packet = targetSlot.Inventory.TryFlipItems(targetSlotId, sourceSlot);
        if (packet is not null)
        {
            api.Network.SendPacketClient(packet);
            targetSlot.MarkDirty();
            sourceSlot.MarkDirty();
        }

        return true;
    }

    #endregion

    #region Private Methods
    /// <summary>
    /// Finds the first empty slot in the inventory that can hold the item from the source slot.
    /// </summary>
    /// <returns>The first empty slot that can hold the item, or null if none found.</returns>
    private static ItemSlot? FindEmptySlotThatCanHold(IInventory? inventory, ItemSlot sourceSlot)
        => inventory?.FirstOrDefault(slot => slot.Empty && slot.CanHold(sourceSlot));

    #endregion
}
