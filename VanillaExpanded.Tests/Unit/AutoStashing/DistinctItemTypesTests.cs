using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for GetDistinctItemTypes method in BlockBehaviorAutoStashable.
/// </summary>
public class DistinctItemTypesTests
{
    [Fact]
    public void GetDistinctItemTypes_EmptyInventory_ReturnsEmptySet()
    {
        // Arrange
        var inventory = MockInventory.CreateEmpty();

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithOneItem_ReturnsSingleId()
    {
        // Arrange
        var inventory = MockInventory.CreateWithItems(42);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(42, result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithMultipleUniqueItems_ReturnsAllIds()
    {
        // Arrange
        var inventory = MockInventory.CreateWithItems(1, 2, 3);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

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
        var inventory = MockInventory.CreateWithItems(1, 1, 2, 2, 2, 3);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

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
        var inventory = MockInventory.CreateMixed(itemIds: [1, 2], emptySlotCount: 5);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void GetDistinctItemTypes_InventoryWithOnlyEmptySlots_ReturnsEmptySet()
    {
        // Arrange
        var inventory = MockInventory.CreateMixed(itemIds: [], emptySlotCount: 10);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDistinctItemTypes_LargeInventory_HandlesCorrectly()
    {
        // Arrange
        var itemIds = Enumerable.Range(1, 100).ToArray();
        var inventory = MockInventory.CreateWithItems(itemIds);

        // Act
        var result = BlockBehaviorAutoStashable.GetDistinctItemTypes(inventory.Object);

        // Assert
        Assert.Equal(100, result.Count);
        foreach (var id in itemIds)
        {
            Assert.Contains(id, result);
        }
    }
}
