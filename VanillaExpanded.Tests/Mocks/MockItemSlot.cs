using Moq;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// Factory methods for creating mock ItemSlots for testing.
/// </summary>
public static class MockItemSlot
{
    /// <summary>
    /// Creates a mock empty ItemSlot.
    /// </summary>
    public static Mock<ItemSlot> CreateEmpty()
    {
        var mock = new Mock<ItemSlot>();
        mock.Setup(s => s.Empty).Returns(true);
        mock.Setup(s => s.Itemstack).Returns((ItemStack?)null);
        return mock;
    }

    /// <summary>
    /// Creates a mock ItemSlot containing an item with the specified collectible ID.
    /// </summary>
    public static Mock<ItemSlot> CreateWithItem(int collectibleId, byte lightValue = 0)
    {
        var collectible = MockCollectible.Create(collectibleId, lightValue);
        
        var itemStack = new Mock<ItemStack>();
        itemStack.Setup(i => i.Collectible).Returns(collectible.Object);

        var mock = new Mock<ItemSlot>();
        mock.Setup(s => s.Empty).Returns(false);
        mock.Setup(s => s.Itemstack).Returns(itemStack.Object);
        
        return mock;
    }

    /// <summary>
    /// Creates a mock ItemSlot containing a light source with the specified brightness.
    /// </summary>
    public static Mock<ItemSlot> CreateLightSource(int collectibleId, byte brightness)
    {
        return CreateWithItem(collectibleId, brightness);
    }
}
