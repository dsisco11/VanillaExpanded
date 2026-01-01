using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Fakes;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for GetDistinctItemTypes method in BlockBehaviorAutoStashable.
/// </summary>
public class DistinctItemTypesTests
{
    /// <summary>
    /// Helper to create an InventoryGeneric with items for testing.
    /// </summary>
    private static InventoryGeneric CreateInventory(params int[] collectibleIds)
    {
        var inv = new InventoryGeneric(collectibleIds.Length, "test", "test-1", null!);
        for (int i = 0; i < collectibleIds.Length; i++)
        {
            var item = new FakeItem(collectibleIds[i]);
            inv[i].Itemstack = new ItemStack(item);
        }
        return inv;
    }

    /// <summary>
    /// Helper to create an InventoryGeneric with a mix of items and empty slots.
    /// </summary>
    private static InventoryGeneric CreateMixedInventory(int[] itemIds, int emptySlotCount)
    {
        var inv = new InventoryGeneric(itemIds.Length + emptySlotCount, "test", "test-1", null!);
        for (int i = 0; i < itemIds.Length; i++)
        {
            var item = new FakeItem(itemIds[i]);
            inv[i].Itemstack = new ItemStack(item);
        }
        // Remaining slots are already empty by default
        return inv;
    }

    [Fact]
    public void GetDistinctItemTypes_EmptyInventory_ReturnsEmptySet()
    {
        // Arrange
        var inventory = new InventoryGeneric(0, "test", "test-1", null!);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithOneItem_ReturnsSingleId()
    {
        // Arrange
        var inventory = CreateInventory(42);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Single(result);
        Assert.Contains(42, result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithMultipleUniqueItems_ReturnsAllIds()
    {
        // Arrange
        var inventory = CreateInventory(1, 2, 3);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithDuplicateItems_ReturnsUniqueIds()
    {
        // Arrange
        var inventory = CreateInventory(1, 1, 2, 2, 2, 3);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithEmptySlots_FiltersOutEmptySlots()
    {
        // Arrange
        var inventory = CreateMixedInventory(itemIds: [1, 2], emptySlotCount: 5);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithOnlyEmptySlots_ReturnsEmptySet()
    {
        // Arrange
        var inventory = CreateMixedInventory(itemIds: [], emptySlotCount: 10);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDistinctItemTypes_LargeInventory_HandlesCorrectly()
    {
        // Arrange
        var itemIds = Enumerable.Range(1, 100).ToArray();
        var inventory = CreateInventory(itemIds);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory);

        // Assert
        Assert.Equal(100, result.Count);
        foreach (var id in itemIds)
        {
            Assert.Contains(id, result);
        }
    }
}
