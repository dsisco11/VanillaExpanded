using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal readonly record struct CraftingSlotId(string InventoryId, int Index);

internal sealed class CraftingStackSnapshot
{
    private readonly ItemStack stack;

    internal CraftingStackSnapshot(CraftingSlotId slot, ItemStack stack)
    {
        Slot = slot;
        this.stack = Copy(stack);
    }

    internal CraftingSlotId Slot { get; }
    internal int Quantity => stack.StackSize;
    internal ItemStack GetStack() => Copy(stack);

    internal static ItemStack Copy(ItemStack stack)
    {
        var copy = stack.Clone();
        copy.TempAttributes = stack.TempAttributes.Clone();
        return copy;
    }
}

internal sealed class CraftingInventorySnapshot
{
    private CraftingInventorySnapshot(int ownerThreadId, int width, int height,
        ImmutableArray<CraftingStackSnapshot> stacks, ImmutableArray<int> gridLimits)
    {
        OwnerThreadId = ownerThreadId;
        Width = width;
        Height = height;
        Stacks = stacks;
        GridLimits = gridLimits;
    }

    internal int OwnerThreadId { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal ImmutableArray<CraftingStackSnapshot> Stacks { get; }
    internal ImmutableArray<int> GridLimits { get; }

    internal static CraftingInventorySnapshot Capture(IPlayer player, int width, int height) =>
        Capture(player, width, height, RuntimeEnv.MainThreadId, false);

    // The fixture path follows QuickToolInventoryView's explicit InventoryGeneric test adapter.
    internal static CraftingInventorySnapshot Capture(IPlayer player, int width, int height,
        int ownerThreadId, bool allowGenericFixture)
    {
        EnsureThread(ownerThreadId);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int gridSize = checked(width * height);
        var manager = player.InventoryManager;
        var stacks = ImmutableArray.CreateBuilder<CraftingStackSnapshot>();
        var limits = ImmutableArray.CreateBuilder<int>(gridSize);
        var seen = new HashSet<ItemSlot>(ReferenceEqualityComparer.Instance);
        var addresses = new HashSet<CraftingSlotId>();
        var seenStacks = new HashSet<ItemStack>(ReferenceEqualityComparer.Instance);

        InventoryBase? Resolve(string name, string type)
        {
            var inventory = manager.GetOwnInventory(name);
            if (inventory == null) return null;
            if (inventory is not InventoryBase native
                || (native.GetType().FullName != type
                    && !(allowGenericFixture && native.GetType() == typeof(InventoryGeneric))))
                throw new NotSupportedException($"Unverified crafting inventory adapter: {inventory.GetType().FullName}.");
            return native;
        }

        void Read(InventoryBase? inventory, int start, int end, bool grid)
        {
            if (inventory == null) return;
            bool accessible = inventory.CanPlayerModify(player, player.Entity.Pos) && !inventory.TakeLocked;
            for (int index = start; index < end; index++)
            {
                var slot = inventory[index];
                var address = new CraftingSlotId(inventory.InventoryID, index);
                if (!ReferenceEquals(slot.Inventory, inventory) || !seen.Add(slot) || !addresses.Add(address))
                    throw new InvalidOperationException("Aliased crafting inventory slot.");
                if (!allowGenericFixture && slot.GetType() != (grid ? typeof(ItemSlot)
                    : start == 4 ? typeof(ItemSlotBagContent) : typeof(ItemSlotSurvival)))
                    throw new NotSupportedException($"Unverified crafting source slot: {slot.GetType().FullName}.");
                if (grid) limits.Add(Math.Max(0, slot.MaxSlotStackSize));
                if (accessible && slot.CanTake() && slot.Itemstack is { StackSize: > 0 } stack)
                {
                    if (!seenStacks.Add(stack)) throw new InvalidOperationException("Aliased crafting inventory stack.");
                    stacks.Add(new CraftingStackSnapshot(address, stack));
                }
            }
        }

        var hotbar = Resolve(GlobalConstants.hotBarInvClassName, "Vintagestory.Common.InventoryPlayerHotbar");
        var backpack = Resolve(GlobalConstants.backpackInvClassName, "Vintagestory.Common.InventoryPlayerBackpacks");
        var crafting = Resolve(GlobalConstants.craftingInvClassName, "Vintagestory.Common.InventoryCraftingGrid");
        if (crafting == null || crafting.Count != gridSize + 1)
            throw new NotSupportedException("The crafting grid topology is not supported.");
        if (!allowGenericFixture && (width != 3 || height != 3))
            throw new NotSupportedException("The native crafting grid dimensions are not supported.");
        if (hotbar != null && hotbar.Count != 12)
            throw new NotSupportedException("The hotbar topology is not supported.");
        if (backpack != null && backpack.Count < 4)
            throw new NotSupportedException("The backpack topology is not supported.");
        Read(hotbar, 0, hotbar == null ? 0 : 10, false);
        Read(backpack, 4, backpack?.Count ?? 4, false);
        Read(crafting, 0, gridSize, true);
        return new(ownerThreadId, width, height, stacks.ToImmutable(), limits.ToImmutable());
    }

    internal static void EnsureThread(int ownerThreadId)
    {
        if (ownerThreadId <= 0 || Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("Crafting inventory and native recipe data must be read on their owning thread.");
    }
}
