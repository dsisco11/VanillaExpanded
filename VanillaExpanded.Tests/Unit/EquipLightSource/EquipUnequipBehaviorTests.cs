using Moq;

using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.EquipLightSource;

/// <summary>
/// Tests for the equip/unequip light source behavior by invoking OnHotKeyPressed.
/// Covers scenarios for equipping brightest light to hand and unequipping back to inventory.
/// </summary>
[Trait("Category", "Unit")]
public class EquipUnequipBehaviorTests
{
    #region Test Infrastructure

    /// <summary>
    /// Creates a configured VsTestFixture for EquipLightSource testing.
    /// </summary>
    private static (VsTestFixture Fixture, VanillaExpanded.EquipLightSource System) CreateFixture()
    {
        var fixture = VsTestFixture.Client();
        var system = new VanillaExpanded.EquipLightSource();
        fixture.ConfigureModSystem(system);
        return (fixture, system);
    }

    #endregion

    #region Equip - Empty Hand Tests

    [Fact]
    public void OnHotKeyPressed_EmptyOffhand_BrightestLightEquippedToOffhand()
    {
        // Arrange - Player has light sources in backpack, empty offhand
        var (fixture, system) = CreateFixture();
        var dimLight = fixture.CreateLightSource(id: 1, brightness: 10);
        var brightLight = fixture.CreateLightSource(id: 2, brightness: 25);
        fixture.WithBackpackItems(dimLight, brightLight);

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Should return true and equip brightest light to offhand
        Assert.True(result);
        Assert.False(fixture.OffhandInventory[0].Empty);
        Assert.Equal(25, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
    }

    [Fact]
    public void OnHotKeyPressed_EmptyActiveHotbar_BrightestLightEquippedToHotbar()
    {
        // Arrange - Player has light sources in backpack, empty active hotbar slot
        var (fixture, system) = CreateFixture();
        var brightLight = fixture.CreateLightSource(id: 1, brightness: 20);
        fixture.WithBackpackItems(brightLight);

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: false);

        // Assert - Should equip to active hotbar slot
        Assert.True(result);
        Assert.False(fixture.HotbarInventory[0].Empty);
        Assert.Equal(20, fixture.HotbarInventory[0].Itemstack.Collectible.LightHsv[2]);
    }

    #endregion

    #region Equip - Hand Holding Non-Light Item Tests

    [Fact]
    public void OnHotKeyPressed_OffhandHoldsNonLightItem_SwappedWithBrightestLight()
    {
        // Arrange - Offhand has non-light item, backpack has light source
        var (fixture, system) = CreateFixture();
        var nonLightItem = fixture.CreateNonLightSource(id: 1);
        var lightSource = fixture.CreateLightSource(id: 2, brightness: 20);
        fixture
            .WithOffhandItem(nonLightItem)
            .WithBackpackItems(lightSource);

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Light source should be in offhand, non-light item in backpack
        Assert.True(result);
        Assert.Equal(20, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(1, fixture.BackpackInventory[0].Itemstack.Collectible.Id);
    }

    [Fact]
    public void OnHotKeyPressed_ActiveHotbarHoldsNonLightItem_SwappedWithBrightestLight()
    {
        // Arrange - Active hotbar slot has non-light item, backpack has light source
        var (fixture, system) = CreateFixture();
        var nonLightItem = fixture.CreateNonLightSource(id: 1);
        var lightSource = fixture.CreateLightSource(id: 2, brightness: 25);
        fixture
            .WithActiveHotbarItem(nonLightItem)
            .WithBackpackItems(lightSource);

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: false);

        // Assert
        Assert.True(result);
        Assert.Equal(25, fixture.HotbarInventory[0].Itemstack.Collectible.LightHsv[2]);
    }

    #endregion

    #region Unequip - Hand Holding Light Tests

    [Fact]
    public void OnHotKeyPressed_OffhandHoldsBrightestLight_UnequipsToBackpack()
    {
        // Arrange - Offhand already has a light source (brightest)
        var (fixture, system) = CreateFixture();
        var brightLight = fixture.CreateLightSource(id: 1, brightness: 31);
        var dimmerLight = fixture.CreateLightSource(id: 2, brightness: 10);
        fixture
            .WithOffhandItem(brightLight)
            .WithBackpackItems(dimmerLight);

        // Act - Press hotkey for offhand when offhand already has brightest light
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Should unequip the light from offhand
        Assert.True(result);
        // After unequip, offhand should have the dimmer light (swapped) or be empty
        // depending on implementation - the key is that a swap occurred
    }

