using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.EquipLightSource;

/// <summary>
/// Tests for the TryFindBrightestLightSource method in EquipLightSource.
/// Verifies that the brightest light source is always selected from an inventory.
/// </summary>
[Trait("Category", "Unit")]
public class FindBrightestLightSourceTests
{
    #region Empty/No Light Source Tests

    [Fact]
    public void TryFindBrightestLightSource_EmptyInventory_ReturnsFalse()
    {
        // Arrange
        var inventory = MockInventory.Empty(slotCount: 10);

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.False(result);
        Assert.Null(slot);
    }

    [Fact]
    public void TryFindBrightestLightSource_NoLightSources_ReturnsFalse()
    {
        // Arrange - inventory with only non-light items
        var inventory = MockInventory.WithItems(
            MockItem.CreateNonLightSource(id: 1),
            MockItem.CreateNonLightSource(id: 2),
            MockItem.CreateNonLightSource(id: 3)
        );

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.False(result);
        Assert.Null(slot);
    }

    #endregion

    #region Single Light Source Tests

    [Fact]
    public void TryFindBrightestLightSource_SingleLightSource_ReturnsIt()
    {
        // Arrange
        var lightSource = MockItem.CreateLightSource(id: 1, brightness: 20);
        var inventory = MockInventory.WithItems(lightSource);

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.True(result);
        Assert.NotNull(slot);
        Assert.Equal(20, slot!.Itemstack.Collectible.LightHsv[2]);
    }

    [Fact]
    public void TryFindBrightestLightSource_SingleLightSourceAmongNonLights_ReturnsLightSource()
    {
        // Arrange - light source in middle of non-light items
        var inventory = MockInventory.WithItems(new Dictionary<int, MockItem>
        {
            { 0, MockItem.CreateNonLightSource(id: 1) },
            { 1, MockItem.CreateLightSource(id: 2, brightness: 15) },
            { 2, MockItem.CreateNonLightSource(id: 3) },
        }, totalSlots: 5);

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.True(result);
        Assert.NotNull(slot);
        Assert.Equal(15, slot!.Itemstack.Collectible.LightHsv[2]);
    }

    #endregion

    #region Multiple Light Sources - Brightness Selection Tests

    [Fact]
    public void TryFindBrightestLightSource_MultipleLightSources_ReturnsBrightest()
    {
        // Arrange - multiple light sources with different brightness levels
        var inventory = MockInventory.WithItems(new Dictionary<int, MockItem>
        {
            { 0, MockItem.CreateLightSource(id: 1, brightness: 10) },
            { 1, MockItem.CreateLightSource(id: 2, brightness: 25) }, // Brightest
            { 2, MockItem.CreateLightSource(id: 3, brightness: 15) },
        }, totalSlots: 5);

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.True(result);
        Assert.NotNull(slot);
        Assert.Same(inventory[1], slot);
    }

    [Fact]
    public void TryFindBrightestLightSource_BrightestAtEnd_ReturnsBrightest()
    {
        // Arrange - brightest light source at the end
        var inventory = MockInventory.WithItems(new Dictionary<int, MockItem>
        {
            { 0, MockItem.CreateLightSource(id: 1, brightness: 5) },
            { 1, MockItem.CreateLightSource(id: 2, brightness: 10) },
            { 2, MockItem.CreateLightSource(id: 3, brightness: 31) }, // Brightest at end
        }, totalSlots: 5);

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.True(result);
        Assert.Same(inventory[2], slot);
    }

    [Fact]
    public void TryFindBrightestLightSource_EqualBrightness_ReturnsFirst()
    {
        // Arrange - multiple light sources with equal brightness
        var inventory = MockInventory.WithItems(new Dictionary<int, MockItem>
        {
            { 0, MockItem.CreateLightSource(id: 1, brightness: 20) },
            { 1, MockItem.CreateLightSource(id: 2, brightness: 20) },
            { 2, MockItem.CreateLightSource(id: 3, brightness: 20) },
        }, totalSlots: 5);

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.True(result);
        Assert.NotNull(slot);
        // When equal brightness, should return first encountered
        Assert.Equal(1, slot!.Itemstack.Collectible.Id);
    }

    #endregion

    #region Brightness Level Edge Cases

    [Theory]
    [InlineData(1)]   // Minimum brightness
    [InlineData(15)]  // Medium brightness  
    [InlineData(31)]  // Maximum brightness
    public void TryFindBrightestLightSource_VariousBrightnessLevels_FindsLightSource(byte brightness)
    {
        // Arrange
        var inventory = MockInventory.WithItems(MockItem.CreateLightSource(id: 1, brightness: brightness));

        // Act
        bool result = VanillaExpanded.Lighting.LightSourceSelection.TryFindBrightestLightSource(inventory.Object, out var slot);

        // Assert
        Assert.True(result);
        Assert.NotNull(slot);
        Assert.Equal(brightness, slot!.Itemstack.Collectible.LightHsv[2]);
    }

    #endregion
}
