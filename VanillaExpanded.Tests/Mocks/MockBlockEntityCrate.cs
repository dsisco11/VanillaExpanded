using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock wrapper for BlockEntityCrate that encapsulates Moq setup for unit testing.
/// Since BlockEntityCrate uses private inventory field, this creates a testable subclass.
/// </summary>
public class MockBlockEntityCrate
{
    public InventoryGeneric Inventory { get; }
    public BlockPos Position { get; }
    public ICoreAPI? Api { get; }

    public Mock<BlockEntityCrate> BlockEntityMock { get; }

    public MockBlockEntityCrate(int slotCount = 16, BlockPos? position = null, ICoreAPI? api = null)
    {
        Position = position ?? new BlockPos(0, 0, 0);
        Api = api;

        // Create real inventory for the crate
        Inventory = new InventoryGeneric(slotCount, "crate", "test-crate", null!);

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

        BlockEntityMock = new Mock<BlockEntityCrate> { CallBase = true };
        BlockEntityMock.SetupGet(blockEntity => blockEntity.Inventory).Returns(Inventory);
        BlockEntityMock.Object.Pos = Position;
    }

    /// <summary>
    /// Gets the BlockEntityCrate instance for use in tests.
    /// </summary>
    public BlockEntityCrate Object => BlockEntityMock.Object;

    /// <summary>
    /// Creates an empty MockBlockEntityCrate.
    /// </summary>
    public static MockBlockEntityCrate Empty(int slotCount = 16, BlockPos? position = null, ICoreAPI? api = null)
        => new(slotCount, position, api);

    /// <summary>
    /// Creates a MockBlockEntityCrate with a single item type (typical crate usage).
    /// Crates only accept one item type, determined by the first item in them.
    /// </summary>
    public static MockBlockEntityCrate WithSingleItemType(MockItem item, int quantity = 1, int slotCount = 16, BlockPos? position = null, ICoreAPI? api = null)
    {
        var crate = new MockBlockEntityCrate(slotCount, position, api);
        crate.SetItem(0, item, quantity);
        return crate;
    }

    /// <summary>
    /// Creates a MockBlockEntityCrate with items in specific slots.
    /// Note: In real crates, all items should be the same type.
    /// </summary>
    public static MockBlockEntityCrate WithItems(Dictionary<int, (MockItem item, int quantity)> items, int slotCount = 16, BlockPos? position = null, ICoreAPI? api = null)
    {
        var crate = new MockBlockEntityCrate(slotCount, position, api);
        foreach (var (slotIndex, (item, quantity)) in items)
        {
            if (slotIndex < slotCount)
            {
                crate.SetItem(slotIndex, item, quantity);
            }
        }
        return crate;
    }

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
    /// Checks if the crate is empty.
    /// </summary>
    public bool IsEmpty => Inventory.Empty;

    /// <summary>
    /// Gets all non-empty item stacks in the crate.
    /// </summary>
    public ItemStack[] GetNonEmptyStacks() => Object.GetNonEmptyContentStacks();

    /// <summary>
    /// Gets the total quantity of items across all slots.
    /// </summary>
    public int TotalItemCount
    {
        get
        {
            int total = 0;
            foreach (var slot in Inventory)
            {
                if (!slot.Empty)
                {
                    total += slot.StackSize;
                }
            }
            return total;
        }
    }

}
