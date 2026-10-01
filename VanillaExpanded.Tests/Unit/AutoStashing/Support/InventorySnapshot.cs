using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Captures immutable slot contents and quantities while retaining slot identities for regression assertions.</summary>
internal sealed class InventorySnapshot
{
    private readonly SlotState[] slots;
    private readonly Dictionary<string, int> totals;

    #region Public API
    /// <summary>Captures all inventories in deterministic enumeration order before an operation.</summary>
    public InventorySnapshot(params IInventory[] inventories)
    {
        slots = inventories.SelectMany(inventory => inventory).Select(Capture).ToArray();
        totals = Totals(slots);
    }

    /// <summary>Asserts slot identity, collectible identity, quantities, and both attribute trees are unchanged except at named slots.</summary>
    public void AssertUnchangedExcept(params ItemSlot[] changedSlots)
    {
        foreach (SlotState before in slots)
        {
            Assert.Same(before.Slot, before.Inventory?[before.Index] ?? before.Slot);
            if (!changedSlots.Contains(before.Slot))
            {
                Assert.Equal(before, Capture(before.Slot));
            }
        }
    }

    /// <summary>Asserts aggregate item-class/code quantities remain conserved across captured slots.</summary>
    public void AssertConserved()
    {
        Dictionary<string, int> after = Totals(slots.Select(state => Capture(state.Inventory?[state.Index] ?? state.Slot)));
        Assert.Equal(totals.OrderBy(pair => pair.Key).ToArray(), after.OrderBy(pair => pair.Key).ToArray());
    }

    /// <summary>Asserts exact contents of a changed slot without treating shared codes as shared collectible identities.</summary>
    public static void AssertStack(ItemSlot slot, CollectibleObject collectible, int quantity)
    {
        Assert.NotNull(slot.Itemstack);
        Assert.Same(collectible, slot.Itemstack.Collectible);
        Assert.Equal(quantity, slot.StackSize);
    }
    #endregion

    #region Private
    /// <summary>Copies attribute bytes so later mutation cannot rewrite the expected state.</summary>
    private static SlotState Capture(ItemSlot slot)
    {
        ItemStack? stack = slot.Itemstack;
        InventoryBase? inventory = slot.Inventory;
        return new SlotState(slot, inventory, inventory?.GetSlotId(slot) ?? -1, stack?.Collectible,
            stack?.Class.ToString(), stack?.Collectible.Code?.ToString(), slot.StackSize,
            stack is null ? null : AttributeBytes(stack.Attributes),
            stack is null ? null : AttributeBytes(stack.TempAttributes));
    }

    /// <summary>Serializes an attribute tree through the engine interface into an immutable comparison value.</summary>
    private static string AttributeBytes(Vintagestory.API.Datastructures.IAttribute attributes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        attributes.ToBytes(writer);
        return Convert.ToHexString(stream.ToArray());
    }

    /// <summary>Groups by item class and code to conserve quantities even in explicit code-only eligibility fixtures.</summary>
    private static Dictionary<string, int> Totals(IEnumerable<SlotState> states)
    {
        return states.Where(state => state.Collectible is not null)
            .GroupBy(state => $"{state.Class}:{state.Code}")
            .ToDictionary(group => group.Key, group => group.Sum(state => state.Quantity));
    }

    /// <summary>Stores copied state and the original engine slot/collectible identities.</summary>
    private sealed record SlotState(ItemSlot Slot, InventoryBase? Inventory, int Index,
        CollectibleObject? Collectible, string? Class, string? Code, int Quantity,
        string? Attributes, string? TempAttributes);
    #endregion
}
