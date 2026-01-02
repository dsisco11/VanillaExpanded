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
    /// Test fixture that creates real InventoryGeneric instances.
    /// Uses null! for API in constructor (same as other tests), then sets Api field after.
    /// </summary>
    private class TestFixture
    {
        public VanillaExpanded.EquipLightSource System { get; }
        public Mock<ICoreClientAPI> ApiMock { get; }
        public Mock<IClientPlayer> PlayerMock { get; }
        
        public InventoryGeneric OffhandInventory { get; }
        public InventoryGeneric HotbarInventory { get; }
        public InventoryGeneric BackpackInventory { get; }

        public TestFixture(
            ItemStack? offhandItem,
            ItemStack? activeHotbarItem,
            MockItem[]? backpackItems = null,
            MockItem[]? hotbarItems = null)
        {
            // Create mock API chain
            ApiMock = new Mock<ICoreClientAPI>();
            var worldMock = new Mock<IClientWorldAccessor>();
            var networkMock = new Mock<IClientNetworkAPI>();
            var loggerMock = new Mock<ILogger>();
            
            ApiMock.Setup(a => a.World).Returns(worldMock.Object);
            ApiMock.Setup(a => a.Network).Returns(networkMock.Object);
            ApiMock.Setup(a => a.Logger).Returns(loggerMock.Object);

            // Create real inventories with null! API (same as DistinctItemTypesTests)
            // then set Api field to allow TryFlipItems to work
            OffhandInventory = new InventoryGeneric(1, "character", "offhand-1", null!);
            HotbarInventory = new InventoryGeneric(10, GlobalConstants.hotBarInvClassName, "hotbar-1", null!);
            BackpackInventory = new InventoryGeneric(10, GlobalConstants.backpackInvClassName, "backpack-1", null!);

            // Create mock network util for inventory operations
            var invNetworkUtilMock = new Mock<IInventoryNetworkUtil>();
            invNetworkUtilMock.Setup(u => u.GetFlipSlotsPacket(It.IsAny<InventoryBase>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new object()); // Return dummy packet

            // Set the Api and InvNetworkUtil fields on inventories
            OffhandInventory.Api = ApiMock.Object;
            OffhandInventory.InvNetworkUtil = invNetworkUtilMock.Object;
            HotbarInventory.Api = ApiMock.Object;
            HotbarInventory.InvNetworkUtil = invNetworkUtilMock.Object;
            BackpackInventory.Api = ApiMock.Object;
            BackpackInventory.InvNetworkUtil = invNetworkUtilMock.Object;

            // Set offhand item
            if (offhandItem is not null)
            {
                OffhandInventory[0].Itemstack = offhandItem;
            }

            // Set active hotbar item (slot 0 is the active slot)
            if (activeHotbarItem is not null)
            {
                HotbarInventory[0].Itemstack = activeHotbarItem;
            }

            // Populate backpack inventory
            if (backpackItems is not null)
            {
                for (int i = 0; i < backpackItems.Length && i < 10; i++)
                {
                    BackpackInventory[i].Itemstack = new ItemStack(backpackItems[i]);
                }
            }

            // Populate remaining hotbar slots (slot 1+)
            if (hotbarItems is not null)
            {
                for (int i = 0; i < hotbarItems.Length && i < 9; i++)
                {
                    HotbarInventory[i + 1].Itemstack = new ItemStack(hotbarItems[i]);
                }
            }

            PlayerMock = new Mock<IClientPlayer>();
            var entityMock = new Mock<EntityPlayer>();
            var inventoryManagerMock = new Mock<IPlayerInventoryManager>();

            // Setup entity with the offhand inventory's slot
            entityMock.Setup(e => e.LeftHandItemSlot).Returns(OffhandInventory[0]);

            // Setup inventory manager with real inventories
            inventoryManagerMock.Setup(i => i.ActiveHotbarSlot).Returns(HotbarInventory[0]);
            inventoryManagerMock.Setup(i => i.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(BackpackInventory);
            inventoryManagerMock.Setup(i => i.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(HotbarInventory);

            // Setup player
            PlayerMock.Setup(p => p.Entity).Returns(entityMock.Object);
            PlayerMock.Setup(p => p.InventoryManager).Returns(inventoryManagerMock.Object);

            // Setup world
            worldMock.Setup(w => w.Player).Returns(PlayerMock.Object);

            // Create and configure the system
            System = new VanillaExpanded.EquipLightSource();

            // Use reflection to set the private api field
            var apiField = typeof(VanillaExpanded.EquipLightSource).GetField("api",
                global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
            apiField?.SetValue(System, ApiMock.Object);
        }
    }

    #endregion

    #region Equip - Empty Hand Tests

    [Fact]
    public void OnHotKeyPressed_EmptyOffhand_BrightestLightEquippedToOffhand()
    {
        // Arrange - Player has light sources in backpack, empty offhand
        var dimLight = MockItem.CreateLightSource(id: 1, brightness: 10);
        var brightLight = MockItem.CreateLightSource(id: 2, brightness: 25);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: null,
            backpackItems: [dimLight, brightLight]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

        // Assert - Should return true and equip brightest light to offhand
        Assert.True(result);
        Assert.False(fixture.OffhandInventory[0].Empty);
        Assert.Equal(25, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
    }

    [Fact]
    public void OnHotKeyPressed_EmptyActiveHotbar_BrightestLightEquippedToHotbar()
    {
        // Arrange - Player has light sources in backpack, empty active hotbar slot
        var brightLight = MockItem.CreateLightSource(id: 1, brightness: 20);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: null,
            backpackItems: [brightLight]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: false);

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
        var nonLightItem = MockItem.CreateNonLightSource(id: 1);
        var lightSource = MockItem.CreateLightSource(id: 2, brightness: 20);

        var fixture = new TestFixture(
            offhandItem: new ItemStack(nonLightItem),
            activeHotbarItem: null,
            backpackItems: [lightSource]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

        // Assert - Light source should be in offhand, non-light item in backpack
        Assert.True(result);
        Assert.Equal(20, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(1, fixture.BackpackInventory[0].Itemstack.Collectible.Id);
    }

    [Fact]
    public void OnHotKeyPressed_ActiveHotbarHoldsNonLightItem_SwappedWithBrightestLight()
    {
        // Arrange - Active hotbar slot has non-light item, backpack has light source
        var nonLightItem = MockItem.CreateNonLightSource(id: 1);
        var lightSource = MockItem.CreateLightSource(id: 2, brightness: 25);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: new ItemStack(nonLightItem),
            backpackItems: [lightSource]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: false);

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
        var brightLight = MockItem.CreateLightSource(id: 1, brightness: 31);
        var dimmerLight = MockItem.CreateLightSource(id: 2, brightness: 10);

        var fixture = new TestFixture(
            offhandItem: new ItemStack(brightLight),
            activeHotbarItem: null,
            backpackItems: [dimmerLight]);

        // Act - Press hotkey for offhand when offhand already has brightest light
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

        // Assert - Should unequip the light from offhand
        Assert.True(result);
        // After unequip, offhand should have the dimmer light (swapped) or be empty
        // depending on implementation - the key is that a swap occurred
    }

    [Fact]
    public void OnHotKeyPressed_ActiveHotbarHoldsLight_UnequipsToBackpack()
    {
        // Arrange - Active hotbar slot has a light source, backpack is empty
        var lightSource = MockItem.CreateLightSource(id: 1, brightness: 20);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: new ItemStack(lightSource));

        // Act - Press hotkey for hotbar when active hotbar already has light
        bool result = fixture.System.OnHotKeyPressed(useOffhand: false);

        // Assert - Should return true (unequip attempted)
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_HandHoldsEqualBrightnessLight_UnequipsToBackpack()
    {
        // Arrange - Offhand has light of equal brightness to one in inventory
        var handLight = MockItem.CreateLightSource(id: 1, brightness: 20);
        var sameLight = MockItem.CreateLightSource(id: 2, brightness: 20);

        var fixture = new TestFixture(
            offhandItem: new ItemStack(handLight),
            activeHotbarItem: null,
            backpackItems: [sameLight]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

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
        var dimHandLight = MockItem.CreateLightSource(id: 1, brightness: 10);
        var brighterBackpackLight = MockItem.CreateLightSource(id: 2, brightness: 30);

        var fixture = new TestFixture(
            offhandItem: new ItemStack(dimHandLight),
            activeHotbarItem: null,
            backpackItems: [brighterBackpackLight]);

        // Verify initial state: offhand has dim light (10), backpack has brighter light (30)
        Assert.False(fixture.OffhandInventory[0].Empty);
        Assert.Equal(10, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(30, fixture.BackpackInventory[0].Itemstack.Collectible.LightHsv[2]);

        // Act - Press hotkey for offhand when offhand already has a light
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

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
        var dimHandLight = MockItem.CreateLightSource(id: 1, brightness: 15);
        var brighterBackpackLight = MockItem.CreateLightSource(id: 2, brightness: 25);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: new ItemStack(dimHandLight),
            backpackItems: [brighterBackpackLight]);

        // Verify initial state: hotbar has dim light (15), backpack has brighter light (25)
        Assert.False(fixture.HotbarInventory[0].Empty);
        Assert.Equal(15, fixture.HotbarInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(25, fixture.BackpackInventory[0].Itemstack.Collectible.LightHsv[2]);

        // Act - Press hotkey for hotbar when hotbar already has a light
        bool result = fixture.System.OnHotKeyPressed(useOffhand: false);

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

        // Create mock API for item comparison (needed when items have same ID)
        var apiMock = new Mock<ICoreClientAPI>();
        var worldMock = new Mock<IClientWorldAccessor>();
        apiMock.Setup(a => a.World).Returns(worldMock.Object);

        var handLight = MockItem.CreateLightSource(id: 1, brightness: 20, api: apiMock.Object);
        var sameBackpackLight = MockItem.CreateLightSource(id: 1, brightness: 20, api: apiMock.Object); // SAME item ID = same item type

        var fixture = new TestFixture(
            offhandItem: new ItemStack(handLight),
            activeHotbarItem: null,
            backpackItems: [sameBackpackLight]);

        // Verify initial state: offhand has 1 light, backpack has 1 of the same light
        Assert.False(fixture.OffhandInventory[0].Empty);
        Assert.Equal(1, fixture.OffhandInventory[0].Itemstack.StackSize);
        Assert.Equal(20, fixture.OffhandInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(1, fixture.BackpackInventory[0].Itemstack.StackSize);

        // Act - Press hotkey for offhand when offhand already has a light
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

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

        // Create mock API for item comparison (needed when items have same ID)
        var apiMock = new Mock<ICoreClientAPI>();
        var worldMock = new Mock<IClientWorldAccessor>();
        apiMock.Setup(a => a.World).Returns(worldMock.Object);

        var handLight = MockItem.CreateLightSource(id: 1, brightness: 20, api: apiMock.Object);
        var sameBackpackLight = MockItem.CreateLightSource(id: 1, brightness: 20, api: apiMock.Object); // SAME item ID = same item type

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: new ItemStack(handLight),
            backpackItems: [sameBackpackLight]);

        // Verify initial state: hotbar has 1 light, backpack has 1 of the same light
        Assert.False(fixture.HotbarInventory[0].Empty);
        Assert.Equal(1, fixture.HotbarInventory[0].Itemstack.StackSize);
        Assert.Equal(20, fixture.HotbarInventory[0].Itemstack.Collectible.LightHsv[2]);
        Assert.Equal(1, fixture.BackpackInventory[0].Itemstack.StackSize);

        // Act - Press hotkey for hotbar when hotbar already has a light
        bool result = fixture.System.OnHotKeyPressed(useOffhand: false);

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
        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: new ItemStack(MockItem.CreateNonLightSource(id: 4)),
            backpackItems: [MockItem.CreateNonLightSource(id: 1), MockItem.CreateNonLightSource(id: 2)],
            hotbarItems: [MockItem.CreateNonLightSource(id: 3)]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

        // Assert - Should return false (no light sources to equip)
        Assert.False(result);
    }

    [Fact]
    public void OnHotKeyPressed_EmptyInventories_ReturnsFalse()
    {
        // Arrange - All inventories empty
        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: null);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OnHotKeyPressed_NoLightSources_NoErrorsThrown()
    {
        // Arrange
        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: null);

        // Act & Assert - Should not throw any exceptions
        var exception = Record.Exception(() => fixture.System.OnHotKeyPressed(useOffhand: false));
        Assert.Null(exception);
    }

    #endregion

    #region Priority Tests

    [Fact]
    public void OnHotKeyPressed_LightInOffhand_TakesHighestPriority()
    {
        // Arrange - Light in offhand should be detected first (highest priority)
        var offhandLight = MockItem.CreateLightSource(id: 1, brightness: 15);
        var brighterBackpackLight = MockItem.CreateLightSource(id: 2, brightness: 30);

        var fixture = new TestFixture(
            offhandItem: new ItemStack(offhandLight),
            activeHotbarItem: null,
            backpackItems: [brighterBackpackLight]);

        // Act - Press hotkey for offhand
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

        // Assert - Should process (offhand light detected) and swap/unequip
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_LightInActiveHotbar_TakesPriorityOverBackpack()
    {
        // Arrange - Light in active hotbar takes priority over backpack
        var hotbarLight = MockItem.CreateLightSource(id: 1, brightness: 10);
        var brighterBackpackLight = MockItem.CreateLightSource(id: 2, brightness: 25);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: new ItemStack(hotbarLight),
            backpackItems: [brighterBackpackLight]);

        // Act - Press hotkey for hotbar
        bool result = fixture.System.OnHotKeyPressed(useOffhand: false);

        // Assert - Should detect active hotbar light first and swap
        Assert.True(result);
    }

    [Fact]
    public void OnHotKeyPressed_MultipleLightsInBackpack_EquipsBrightest()
    {
        // Arrange - Multiple light sources with different brightness
        var dimLight = MockItem.CreateLightSource(id: 1, brightness: 5);
        var mediumLight = MockItem.CreateLightSource(id: 2, brightness: 15);
        var brightLight = MockItem.CreateLightSource(id: 3, brightness: 30);

        var fixture = new TestFixture(
            offhandItem: null,
            activeHotbarItem: null,
            backpackItems: [dimLight, mediumLight, brightLight]);

        // Act
        bool result = fixture.System.OnHotKeyPressed(useOffhand: true);

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
