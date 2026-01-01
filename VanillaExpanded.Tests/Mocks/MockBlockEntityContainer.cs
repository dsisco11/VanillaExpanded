using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock wrapper for BlockEntityContainer that encapsulates Moq setup for unit testing.
/// Since BlockEntityContainer is abstract, this creates a testable concrete implementation.
/// </summary>
public class MockBlockEntityContainer
{
    public Mock<InventoryGeneric> InventoryMock { get; }
    public InventoryGeneric Inventory { get; }
    public BlockPos Position { get; }

    private readonly List<ItemSlot> _slots;
    private readonly TestableBlockEntityContainer _container;

    public MockBlockEntityContainer(int slotCount = 10, BlockPos? position = null)
    {
        Position = position ?? new BlockPos(0, 0, 0);
        _slots = [];

        // Create real inventory with slots
        Inventory = new InventoryGeneric(slotCount, "test", "test-container", null!);
        InventoryMock = new Mock<InventoryGeneric>(slotCount, "test", "test-container", null!) { CallBase = true };

        for (int i = 0; i < slotCount; i++)
        {
            _slots.Add(Inventory[i]);
        }

        _container = new TestableBlockEntityContainer(Inventory, Position);
    }

    /// <summary>
    /// Gets the BlockEntityContainer instance for use in tests.
    /// </summary>
    public BlockEntityContainer Object => _container;

    /// <summary>
    /// Creates a MockBlockEntityContainer with items in specific slots.
    /// </summary>
    public static MockBlockEntityContainer WithItems(Dictionary<int, MockItem> items, int totalSlots = 10, BlockPos? position = null)
    {
        var container = new MockBlockEntityContainer(totalSlots, position);
        foreach (var (slotIndex, item) in items)
        {
            if (slotIndex < totalSlots)
            {
                container.SetItem(slotIndex, item);
            }
        }
        return container;
    }

    /// <summary>
    /// Creates a MockBlockEntityContainer with items from an array (fills slots sequentially).
    /// </summary>
    public static MockBlockEntityContainer WithItems(params MockItem[] items)
    {
        var container = new MockBlockEntityContainer(Math.Max(items.Length, 10));
        for (int i = 0; i < items.Length; i++)
        {
            container.SetItem(i, items[i]);
        }
        return container;
    }

    /// <summary>
    /// Creates an empty MockBlockEntityContainer.
    /// </summary>
    public static MockBlockEntityContainer Empty(int slotCount = 10, BlockPos? position = null)
        => new(slotCount, position);

    /// <summary>
    /// Sets an item at the specified slot index.
    /// </summary>
    public void SetItem(int slotIndex, MockItem item, int stackSize = 1)
    {
        Inventory[slotIndex].Itemstack = new ItemStack(item, stackSize);
    }

    /// <summary>
    /// Clears the item at the specified slot index.
    /// </summary>
    public void ClearSlot(int slotIndex)
    {
        Inventory[slotIndex].Itemstack = null;
    }

    /// <summary>
    /// Gets the item stack at the specified slot.
    /// </summary>
    public ItemStack? GetItemStack(int slotIndex)
    {
        return Inventory[slotIndex].Itemstack;
    }

    /// <summary>
    /// Checks if the container is empty.
    /// </summary>
    public bool IsEmpty => Inventory.Empty;

    /// <summary>
    /// Gets all non-empty item stacks in the container.
    /// </summary>
    public ItemStack[] GetNonEmptyStacks() => _container.GetNonEmptyContentStacks();

    /// <summary>
    /// A testable concrete implementation of BlockEntityContainer.
    /// </summary>
    private class TestableBlockEntityContainer : BlockEntityContainer
    {
        private readonly InventoryGeneric _inventory;
        private readonly string _inventoryClassName = "test-container";

        public TestableBlockEntityContainer(InventoryGeneric inventory, BlockPos pos)
        {
            _inventory = inventory;
            Pos = pos;
        }

        public override InventoryBase Inventory => _inventory;
        public override string InventoryClassName => _inventoryClassName;
    }
}
