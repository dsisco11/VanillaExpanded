using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing;

internal static class EntityAttachedContainerAutoStash
{
    public static bool HandleInteract(
        EntityBehaviorAttachable attachable,
        EntityAgent byEntity,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        EntityControls controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
        if (mode != EnumInteractMode.Interact
            || !VanillaExpandedModSystem.Config.EnableAutoStash
            || byEntity.World.Side != EnumAppSide.Client
            || byEntity is not EntityPlayer playerEntity
            || !controls.CtrlKey
            || !controls.ShiftKey)
        {
            return true;
        }

        int selectionBoxIndex = playerEntity.EntitySelection?.SelectionBoxIndex ?? -1;
        int attachmentSlotIndex = selectionBoxIndex > 0
            ? attachable.GetSlotIndexFromSelectionBoxIndex(selectionBoxIndex - 1)
            : -1;
        if (attachmentSlotIndex < 0
            || !CanAutoStash(playerEntity.Player.InventoryManager, attachable.Inventory[attachmentSlotIndex], byEntity.World))
        {
            return true;
        }

        EntityAttachedContainerAutoStashClient? client = byEntity.World.Api.ModLoader
            .GetModSystem<AutoStashSystem_Client>()?.EntityAttachedContainers;
        if (client is null)
        {
            return true;
        }

        client.Begin(attachable, attachmentSlotIndex);
        handled = EnumHandling.PreventSubsequent;
        return false;
    }

    public static void AppendInteractionHelp(
        EntityBehaviorAttachable attachable,
        IClientWorldAccessor world,
        EntitySelection selection,
        IClientPlayer player,
        ref WorldInteraction[] interactions)
    {
        if (!VanillaExpandedModSystem.Config.EnableAutoStash)
        {
            return;
        }

        int attachmentSlotIndex = selection.SelectionBoxIndex > 0
            ? attachable.GetSlotIndexFromSelectionBoxIndex(selection.SelectionBoxIndex - 1)
            : -1;
        if (attachmentSlotIndex < 0
            || !CanAutoStash(player.InventoryManager, attachable.Inventory[attachmentSlotIndex], world))
        {
            return;
        }

        WorldInteraction autoStashInteraction = new()
        {
            ActionLangCode = "vanillaexpanded:blockhelp-autostash-container",
            MouseButton = EnumMouseButton.Right,
            HotKeyCodes = ["ctrl", "shift"]
        };
        interactions = [.. interactions, autoStashInteraction];
    }

    public static bool CanAutoStash(IPlayerInventoryManager playerInventory, ItemSlot attachmentSlot, IWorldAccessor world)
    {
        ItemStack? attachmentStack = attachmentSlot.Itemstack;
        IHeldBag? heldBag = attachmentStack?.Collectible.GetCollectibleInterface<IHeldBag>();
        if (heldBag is null || attachmentStack is null)
        {
            return false;
        }

        HashSet<int> contentTypes = [.. heldBag.GetContents(attachmentStack, world)
            .Where(static stack => stack?.Collectible is not null)
            .Select(static stack => stack.Collectible.Id)];

        return contentTypes.Count != 0
            && PlayerSlots(playerInventory).Any(slot =>
                slot.Itemstack?.Collectible is not null && contentTypes.Contains(slot.Itemstack.Collectible.Id));
    }

    public static bool TryAutoStash(
        IWorldAccessor world,
        IPlayer player,
        Entity hostEntity,
        EntityBehaviorAttachable attachable,
        int attachmentSlotIndex)
    {
        if (attachmentSlotIndex < 0 || attachmentSlotIndex >= attachable.Inventory.Count)
        {
            return false;
        }

        ItemSlot attachmentSlot = attachable.Inventory[attachmentSlotIndex];
        ItemStack? attachmentStack = attachmentSlot.Itemstack;
        IHeldBag? heldBag = attachmentStack?.Collectible.GetCollectibleInterface<IHeldBag>();
        if (heldBag is null || attachmentStack is null)
        {
            return false;
        }

        int slotCount = heldBag.GetQuantitySlots(attachmentStack);
        if (slotCount <= 0)
        {
            return false;
        }

        InventoryGeneric targetInventory;
        bool usesWorkspace = heldBag is CollectibleBehaviorHeldBag;
        if (heldBag is CollectibleBehaviorHeldBag vanillaBag)
        {
            AttachedContainerWorkspace workspace = vanillaBag.getOrCreateContainerWorkspace(
                attachmentSlotIndex, hostEntity, attachable.storeInv);
            if (!workspace.TryLoadInv(attachmentSlot, attachmentSlotIndex, hostEntity))
            {
                return false;
            }

            targetInventory = workspace.WrapperInv;
            RefreshWorkspaceSlots(targetInventory, workspace.BagInventory);
        }
        else
        {
            targetInventory = new InventoryGeneric(slotCount, "attachedcontainer", $"{hostEntity.EntityId}-{attachmentSlotIndex}", world.Api);
            List<ItemSlotBagContent> loadedSlots = heldBag.GetOrCreateSlots(attachmentStack, targetInventory, 0, world);
            for (int index = 0; index < loadedSlots.Count && index < targetInventory.Count; index++)
            {
                targetInventory[index] = loadedSlots[index];
            }
        }

        ItemSlot[] contentSlots = targetInventory.ToArray();
        HashSet<AssetLocation> contentTypes = [.. contentSlots
            .Where(static slot => slot.Itemstack?.Collectible is not null)
            .Select(static slot => slot.Itemstack!.Collectible.Code)];
        bool movedItems = contentTypes.Count != 0 && AutoStashTransferService.AutoStashToInventory(
            world,
            player.InventoryManager,
            player.PlayerName,
            targetInventory,
            hostEntity.Pos.AsBlockPos,
            $"attached container on {hostEntity.Code}",
            stack => contentTypes.Contains(stack.Collectible.Code),
            manageInventorySession: usesWorkspace) > 0;

        if (movedItems)
        {
            foreach (ItemSlotBagContent contentSlot in contentSlots)
            {
                heldBag.Store(attachmentStack, contentSlot);
            }

            attachable.Inventory.MarkSlotDirty(attachmentSlotIndex);
            attachable.storeInv();
        }

        return movedItems;
    }

    internal static void RefreshWorkspaceSlots(InventoryGeneric inventory, IEnumerable<ItemSlot> loadedSlots)
    {
        int slotIndex = 0;
        foreach (ItemSlot loadedSlot in loadedSlots)
        {
            inventory[slotIndex].Itemstack = loadedSlot.Itemstack;
            inventory.MarkSlotDirty(slotIndex);
            slotIndex++;
        }
    }

    private static IEnumerable<ItemSlot> PlayerSlots(IPlayerInventoryManager playerInventory)
    {
        IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);

        return (backpack ?? Enumerable.Empty<ItemSlot>()).Concat(hotbar ?? Enumerable.Empty<ItemSlot>());
    }
}