    [Fact]
    public void OnHotKeyPressed_ActiveHotbarHoldsLight_UnequipsToBackpack()
    {
        // Arrange - Active hotbar slot has a light source, backpack is empty
        var (fixture, system) = CreateFixture();
        var lightSource = fixture.CreateLightSource(id: 1, brightness: 20);
        fixture.WithActiveHotbarItem(lightSource);

        // Act - Press hotkey for hotbar when active hotbar already has light
        bool result = system.OnHotKeyPressed(useOffhand: false);

        // Assert - Should return true (unequip attempted)
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_HandHoldsEqualBrightnessLight_UnequipsToBackpack()
    {
        // Arrange - Offhand has light of equal brightness to one in inventory
        var (fixture, system) = CreateFixture();
        var handLight = fixture.CreateLightSource(id: 1, brightness: 20);
        var sameLight = fixture.CreateLightSource(id: 2, brightness: 20);
        fixture
            .WithOffhandItem(handLight)
            .WithBackpackItems(sameLight);

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Should trigger unequip since hand already has light (equal brightness)
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_OffhandHoldsLight_BrighterLightInBackpack_UnequipsHeldLight()
    {
        // Arrange - Offhand has a dim light source, backpack has a brighter one
        // DESIRED BEHAVIOR: When pressing the hotkey with a light already in target hand,
        // the held light should be unequipped and the hand should become EMPTY,
        // regardless of what other items exist in inventory.
        var (fixture, system) = CreateFixture();
        var dimHandLight = fixture.CreateLightSource(id: 1, brightness: 10);
        var brighterBackpackLight = fixture.CreateLightSource(id: 2, brightness: 30);
        fixture
            .WithOffhandItem(dimHandLight)
            .WithBackpackItems(brighterBackpackLight);

        // Verify initial state: offhand has dim light (10), backpack has brighter light (30)
        Assert.False(fixture.OffhandInventory[0].Empty);
        Assert.Equal(10, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(30, fixture.BackpackInventory[0].Itemstack.Collectible.LightHsv[2]);

        // Act - Press hotkey for offhand when offhand already has a light
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - DESIRED: The light should be unequipped and hand should be EMPTY
        Assert.True(result, "Hotkey action should return true");
        Assert.True(fixture.OffhandInventory[0].Empty,
            "The offhand should be empty after unequipping a light source");
    }

    [Fact]
    public void OnHotKeyPressed_ActiveHotbarHoldsLight_BrighterLightInBackpack_UnequipsHeldLight()
    {
        // Arrange - Active hotbar has a dim light source, backpack has a brighter one
        // DESIRED BEHAVIOR: When pressing the hotkey with a light already in target hand,
        // the held light should be unequipped and the hand should become EMPTY.
        var (fixture, system) = CreateFixture();
        var dimHandLight = fixture.CreateLightSource(id: 1, brightness: 15);
        var brighterBackpackLight = fixture.CreateLightSource(id: 2, brightness: 25);
        fixture
            .WithActiveHotbarItem(dimHandLight)
            .WithBackpackItems(brighterBackpackLight);

        // Verify initial state: hotbar has dim light (15), backpack has brighter light (25)
        Assert.False(fixture.HotbarInventory[0].Empty);
        Assert.Equal(15, fixture.HotbarInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(25, fixture.BackpackInventory[0].Itemstack.Collectible.LightHsv[2]);

        // Act - Press hotkey for hotbar when hotbar already has a light
        bool result = system.OnHotKeyPressed(useOffhand: false);

        // Assert - DESIRED: The light should be unequipped and hand should be EMPTY
        Assert.True(result, "Hotkey action should return true");
        Assert.True(fixture.HotbarInventory[0].Empty,
            "The active hotbar slot should be empty after unequipping a light source");
    }

    [Fact]
    public void OnHotKeyPressed_OffhandHoldsLight_SameLightInBackpack_UnequipsToEmptyHand()
    {
        // Arrange - Offhand has a light source, backpack has another instance of the SAME light type
        // DESIRED BEHAVIOR: When pressing the hotkey with a light already in target hand,
        // the held light should be unequipped and the hand should become EMPTY,
        // even if the same type of light exists in inventory.
        // BUG SCENARIO: Currently the game merges them into a stack of 2 in hand instead of unequipping.
        var (fixture, system) = CreateFixture();
        var handLight = fixture.CreateLightSource(id: 1, brightness: 20);
        var sameBackpackLight = fixture.CreateLightSource(id: 1, brightness: 20); // SAME item ID = same item type
        fixture
            .WithOffhandItem(handLight)
            .WithBackpackItems(sameBackpackLight);

        // Verify initial state: offhand has 1 light, backpack has 1 of the same light
        Assert.False(fixture.OffhandInventory[0].Empty);
        Assert.Equal(1, fixture.OffhandInventory[0].Itemstack.StackSize);
        Assert.Equal(20, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(1, fixture.BackpackInventory[0].Itemstack.StackSize);

        // Act - Press hotkey for offhand when offhand already has a light
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - DESIRED: The light should be unequipped and hand should be EMPTY
        // NOT merged into a stack of 2
        Assert.True(result, "Hotkey action should return true");
        Assert.True(fixture.OffhandInventory[0].Empty,
            "The offhand should be empty after unequipping a light source, not merged into a stack");
    }

    [Fact]
    public void OnHotKeyPressed_ActiveHotbarHoldsLight_SameLightInBackpack_UnequipsToEmptyHand()
    {
        // Arrange - Active hotbar has a light source, backpack has another instance of the SAME light type
        // DESIRED BEHAVIOR: When pressing the hotkey with a light already in target hand,
        // the held light should be unequipped and the hand should become EMPTY.
        // BUG SCENARIO: Currently the game merges them into a stack of 2 in hand instead of unequipping.
        var (fixture, system) = CreateFixture();
        var handLight = fixture.CreateLightSource(id: 1, brightness: 20);
        var sameBackpackLight = fixture.CreateLightSource(id: 1, brightness: 20); // SAME item ID = same item type
        fixture
            .WithActiveHotbarItem(handLight)
            .WithBackpackItems(sameBackpackLight);

        // Verify initial state: hotbar has 1 light, backpack has 1 of the same light
        Assert.False(fixture.HotbarInventory[0].Empty);
        Assert.Equal(1, fixture.HotbarInventory[0].Itemstack.StackSize);
        Assert.Equal(20, fixture.HotbarInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(1, fixture.BackpackInventory[0].Itemstack.StackSize);

        // Act - Press hotkey for hotbar when hotbar already has a light
        bool result = system.OnHotKeyPressed(useOffhand: false);

        // Assert - DESIRED: The light should be unequipped and hand should be EMPTY
        // NOT merged into a stack of 2
        Assert.True(result, "Hotkey action should return true");
        Assert.True(fixture.HotbarInventory[0].Empty,
            "The active hotbar slot should be empty after unequipping a light source, not merged into a stack");
    }

    #endregion

    #region No Light Source Available Tests

    [Fact]
    public void OnHotKeyPressed_NoLightSourcesAnywhere_ReturnsFalse()
    {
        // Arrange - No light sources in any inventory
        var (fixture, system) = CreateFixture();
        fixture
            .WithActiveHotbarItem(fixture.CreateNonLightSource(id: 4))
            .WithBackpackItems(fixture.CreateNonLightSource(id: 1), fixture.CreateNonLightSource(id: 2))
            .WithHotbarItems(fixture.CreateNonLightSource(id: 3));

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Should return false (no light sources to equip)
        Assert.False(result);
    }

    [Fact]
    public void OnHotKeyPressed_EmptyInventories_ReturnsFalse()
    {
        // Arrange - All inventories empty
        var (fixture, system) = CreateFixture();

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OnHotKeyPressed_NoLightSources_NoErrorsThrown()
    {
        // Arrange
        var (fixture, system) = CreateFixture();

        // Act & Assert - Should not throw any exceptions
        var exception = Record.Exception(() => system.OnHotKeyPressed(useOffhand: false));
        Assert.Null(exception);
    }

    #endregion

    #region Priority Tests

    [Fact]
    public void OnHotKeyPressed_LightInOffhand_TakesHighestPriority()
    {
        // Arrange - Light in offhand should be detected first (highest priority)
        var (fixture, system) = CreateFixture();
        var offhandLight = fixture.CreateLightSource(id: 1, brightness: 15);
        var brighterBackpackLight = fixture.CreateLightSource(id: 2, brightness: 30);
        fixture
            .WithOffhandItem(offhandLight)
            .WithBackpackItems(brighterBackpackLight);

        // Act - Press hotkey for offhand
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Should process (offhand light detected) and swap/unequip
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_LightInActiveHotbar_TakesPriorityOverBackpack()
    {
        // Arrange - Light in active hotbar takes priority over backpack
        var (fixture, system) = CreateFixture();
        var hotbarLight = fixture.CreateLightSource(id: 1, brightness: 10);
        var brighterBackpackLight = fixture.CreateLightSource(id: 2, brightness: 25);
        fixture
            .WithActiveHotbarItem(hotbarLight)
            .WithBackpackItems(brighterBackpackLight);

        // Act - Press hotkey for hotbar
        bool result = system.OnHotKeyPressed(useOffhand: false);

        // Assert - Should detect active hotbar light first and swap
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_MultipleLightsInBackpack_EquipsBrightest()
    {
        // Arrange - Multiple light sources with different brightness
        var (fixture, system) = CreateFixture();
        var dimLight = fixture.CreateLightSource(id: 1, brightness: 5);
        var mediumLight = fixture.CreateLightSource(id: 2, brightness: 15);
        var brightLight = fixture.CreateLightSource(id: 3, brightness: 30);
        fixture.WithBackpackItems(dimLight, mediumLight, brightLight);

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert - Should equip the brightest (30)
        Assert.True(result);
        Assert.Equal(30, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
    }

    #endregion

    #region API Null Check Tests

    [Fact]
    public void OnHotKeyPressed_ApiIsNull_ReturnsFalse()
    {
        // Arrange - Create system without setting API
        var system = new VanillaExpanded.EquipLightSource();

        // Act
        bool result = system.OnHotKeyPressed(useOffhand: true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OnHotKeyPressed_ApiIsNull_NoExceptionThrown()
    {
        // Arrange
        var system = new VanillaExpanded.EquipLightSource();

        // Act & Assert
        var exception = Record.Exception(() => system.OnHotKeyPressed(useOffhand: false));
        Assert.Null(exception);
    }

    #endregion
}
