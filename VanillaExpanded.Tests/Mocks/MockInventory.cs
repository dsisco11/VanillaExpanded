using Moq;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock wrapper for IInventory that encapsulates Moq setup for unit testing.
/// </summary>
public class MockInventory
{
    public Mock<IInventory> Mock { get; }
    public IInventory Object => Mock.Object;

    private readonly List<ItemSlot> _slots;

    public MockInventory(int slotCount = 10)
    {
        Mock = new Mock<IInventory>();
        _slots = [];

        for (int i = 0; i < slotCount; i++)
        {
            _slots.Add(new DummySlot());
        }

        SetupEnumerator();
        SetupIndexer();
        SetupCount();
    }

    /// <summary>
    /// Creates a MockInventory with items in specific slots.
    /// </summary>
    /// <param name="items">Dictionary of slot index to MockItem</param>
    /// <param name="totalSlots">Total number of slots in the inventory</param>
    public static MockInventory WithItems(Dictionary<int, MockItem> items, int totalSlots = 10)
    {
        var mockInv = new MockInventory(totalSlots);
        foreach (var (slotIndex, item) in items)
        {
            if (slotIndex < totalSlots)
            {
                mockInv._slots[slotIndex].Itemstack = new ItemStack(item);
            }
        }
        return mockInv;
    }

    /// <summary>
    /// Creates a MockInventory with items from an array (fills slots sequentially).
    /// </summary>
    public static MockInventory WithItems(params MockItem[] items)
    {
        var mockInv = new MockInventory(items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            mockInv._slots[i].Itemstack = new ItemStack(items[i]);
        }
        return mockInv;
    }

    /// <summary>
    /// Creates an empty MockInventory.
    /// </summary>
    public static MockInventory Empty(int slotCount = 10) => new(slotCount);

    /// <summary>
    /// Gets the slot at the specified index.
    /// </summary>
    public ItemSlot this[int index]
    {
        get => _slots[index];
        set => _slots[index] = value;
    }

    /// <summary>
    /// Sets the item at the specified slot index.
    /// </summary>
    public void SetItem(int slotIndex, MockItem item)
    {
        _slots[slotIndex].Itemstack = new ItemStack(item);
    }

    /// <summary>
    /// Clears the item at the specified slot index.
    /// </summary>
    public void ClearSlot(int slotIndex)
    {
        _slots[slotIndex].Itemstack = null;
    }

    private void SetupEnumerator()
    {
        Mock.Setup(i => i.GetEnumerator()).Returns(() => _slots.GetEnumerator());
    }

    private void SetupIndexer()
    {
        Mock.Setup(i => i[It.IsAny<int>()]).Returns((int index) => _slots[index]);
    }

    private void SetupCount()
    {
        Mock.Setup(i => i.Count).Returns(() => _slots.Count);
    }
}
