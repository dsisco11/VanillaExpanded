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
    public ICoreAPI? Api { get; }

    private readonly List<ItemSlot> _slots;
    private readonly TestableBlockEntityContainer _container;

    public MockBlockEntityContainer(int slotCount = 10, BlockPos? position = null, ICoreAPI? api = null)
    {
        Position = position ?? new BlockPos(0, 0, 0);
        Api = api;
        _slots = [];

        // Create real inventory with slots
        Inventory = new InventoryGeneric(slotCount, "test", "test-container", null!);
        InventoryMock = new Mock<InventoryGeneric>(slotCount, "test", "test-container", null!) { CallBase = true };

        // Set up API on inventory if provided
        if (api is not null)
        {
            Inventory.Api = api;

            // Set up network util to prevent null references
            var networkUtilMock = new Mock<IInventoryNetworkUtil>();
            networkUtilMock
                .Setup(u => u.GetFlipSlotsPacket(It.IsAny<InventoryBase>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new object());
            Inventory.InvNetworkUtil = networkUtilMock.Object;
        }

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
    public static MockBlockEntityContainer WithItems(Dictionary<int, MockItem> items, int totalSlots = 10, BlockPos? position = null, ICoreAPI? api = null)
    {
        var container = new MockBlockEntityContainer(totalSlots, position, api);
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
    public static MockBlockEntityContainer WithItems(ICoreAPI? api, params MockItem[] items)
    {
        var container = new MockBlockEntityContainer(Math.Max(items.Length, 10), api: api);
        for (int i = 0; i < items.Length; i++)
        {
            container.SetItem(i, items[i]);
        }
        return container;
    }

    /// <summary>
    /// Creates a MockBlockEntityContainer with items from an array (fills slots sequentially).
    /// Overload without API for backwards compatibility.
    /// </summary>
    public static MockBlockEntityContainer WithItems(params MockItem[] items)
        => WithItems(null, items);

    /// <summary>
    /// Creates an empty MockBlockEntityContainer.
    /// </summary>
    public static MockBlockEntityContainer Empty(int slotCount = 10, BlockPos? position = null, ICoreAPI? api = null)
        => new(slotCount, position, api);

    /// <summary>
    /// Sets an item at the specified slot index.
    /// </summary>
    public void SetItem(int slotIndex, MockItem item, int stackSize = 1)
    {
        // Set API on item if we have one (needed for CollectibleObject.Equals)
        if (Api is not null)
        {
            item.SetApi(Api);
        }
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
