using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for GetPlacedBlockInteractionHelp method with BlockEntityBloomery.
/// Verifies the correct interaction help text and item stacks are returned.
/// </summary>
[Trait("Category", "Unit")]
public class BloomeryInteractionHelpTests
{
    private const string BloomeryLangCode = "vanillaexpanded:blockhelp-autostash-bloomery";
    private static readonly BlockPos DefaultPos = new(0, 0, 0, 0);

    /// <summary>
    /// Creates a Block configured as a Bloomery by setting the EntityClass field.
    /// </summary>
    private static Block CreateBloomeryBlock()
    {
        var block = new Block();
        block.EntityClass = "Bloomery";
        return block;
    }

    /// <summary>
    /// Creates a mock IWorldAccessor with the given block entity at position.
    /// Note: Using simple inline mock setup due to Moq matcher issues with BlockPos in MockBlockAccessor.
    /// </summary>
    private static Mock<IWorldAccessor> CreateMockWorld(BlockEntity? blockEntity, BlockPos? position = null)
    {
        position ??= DefaultPos;

        var blockAccessorMock = new Mock<IBlockAccessor>();
        blockAccessorMock
            .Setup(ba => ba.GetBlockEntity(position))
            .Returns(blockEntity);

        var worldMock = new Mock<IWorldAccessor>();
        worldMock.Setup(w => w.BlockAccessor).Returns(blockAccessorMock.Object);

        return worldMock;
    }

    /// <summary>
    /// Creates a BlockSelection for the given position.
    /// </summary>
    private static BlockSelection CreateBlockSelection(BlockPos? position = null)
    {
        position ??= DefaultPos;
        return new BlockSelection { Position = position };
    }

    #region Empty/No Stashables Tests

    [Fact]
    public void GetPlacedBlockInteractionHelp_NoStashableItems_ReturnsEmptyArray()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var player = new MockPlayer()
            .WithBackpack(MockInventory.Empty())
            .WithHotbar(MockInventory.Empty());

        var bloomery = MockBlockEntityBloomery.Empty();
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_BloomeryIsBurning_ReturnsEmptyArray()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel = MockItem.CreateBloomeryFuel(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(fuel);

        var bloomery = MockBlockEntityBloomery.Empty().AsBurning(true);
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_BloomeryOutputNotEmpty_ReturnsEmptyArray()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel = MockItem.CreateBloomeryFuel(1);
        var output = MockItem.CreateNonCombustible(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(fuel);

        var bloomery = MockBlockEntityBloomery.Empty().WithOutput(output);
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_EmptyBloomeryAndNoValidActiveItem_ReturnsEmptyArray()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel = MockItem.CreateBloomeryFuel(1);
        var nonCombustible = MockItem.CreateNonCombustible(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(nonCombustible); // Active item is not valid for bloomery

        var bloomery = MockBlockEntityBloomery.Empty();
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Valid Stashables Tests

    [Fact]
    public void GetPlacedBlockInteractionHelp_PlayerHasFuel_ReturnsInteractionWithFuelStack()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel = MockItem.CreateBloomeryFuel(1);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(fuel);

        var bloomery = MockBlockEntityBloomery.Empty();
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Single(result);
        Assert.Equal(BloomeryLangCode, result[0].ActionLangCode);
        Assert.Equal(EnumMouseButton.Right, result[0].MouseButton);
        Assert.NotNull(result[0].Itemstacks);
        Assert.Single(result[0].Itemstacks);
        Assert.Equal(1, result[0].Itemstacks[0].Collectible.Id);
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_PlayerHasOre_ReturnsInteractionWithOreStack()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var ore = MockItem.CreateBloomeryOre(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(ore))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(ore);

        var bloomery = MockBlockEntityBloomery.Empty();
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Single(result);
        Assert.Equal(BloomeryLangCode, result[0].ActionLangCode);
        Assert.Equal(EnumMouseButton.Right, result[0].MouseButton);
        Assert.NotNull(result[0].Itemstacks);
        Assert.Single(result[0].Itemstacks);
        Assert.Equal(2, result[0].Itemstacks[0].Collectible.Id);
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_PlayerHasBothFuelAndOre_ReturnsInteractionWithBothStacks()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel = MockItem.CreateBloomeryFuel(1);
        var ore = MockItem.CreateBloomeryOre(2);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel, ore))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(fuel);

        var bloomery = MockBlockEntityBloomery.Empty();
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Single(result);
        Assert.Equal(BloomeryLangCode, result[0].ActionLangCode);
        Assert.Equal(EnumMouseButton.Right, result[0].MouseButton);
        Assert.NotNull(result[0].Itemstacks);
        Assert.Equal(2, result[0].Itemstacks.Length);

        var stackIds = result[0].Itemstacks.Select(s => s.Collectible.Id).OrderBy(id => id).ToArray();
        Assert.Equal(1, stackIds[0]); // Fuel
        Assert.Equal(2, stackIds[1]); // Ore
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_BloomeryHasExistingFuel_ShowsInteractionWhenPlayerHasMatchingFuel()
    {
        // Arrange
        var api = CreateMockApi();
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel = MockItem.CreateBloomeryFuel(1, api);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel))
            .WithHotbar(MockInventory.Empty());

        // Bloomery already has fuel, so player doesn't need to have it as active item
        var bloomery = MockBlockEntityBloomery.Empty(api: api).WithFuel(fuel, stackSize: 1);
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Single(result);
        Assert.Equal(BloomeryLangCode, result[0].ActionLangCode);
        Assert.NotNull(result[0].Itemstacks);
        Assert.Single(result[0].Itemstacks);
    }

    [Fact]
    public void GetPlacedBlockInteractionHelp_MultipleStacksOfSameItem_ReturnsUniqueItemStacksOnly()
    {
        // Arrange
        var block = CreateBloomeryBlock();
        var behavior = new BlockBehaviorAutoStashable(block);

        var fuel1 = MockItem.CreateBloomeryFuel(1);
        var fuel2 = MockItem.CreateBloomeryFuel(1); // Same ID
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(fuel1, fuel2))
            .WithHotbar(MockInventory.Empty())
            .WithActiveHotbarItem(fuel1);

        var bloomery = MockBlockEntityBloomery.Empty();
        var world = CreateMockWorld(bloomery.Object);
        var selection = CreateBlockSelection();

        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        var result = behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling);

        // Assert
        Assert.Single(result);
        Assert.NotNull(result[0].Itemstacks);
        Assert.Single(result[0].Itemstacks); // Should be deduplicated
        Assert.Equal(1, result[0].Itemstacks[0].Collectible.Id);
    }

    #endregion

    #region Helper Methods

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

    #endregion
}
