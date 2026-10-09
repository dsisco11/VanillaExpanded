using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing.Targets;

/// <summary>Owns attached-bag validation, workspace loading, session ownership and bag/attachment persistence.</summary>
internal sealed class AttachedBagAutoStashTarget : AutoStashTarget
{
    private readonly IWorldAccessor world;
    private readonly Entity host;
    private readonly EntityBehaviorAttachable attachable;
    private readonly int attachmentIndex;
    private readonly ItemSlot attachment;
    private readonly ItemStack stack;
    private readonly IHeldBag bag;
    private readonly int slotCount;
    private InventoryGeneric? inventory;
    private AutoStashInventorySession? session;
    public override bool IsPrepared => inventory is not null;
    public override IInventory Inventory => inventory ?? throw new InvalidOperationException("Prepare the attached bag before accessing its execution inventory.");

    #region Public API
    #region Resolution
    /// <summary>Validates an attachment without loading slots, constructing workspaces or acquiring a session.</summary>
    public static AttachedBagAutoStashTarget? Resolve(IWorldAccessor world, Entity host, EntityBehaviorAttachable attachable, int index)
    {
        if (index < 0 || index >= attachable.Inventory.Count) return null;
        ItemSlot attachment = attachable.Inventory[index];
        ItemStack? stack = attachment.Itemstack;
        IHeldBag? bag = stack?.Collectible.GetCollectibleInterface<IHeldBag>();
        if (stack is null || bag is null) return null;
        int count = bag.GetQuantitySlots(stack);
        return count > 0 ? new AttachedBagAutoStashTarget(world, host, attachable, index, attachment, stack, bag, count) : null;
    }

    /// <summary>Reads persisted contents through the bag API without preparing a mutable server workspace.</summary>
    public override IEnumerable<ItemStack?> GetContents() => bag.GetContents(stack, world);

    /// <summary>Loads current bag slots while preserving vanilla wrapper identity and modification callbacks.</summary>
    public override bool TryPrepare()
    {
        if (IsPrepared) return true;
        if (bag is CollectibleBehaviorHeldBag vanilla)
        {
            // Reload persisted data into the owning workspace, then refresh its existing wrapper slots in place.
            // A failed load exposes no execution inventory and cannot acquire an inventory session.
            AttachedContainerWorkspace workspace = vanilla.getOrCreateContainerWorkspace(attachmentIndex, host, attachable.storeInv);
            if (!workspace.TryLoadInv(attachment, attachmentIndex, host)) return false;
            RefreshWorkspaceSlots(workspace.WrapperInv, workspace.BagInventory);
            inventory = workspace.WrapperInv;
            session = new AutoStashInventorySession(inventory);
        }
        else
        {
            // Distinct IHeldBag implementations supply real bag-content slots for a temporary, session-free inventory.
            var temporary = new InventoryGeneric(slotCount, "attachedcontainer", $"{host.EntityId}-{attachmentIndex}", world.Api);
            List<ItemSlotBagContent> slots = bag.GetOrCreateSlots(stack, temporary, 0, world);
            for (int index = 0; index < slots.Count && index < temporary.Count; index++) temporary[index] = slots[index];
            inventory = temporary;
        }
        return true;
    }
    #endregion

    #region Execution lifecycle
    /// <summary>Acquires a workspace session; temporary inventories have no session to open.</summary>
    public override void Acquire(IPlayerInventoryManager owner) => session?.Acquire(owner);

    /// <summary>Saves live bag slots, marks the attachment dirty and stores the owner before releasing a session.</summary>
    public override void FinalizeChanges()
    {
        // Engine callbacks can fail after mutation; save current slot contents rather than a predicted moved count.
        foreach (ItemSlotBagContent slot in Inventory) bag.Store(stack, slot);
        attachable.Inventory.MarkSlotDirty(attachmentIndex);
        attachable.storeInv();
    }

    /// <summary>Closes only an acquired workspace session; caller-owned and temporary inventories remain untouched.</summary>
    public override void Release(IPlayerInventoryManager owner) => session?.Release(owner);

    /// <summary>Refreshes contents in existing wrapper slots, retaining engine-owned identities and dirty callbacks.</summary>
    internal static void RefreshWorkspaceSlots(InventoryGeneric inventory, IEnumerable<ItemSlot> loadedSlots)
    {
        int index = 0;
        foreach (ItemSlot slot in loadedSlots)
        {
            inventory[index].Itemstack = slot.Itemstack;
            inventory.MarkSlotDirty(index++);
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Retains the resolved owner and bag references for this immediate operation only.</summary>
    private AttachedBagAutoStashTarget(IWorldAccessor world, Entity host, EntityBehaviorAttachable attachable,
        int index, ItemSlot attachment, ItemStack stack, IHeldBag bag, int count)
    {
        this.world = world;
        this.host = host;
        this.attachable = attachable;
        attachmentIndex = index;
        this.attachment = attachment;
        this.stack = stack;
        this.bag = bag;
        slotCount = count;
    }
    #endregion
}
