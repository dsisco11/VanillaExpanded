using Moq;

using Vintagestory.API.Common;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for GetStashableItems method with BlockEntityBloomery.
/// Tests bloomery-specific validation: burning state, output slot, and item acceptance (fuel vs ore).
/// </summary>
[Trait("Category", "Unit")]
public class GetStashableItemsBloomeryTests
{
    /// <summary>
    /// Creates a mock API with World for stack comparisons.
    /// </summary>
    private static ICoreAPI CreateMockApi()
    {
        var worldMock = new Mock<IWorldAccessor>();
        var apiMock = new Mock<ICoreAPI>();
        apiMock.Setup(a => a.World).Returns(worldMock.Object);
        return apiMock.Object;
    }

    #region Null/Empty Tests

    [Fact]
    public void GetStashableItems_NullBloomery_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(MockItem.CreateBloomeryFuel(1)))
            .WithHotbar(MockInventory.Empty());

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, null);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_EmptyPlayerInventory_ReturnsEmptySet()
    {
        // Arrange
        var player = new MockPlayer()
            .WithBackpack(MockInventory.Empty())
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Bloomery State Validation Tests

    [Fact]
    public void GetStashableItems_BloomeryIsBurning_ReturnsEmptySet()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty().AsBurning(true);

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_BloomeryOutputNotEmpty_ReturnsEmptySet()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var output = MockItem.CreateNonCombustible(99);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty().WithOutput(output);

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_BloomeryNotBurningAndOutputEmpty_ReturnsStashableItems()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    #endregion

    #region Fuel Item Tests

    [Fact]
    public void GetStashableItems_PlayerHasValidFuel_ReturnsFuelId()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    [Fact]
    public void GetStashableItems_PlayerHasMultipleFuelStacks_ReturnsUniqueFuelId()
    {
        // Arrange
        var fuel1 = MockItem.CreateBloomeryFuel(1);
        var fuel2 = MockItem.CreateBloomeryFuel(1); // Same ID
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel1, fuel2))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    [Fact]
    public void GetStashableItems_BloomeryFuelSlotFull_ReturnsEmptySet()
    {
        // Arrange
        var api = CreateMockApi();
        var existingFuel = MockItem.CreateBloomeryFuel(1, api);
        var playerFuel = MockItem.CreateBloomeryFuel(1, api);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(playerFuel))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty(api: api).WithFuel(existingFuel, stackSize: 6); // Full (capacity = 6)

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_BloomeryFuelSlotPartiallyFull_ReturnsFuelId()
    {
        // Arrange
        var api = CreateMockApi();
        var existingFuel = MockItem.CreateBloomeryFuel(1, api);
        var playerFuel = MockItem.CreateBloomeryFuel(1, api);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(playerFuel))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty(api: api).WithFuel(existingFuel, stackSize: 3); // Partial (capacity = 6)

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    #endregion

    #region Ore Item Tests

    [Fact]
    public void GetStashableItems_PlayerHasValidOre_ReturnsOreId()
    {
        // Arrange
        var ore = MockItem.CreateBloomeryOre(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(ore))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void GetStashableItems_BloomeryOreSlotHasDifferentOre_ReturnsEmptySet()
    {
        // Arrange
        var api = CreateMockApi();
        var existingOre = MockItem.CreateBloomeryOre(2, api: api);
        var playerOre = MockItem.CreateBloomeryOre(3, api: api); // Different ore type
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(playerOre))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty(api: api).WithOre(existingOre, stackSize: 1);

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_BloomeryOreSlotHasSameOre_ReturnsOreId()
    {
        // Arrange
        var api = CreateMockApi();
        var existingOre = MockItem.CreateBloomeryOre(2, api: api);
        var playerOre = MockItem.CreateBloomeryOre(2, api: api); // Same ore type
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(playerOre))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty(api: api).WithOre(existingOre, stackSize: 1);

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(2, result);
    }

    #endregion

    #region Mixed Fuel and Ore Tests

    [Fact]
    public void GetStashableItems_PlayerHasBothFuelAndOre_ReturnsBothIds()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var ore = MockItem.CreateBloomeryOre(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel, ore))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    [Fact]
    public void GetStashableItems_PlayerHasFuelInBackpackOreInHotbar_ReturnsBothIds()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var ore = MockItem.CreateBloomeryOre(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.WithItems(ore));
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result);
        Assert.Contains(2, result);
    }

    #endregion

    #region Invalid Item Tests

    [Fact]
    public void GetStashableItems_PlayerHasNonCombustibleItems_ReturnsEmptySet()
    {
        // Arrange
        var nonCombustible = MockItem.CreateNonCombustible(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(nonCombustible))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_PlayerHasLowTempCombustible_ReturnsEmptySet()
    {
        // Arrange
        var lowTemp = MockItem.CreateLowTempCombustible(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(lowTemp))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_PlayerHasMixedValidAndInvalidItems_ReturnsOnlyValidIds()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(1);
        var nonCombustible = MockItem.CreateNonCombustible(2);
        var lowTemp = MockItem.CreateLowTempCombustible(3);
        var ore = MockItem.CreateBloomeryOre(4);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel, nonCombustible, lowTemp, ore))
            .WithHotbar(MockInventory.Empty());
        var bloomery = MockBlockEntityBloomery.Empty();

        // Act
        var result = BlockBehaviorAutoStashable.GetStashableItems(player.Object, bloomery.Object);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result); // Fuel
        Assert.Contains(4, result); // Ore
        Assert.DoesNotContain(2, result); // Non-combustible
        Assert.DoesNotContain(3, result); // Low temp
    }

    #endregion
}
