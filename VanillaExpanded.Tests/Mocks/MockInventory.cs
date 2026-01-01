using Moq;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// Factory methods for creating mock IInventory instances for testing.
/// </summary>
public static class MockInventory
{
    /// <summary>
    /// Creates a mock empty inventory.
    /// </summary>
    public static Mock<IInventory> CreateEmpty()
    {
        var mock = new Mock<IInventory>();
        var emptyList = new List<ItemSlot>();
        mock.Setup(i => i.GetEnumerator()).Returns(() => emptyList.GetEnumerator());
        mock.As<IEnumerable<ItemSlot>>().Setup(i => i.GetEnumerator()).Returns(() => emptyList.GetEnumerator());
        return mock;
    }

    /// <summary>
    /// Creates a mock inventory containing the specified slots.
    /// </summary>
    public static Mock<IInventory> CreateWithSlots(params Mock<ItemSlot>[] slots)
    {
        var mock = new Mock<IInventory>();
        var slotList = slots.Select(s => s.Object).ToList();
        mock.Setup(i => i.GetEnumerator()).Returns(() => slotList.GetEnumerator());
        mock.As<IEnumerable<ItemSlot>>().Setup(i => i.GetEnumerator()).Returns(() => slotList.GetEnumerator());
        return mock;
    }

    /// <summary>
    /// Creates a mock inventory with items having the specified collectible IDs.
    /// </summary>
    public static Mock<IInventory> CreateWithItems(params int[] collectibleIds)
    {
        var slots = collectibleIds.Select(id => MockItemSlot.CreateWithItem(id)).ToArray();
        return CreateWithSlots(slots);
    }

    /// <summary>
    /// Creates a mock inventory with a mix of items and empty slots.
    /// </summary>
    public static Mock<IInventory> CreateMixed(int[] itemIds, int emptySlotCount)
    {
        var slots = new List<Mock<ItemSlot>>();
        
        foreach (var id in itemIds)
        {
            slots.Add(MockItemSlot.CreateWithItem(id));
        }
        
        for (int i = 0; i < emptySlotCount; i++)
        {
            slots.Add(MockItemSlot.CreateEmpty());
        }

        return CreateWithSlots(slots.ToArray());
    }
}
