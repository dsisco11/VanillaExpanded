using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for GetStashableItems method in BlockBehaviorAutoStashable.
/// Tests item matching between player inventory and container contents.
/// </summary>
[Trait("Category", "Unit")]
public class GetStashableItemsTests
{
    #region Empty Container Tests

    [Fact]
    public void GetStashableItems_NullContainer_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2)))
            .WithHotbar(MockInventory.Empty());

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_EmptyContainer_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Empty Player Inventory Tests

    [Fact]
    public void GetStashableItems_EmptyPlayerInventory_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.Empty())
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(1), new MockItem(2));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region No Matching Items Tests

    [Fact]
    public void GetStashableItems_NoMatchingItems_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(3), new MockItem(4));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_PlayerHotbarOnlyNoMatch_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.Empty())
            .WithHotbar(MockInventory.WithItems(new MockItem(1), new MockItem(2)));
        var container = MockBlockEntityContainer.WithItems(new MockItem(3));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Partial Overlap Tests

    [Fact]
    public void GetStashableItems_PartialOverlap_ReturnsOnlyMatchingItems()
    {
        // Arrange - Player has items 1, 2, 3; Container has items 2, 3, 4
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2), new MockItem(3)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(2), new MockItem(3), new MockItem(4));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
        Assert.DoesNotContain(1, result);
        Assert.DoesNotContain(4, result);
    }

    [Fact]
    public void GetStashableItems_SingleMatchingItem_ReturnsSingleItem()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(2), new MockItem(3));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(2, result);
    }

    #endregion

    #region Full Overlap Tests

    [Fact]
    public void GetStashableItems_FullOverlap_ReturnsAllContainerItems()
    {
        // Arrange - Player has items 1, 2, 3; Container has items 1, 2
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2), new MockItem(3)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(1), new MockItem(2));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void GetStashableItems_IdenticalContents_ReturnsAllItems()
    {
        // Arrange - Both have items 1, 2, 3
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(2), new MockItem(3)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(1), new MockItem(2), new MockItem(3));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
        Assert.Contains(3, result);
    }

    #endregion

    #region Backpack and Hotbar Combined Tests

    [Fact]
    public void GetStashableItems_MatchInBackpackOnly_ReturnsMatch()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1)))
            .WithHotbar(MockInventory.WithItems(new MockItem(2)));
        var container = MockBlockEntityContainer.WithItems(new MockItem(1));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    [Fact]
    public void GetStashableItems_MatchInHotbarOnly_ReturnsMatch()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1)))
            .WithHotbar(MockInventory.WithItems(new MockItem(2)));
        var container = MockBlockEntityContainer.WithItems(new MockItem(2));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void GetStashableItems_MatchesInBothInventories_ReturnsAllMatches()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1)))
            .WithHotbar(MockInventory.WithItems(new MockItem(2)));
        var container = MockBlockEntityContainer.WithItems(new MockItem(1), new MockItem(2));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    #endregion

    #region Duplicate Items Tests

    [Fact]
    public void GetStashableItems_DuplicateItemsInPlayer_ReturnsUniqueIds()
    {
        // Arrange - Player has multiple stacks of item 1
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1), new MockItem(1), new MockItem(1)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(1));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    [Fact]
    public void GetStashableItems_DuplicateItemsInContainer_ReturnsUniqueIds()
    {
        // Arrange - Container has multiple stacks of item 1
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(new MockItem(1)))
            .WithHotbar(MockInventory.Empty());
        var container = MockBlockEntityContainer.WithItems(new MockItem(1), new MockItem(1), new MockItem(1));

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, container.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    #endregion
}
