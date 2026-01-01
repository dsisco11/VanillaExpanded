using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock wrapper for IPlayer that encapsulates Moq setup for unit testing.
/// </summary>
public class MockPlayer
{
    public Mock<IPlayer> Mock { get; }
    public IPlayer Object => Mock.Object;

    public Mock<IPlayerInventoryManager> InventoryManagerMock { get; }

    private MockInventory? _backpackInventory;
    private MockInventory? _hotbarInventory;
    private ItemSlot? _activeHotbarSlot;

    public MockPlayer()
    {
        Mock = new Mock<IPlayer>();
        InventoryManagerMock = new Mock<IPlayerInventoryManager>();

        SetupInventoryManager();
    }

    /// <summary>
    /// Gets the backpack inventory for this player.
    /// </summary>
    public MockInventory BackpackInventory
    {
        get
        {
            _backpackInventory ??= new MockInventory();
            return _backpackInventory;
        }
    }

    /// <summary>
    /// Gets the hotbar inventory for this player.
    /// </summary>
    public MockInventory HotbarInventory
    {
        get
        {
            _hotbarInventory ??= new MockInventory(10);
            return _hotbarInventory;
        }
    }

    /// <summary>
    /// Sets the backpack inventory for this player.
    /// </summary>
    public MockPlayer WithBackpack(MockInventory backpack)
    {
        _backpackInventory = backpack;
        UpdateInventorySetup();
        return this;
    }

    /// <summary>
    /// Sets the hotbar inventory for this player.
    /// </summary>
    public MockPlayer WithHotbar(MockInventory hotbar)
    {
        _hotbarInventory = hotbar;
        UpdateInventorySetup();
        return this;
    }

    /// <summary>
    /// Sets the active hotbar slot.
    /// </summary>
    public MockPlayer WithActiveHotbarSlot(ItemSlot? slot)
    {
        _activeHotbarSlot = slot;
        InventoryManagerMock.Setup(i => i.ActiveHotbarSlot).Returns(slot!);
        return this;
    }

    /// <summary>
    /// Sets the active hotbar slot to contain a specific item.
    /// </summary>
    public MockPlayer WithActiveHotbarItem(MockItem? item)
    {
        if (item is null)
        {
            _activeHotbarSlot = new DummySlot();
        }
        else
        {
            _activeHotbarSlot = new DummySlot(new ItemStack(item));
        }
        InventoryManagerMock.Setup(i => i.ActiveHotbarSlot).Returns(_activeHotbarSlot);
        return this;
    }

    /// <summary>
    /// Sets the player name.
    /// </summary>
    public MockPlayer WithName(string name)
    {
        Mock.Setup(p => p.PlayerName).Returns(name);
        return this;
    }

    /// <summary>
    /// Sets the player UID.
    /// </summary>
    public MockPlayer WithUid(string uid)
    {
        Mock.Setup(p => p.PlayerUID).Returns(uid);
        return this;
    }

    private void SetupInventoryManager()
    {
        Mock.Setup(p => p.InventoryManager).Returns(InventoryManagerMock.Object);
        UpdateInventorySetup();
    }

    private void UpdateInventorySetup()
    {
        InventoryManagerMock
            .Setup(i => i.GetOwnInventory(GlobalConstants.backpackInvClassName))
            .Returns(() => _backpackInventory?.Object);

        InventoryManagerMock
            .Setup(i => i.GetOwnInventory(GlobalConstants.hotBarInvClassName))
            .Returns(() => _hotbarInventory?.Object);
    }
}
