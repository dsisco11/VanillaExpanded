using Moq;

using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.EquipLightSource;

/// <summary>
/// Tests for the IsLightSource detection methods in EquipLightSource.
/// </summary>
public class LightSourceDetectionTests
{
    #region IsLightSource(ItemSlot) Tests

    [Fact]
    public void IsLightSource_NullSlot_ReturnsFalse()
    {
        // Arrange
        ItemSlot? slot = null;

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(slot);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLightSource_EmptySlot_ReturnsFalse()
    {
        // Arrange
        var slot = MockItemSlot.CreateEmpty();

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(slot.Object);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLightSource_SlotWithNonLightItem_ReturnsFalse()
    {
        // Arrange
        var slot = MockItemSlot.CreateWithItem(collectibleId: 1, lightValue: 0);

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(slot.Object);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLightSource_SlotWithLightSource_ReturnsTrue()
    {
        // Arrange
        var slot = MockItemSlot.CreateLightSource(collectibleId: 1, brightness: 20);

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(slot.Object);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(31)]
    public void IsLightSource_SlotWithVariousBrightnessLevels_ReturnsTrue(byte brightness)
    {
        // Arrange
        var slot = MockItemSlot.CreateLightSource(collectibleId: 1, brightness: brightness);

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(slot.Object);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region IsLightSource(CollectibleObject) Tests

    [Fact]
    public void IsLightSource_NullCollectible_ReturnsFalse()
    {
        // Arrange
        CollectibleObject? item = null;

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(item);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLightSource_NonLightCollectible_ReturnsFalse()
    {
        // Arrange
        var collectible = MockCollectible.CreateNonLightSource(id: 1);

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(collectible.Object);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLightSource_LightCollectible_ReturnsTrue()
    {
        // Arrange
        var collectible = MockCollectible.CreateLightSource(id: 1, brightness: 20);

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(collectible.Object);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(31)]
    public void IsLightSource_CollectibleWithVariousBrightnessLevels_ReturnsTrue(byte brightness)
    {
        // Arrange
        var collectible = MockCollectible.CreateLightSource(id: 1, brightness: brightness);

        // Act
        bool result = VanillaExpanded.EquipLightSource.IsLightSource(collectible.Object);

        // Assert
        Assert.True(result);
    }

    #endregion
}
