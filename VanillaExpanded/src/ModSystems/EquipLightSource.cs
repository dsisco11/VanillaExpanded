using System;
using System.Diagnostics.CodeAnalysis;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded;
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

    #region Lifecycle
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

        ItemSlot? lightSourceSlot = ResolveLightSourceSlot(api!.World.Player, out bool isInLeftHand, out bool isInRightHand);
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

        ItemSlot desiredHand = useOffhand ? api!.World.Player.Entity.LeftHandItemSlot : api!.World.Player.InventoryManager.ActiveHotbarSlot;
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

            // Target slot is either the previous backpack/hotbar slot, or we need to find a new slot for it (best available backpack slot or best available hotbar slot).
            ItemSlot? previousSlot = previousBackpackSlot ?? previousHotbarSlot;
            targetSlot = previousSlot;// set target slot to previous by default, override with a new value if needed according to additional logic below.
            if (previousSlot is null)
            {
                // find the first available backpack slot or last available hotbar slot
                IPlayerInventoryManager playerInventory = api!.World.Player.InventoryManager;
                IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
                WeightedSlot? bpBestSlot = backpack?.GetBestSuitedSlot(lightSourceSlot);
                if (bpBestSlot is not null)
                {
                    targetSlot = bpBestSlot.slot;
                    previousBackpackSlot = targetSlot;
                }
                else
                {
                    IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
                    WeightedSlot? hbBestSlot = hotbar?.GetBestSuitedSlot(lightSourceSlot);
                    if (hbBestSlot is not null)
                    {
                        targetSlot = hbBestSlot.slot;
                        previousHotbarSlot = targetSlot;
                    }
                }
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

        // Check other hotbar slots
        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (hotbar is not null)
        {
            if (TryFindBrightestLightSource(hotbar, out ItemSlot? brightestHotbarSlot))
            {
                return brightestHotbarSlot;
            }
        }

        // Check backpack slots last
        IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (backpack is not null)
        {
            if (TryFindBrightestLightSource(backpack, out ItemSlot? brightestBackpackSlot))
            {
                return brightestBackpackSlot;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches the given inventory for the brightest light source item.
    /// </summary>
    /// <returns> True if a light source was found; otherwise, false. </returns>
    protected static bool TryFindBrightestLightSource(in IInventory inventory, [NotNullWhen(true)] out ItemSlot? result)
    {
        CollectibleObject? current = null;
        result = null!;
        foreach (ItemSlot slot in inventory)
        {
            if (slot.Empty) continue;
            CollectibleObject item = slot.Itemstack.Collectible;
            int itemLightLevel = item.LightHsv[2];
            // check the lightHsv value to know if it's a light source
            if (itemLightLevel <= 0) continue;
            // if we have no current light source, take the first we find
            if (current is null)
            {
                result = slot;
                return true;
            }

            int currentItemLightLevel = current.LightHsv[2];
            // if the found light source is brighter than the current one, take it
            if (itemLightLevel > currentItemLightLevel)
            {
                result = slot;
                return true;
            }
        }
        return false;
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Determines if the given item slot contains a light source.
    /// </summary>
    private static bool IsLightSource(in ItemSlot? slot)
    {
        if (slot?.Empty ?? true) return false;
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
