using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Verifies captured assessment membership across populated, duplicate and empty engine slots.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public class DistinctItemTypesTests
{
    #region Public API
    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_EmptyInventory_ReturnsEmptySet()
    {
        // Arrange
        var inventory = new InventoryGeneric(0, "test", "test-1", null!);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Empty(result);
    }

    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_InventoryWithOneItem_ReturnsSingleId()
    {
        // Arrange
        var inventory = CreateInventory(42);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Single(result);
        Assert.Contains(42, result);
    }

    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_InventoryWithMultipleUniqueItems_ReturnsAllIds()
    {
        // Arrange
        var inventory = CreateInventory(1, 2, 3);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
    }

    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_InventoryWithDuplicateItems_ReturnsUniqueIds()
    {
        // Arrange
        var inventory = CreateInventory(1, 1, 2, 2, 2, 3);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
    }

    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_InventoryWithEmptySlots_FiltersOutEmptySlots()
    {
        // Arrange
        var inventory = CreateMixedInventory(itemIds: [1, 2], emptySlotCount: 5);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_InventoryWithOnlyEmptySlots_ReturnsEmptySet()
    {
        // Arrange
        var inventory = CreateMixedInventory(itemIds: [], emptySlotCount: 10);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Empty(result);
    }

    /// <summary>Verifies the named membership scenario through the shared read-only assessment boundary.</summary>
    [Fact]
    public void AssessmentMembership_LargeInventory_HandlesCorrectly()
    {
        // Arrange
        var itemIds = Enumerable.Range(1, 100).ToArray();
        var inventory = CreateInventory(itemIds);

        // Act
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment(inventory.Select(slot => slot.Itemstack)), null, inventory, null).CandidateItemIds;

        // Assert
        Assert.Equal(100, result.Count);
        foreach (var id in itemIds)
        {
            Assert.Contains(id, result);
        }
    }
    #endregion

    #region Private
    /// <summary>
    /// Helper to create an InventoryGeneric with items for testing.
    /// </summary>
    private static InventoryGeneric CreateInventory(params int[] collectibleIds)
    {
        var inv = new InventoryGeneric(collectibleIds.Length, "test", "test-1", null!);
        for (int i = 0; i < collectibleIds.Length; i++)
        {
            var item = new MockItem(collectibleIds[i]);
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
            var item = new MockItem(itemIds[i]);
            inv[i].Itemstack = new ItemStack(item);
        }
        // Remaining slots are already empty by default
        return inv;
    }

    #endregion
}
