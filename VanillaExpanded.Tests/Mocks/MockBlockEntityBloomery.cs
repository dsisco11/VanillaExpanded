using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A testable implementation of BlockEntityBloomery for unit testing.
/// Uses a concrete subclass since BlockEntityBloomery has non-virtual members.
/// </summary>
public class TestableBlockEntityBloomery : BlockEntityBloomery
{
    private readonly InventoryGeneric _testInventory;

    public TestableBlockEntityBloomery(BlockPos? position = null, ICoreAPI? api = null)
    {
        // Create our own inventory (the base class creates one but we can't easily access it)
        _testInventory = new InventoryGeneric(3, "bloomery-1", null, null);

        // Use reflection to set the internal bloomeryInv field
        var field = typeof(BlockEntityBloomery).GetField("bloomeryInv",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(this, _testInventory);

        // Set Api on the block entity (needed for CanAdd stack comparison)
        if (api is not null)
        {
            Api = api;
            _testInventory.Api = api;

            var networkUtilMock = new Mock<IInventoryNetworkUtil>();
            networkUtilMock
                .Setup(u => u.GetFlipSlotsPacket(It.IsAny<InventoryBase>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new object());
            _testInventory.InvNetworkUtil = networkUtilMock.Object;
        }

        // Set position
        if (position is not null)
        {
            Pos = position;
        }
    }

    /// <summary>
    /// Gets the inventory for testing purposes.
    /// </summary>
    public InventoryGeneric TestInventory => _testInventory;

    /// <summary>
    /// Sets whether the bloomery is burning (for testing).
    /// </summary>
    public void SetBurning(bool burning)
    {
        // Use reflection to set the private burning field
        var field = typeof(BlockEntityBloomery).GetField("burning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(this, burning);
    }

    /// <summary>
    /// Gets the fuel slot (slot 0).
    /// </summary>
    public ItemSlot FuelSlot => _testInventory[0];

    /// <summary>
    /// Gets the ore slot (slot 1).
    /// </summary>
    public ItemSlot OreSlot => _testInventory[1];

    /// <summary>
    /// Gets the output slot (slot 2).
    /// </summary>
    public ItemSlot OutSlot => _testInventory[2];
}

/// <summary>
/// A mock wrapper for BlockEntityBloomery that allows testing bloomery-specific auto-stashing.
/// </summary>
public class MockBlockEntityBloomery
{
    private readonly TestableBlockEntityBloomery _bloomery;

    public MockBlockEntityBloomery(BlockPos? position = null, ICoreAPI? api = null)
    {
        _bloomery = new TestableBlockEntityBloomery(position, api);
    }

    /// <summary>
    /// Gets the BlockEntityBloomery instance for use in tests.
    /// </summary>
    public BlockEntityBloomery Object => _bloomery;

    /// <summary>
    /// Gets the inventory for testing.
    /// </summary>
    public InventoryGeneric Inventory => _bloomery.TestInventory;

    /// <summary>
    /// Gets the fuel slot (slot 0).
    /// </summary>
    public ItemSlot FuelSlot => _bloomery.FuelSlot;

    /// <summary>
    /// Gets the ore slot (slot 1).
    /// </summary>
    public ItemSlot OreSlot => _bloomery.OreSlot;

    /// <summary>
    /// Gets the output slot (slot 2).
    /// </summary>
    public ItemSlot OutSlot => _bloomery.OutSlot;

    /// <summary>
    /// Sets the fuel in the bloomery.
    /// </summary>
    public MockBlockEntityBloomery WithFuel(MockItem? item, int stackSize = 1)
    {
        if (item is not null)
        {
            FuelSlot.Itemstack = new ItemStack(item, stackSize);
        }
        return this;
    }

    /// <summary>
    /// Sets the ore in the bloomery.
    /// </summary>
    public MockBlockEntityBloomery WithOre(MockItem? item, int stackSize = 1)
    {
        if (item is not null)
        {
            OreSlot.Itemstack = new ItemStack(item, stackSize);
        }
        return this;
    }

    /// <summary>
    /// Sets the output in the bloomery.
    /// </summary>
    public MockBlockEntityBloomery WithOutput(MockItem? item, int stackSize = 1)
    {
        if (item is not null)
        {
            OutSlot.Itemstack = new ItemStack(item, stackSize);
        }
        return this;
    }

    /// <summary>
    /// Sets the bloomery to burning state.
    /// </summary>
    public MockBlockEntityBloomery AsBurning(bool burning = true)
    {
        _bloomery.SetBurning(burning);
        return this;
    }

    /// <summary>
    /// Creates an empty bloomery.
    /// </summary>
    public static MockBlockEntityBloomery Empty(BlockPos? position = null, ICoreAPI? api = null)
        => new(position, api);

    /// <summary>
    /// Creates a bloomery with fuel.
    /// </summary>
    public static MockBlockEntityBloomery WithFuelItem(MockItem fuel, int stackSize = 1, BlockPos? position = null, ICoreAPI? api = null)
        => new MockBlockEntityBloomery(position, api).WithFuel(fuel, stackSize);

    /// <summary>
    /// Creates a bloomery with ore.
    /// </summary>
    public static MockBlockEntityBloomery WithOreItem(MockItem ore, int stackSize = 1, BlockPos? position = null, ICoreAPI? api = null)
        => new MockBlockEntityBloomery(position, api).WithOre(ore, stackSize);
}

