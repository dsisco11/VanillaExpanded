using Moq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// Unified test fixture providing a fully-wired mock environment for unit tests.
/// Supports both client-side and server-side API modes.
/// </summary>
public class VsTestFixture
{
    #region Core API Mocks

    /// <summary>
    /// The side this fixture is configured for (Client or Server).
    /// </summary>
    public EnumAppSide Side { get; }

    /// <summary>
    /// Generic API mock (available in both client and server modes).
    /// </summary>
    public Mock<ICoreAPI> ApiMock { get; }

    /// <summary>
    /// Client API mock (only available in client mode, null in server mode).
    /// </summary>
    public Mock<ICoreClientAPI>? ClientApiMock { get; }

    /// <summary>
    /// Server API mock (only available in server mode, null in client mode).
    /// </summary>
    public Mock<ICoreServerAPI>? ServerApiMock { get; }

    /// <summary>
    /// World accessor mock.
    /// </summary>
    public Mock<IWorldAccessor> WorldMock { get; }

    /// <summary>
    /// Client world accessor mock (only in client mode).
    /// </summary>
    public Mock<IClientWorldAccessor>? ClientWorldMock { get; }

    /// <summary>
    /// Server world accessor mock (only in server mode).
    /// </summary>
    public Mock<IServerWorldAccessor>? ServerWorldMock { get; }

    /// <summary>
    /// Logger mock.
    /// </summary>
    public Mock<ILogger> LoggerMock { get; }

    /// <summary>
    /// Network API mock (client mode).
    /// </summary>
    public Mock<IClientNetworkAPI>? ClientNetworkMock { get; }

    #endregion

    #region Inventory Infrastructure

    /// <summary>
    /// Mock for inventory network utility operations.
    /// </summary>
    public Mock<IInventoryNetworkUtil> InvNetworkUtilMock { get; }

    /// <summary>
    /// Player's backpack inventory (10 slots by default).
    /// </summary>
    public InventoryGeneric BackpackInventory { get; }

    /// <summary>
    /// Player's hotbar inventory (10 slots by default).
    /// </summary>
    public InventoryGeneric HotbarInventory { get; }

    /// <summary>
    /// Player's offhand/character inventory (1 slot).
    /// </summary>
    public InventoryGeneric OffhandInventory { get; }

    #endregion

    #region Player Infrastructure

    /// <summary>
    /// Player mock (IPlayer for server, IClientPlayer for client).
    /// </summary>
    public Mock<IPlayer> PlayerMock { get; }

    /// <summary>
    /// Client player mock (only in client mode).
    /// </summary>
    public Mock<IClientPlayer>? ClientPlayerMock { get; }

    /// <summary>
    /// Player's inventory manager mock.
    /// </summary>
    public Mock<IPlayerInventoryManager> InventoryManagerMock { get; }

    /// <summary>
    /// Player entity mock.
    /// </summary>
    public Mock<EntityPlayer> EntityMock { get; }

    #endregion

    #region Convenience Accessors

    /// <summary>
    /// Gets the ICoreAPI object.
    /// </summary>
    public ICoreAPI Api => ApiMock.Object;

    /// <summary>
    /// Gets the ICoreClientAPI object (throws if not in client mode).
    /// </summary>
    public ICoreClientAPI ClientApi => ClientApiMock?.Object
        ?? throw new InvalidOperationException("ClientApi is only available in Client mode. Use VsTestFixture.Client().");

    /// <summary>
    /// Gets the ICoreServerAPI object (throws if not in server mode).
    /// </summary>
    public ICoreServerAPI ServerApi => ServerApiMock?.Object
        ?? throw new InvalidOperationException("ServerApi is only available in Server mode. Use VsTestFixture.Server().");

    /// <summary>
    /// Gets the IWorldAccessor object.
    /// </summary>
    public IWorldAccessor World => WorldMock.Object;

    /// <summary>
    /// Gets the IPlayer object.
    /// </summary>
    public IPlayer Player => PlayerMock.Object;

    /// <summary>
    /// Gets the IClientPlayer object (throws if not in client mode).
    /// </summary>
    public IClientPlayer ClientPlayer => ClientPlayerMock?.Object
        ?? throw new InvalidOperationException("ClientPlayer is only available in Client mode. Use VsTestFixture.Client().");

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a client-side test fixture.
    /// </summary>
    public static VsTestFixture Client() => new(EnumAppSide.Client);

    /// <summary>
    /// Creates a server-side test fixture.
    /// </summary>
    public static VsTestFixture Server() => new(EnumAppSide.Server);

    #endregion

    #region Constructor

    private VsTestFixture(EnumAppSide side)
    {
        Side = side;
        LoggerMock = new Mock<ILogger>();

        // Create API mocks based on side
        if (side == EnumAppSide.Client)
        {
            ClientApiMock = new Mock<ICoreClientAPI>();
            ClientWorldMock = new Mock<IClientWorldAccessor>();
            ClientNetworkMock = new Mock<IClientNetworkAPI>();
            ClientPlayerMock = new Mock<IClientPlayer>();

            // Setup as ICoreAPI too
            ApiMock = ClientApiMock.As<ICoreAPI>();
            WorldMock = ClientWorldMock.As<IWorldAccessor>();
            PlayerMock = ClientPlayerMock.As<IPlayer>();

            // Wire up client API
            ClientApiMock.Setup(a => a.World).Returns(ClientWorldMock.Object);
            ClientApiMock.Setup(a => a.Network).Returns(ClientNetworkMock.Object);
            ClientApiMock.Setup(a => a.Logger).Returns(LoggerMock.Object);
            ClientApiMock.Setup(a => a.Side).Returns(EnumAppSide.Client);

            // Wire up client world
            ClientWorldMock.Setup(w => w.Player).Returns(ClientPlayerMock.Object);
            ClientWorldMock.Setup(w => w.Side).Returns(EnumAppSide.Client);
            ClientWorldMock.Setup(w => w.Logger).Returns(LoggerMock.Object);
        }
        else
        {
            ServerApiMock = new Mock<ICoreServerAPI>();
            ServerWorldMock = new Mock<IServerWorldAccessor>();

            // Setup as ICoreAPI too
            ApiMock = ServerApiMock.As<ICoreAPI>();
            WorldMock = ServerWorldMock.As<IWorldAccessor>();
            PlayerMock = new Mock<IPlayer>();

            // Wire up server API
            ServerApiMock.Setup(a => a.World).Returns(ServerWorldMock.Object);
            ServerApiMock.Setup(a => a.Logger).Returns(LoggerMock.Object);
            ServerApiMock.Setup(a => a.Side).Returns(EnumAppSide.Server);

            // Wire up server world
            ServerWorldMock.Setup(w => w.Side).Returns(EnumAppSide.Server);
            ServerWorldMock.Setup(w => w.Logger).Returns(LoggerMock.Object);
        }

        // Common world setup
        WorldMock.Setup(w => w.Side).Returns(side);
        WorldMock.Setup(w => w.Logger).Returns(LoggerMock.Object);

        // Create inventory network util mock
        InvNetworkUtilMock = new Mock<IInventoryNetworkUtil>();
        InvNetworkUtilMock
            .Setup(u => u.GetFlipSlotsPacket(It.IsAny<InventoryBase>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new object());

        // Create inventories
        OffhandInventory = new InventoryGeneric(1, "character", "offhand-1", null!);
        HotbarInventory = new InventoryGeneric(10, GlobalConstants.hotBarInvClassName, "hotbar-1", null!);
        BackpackInventory = new InventoryGeneric(10, GlobalConstants.backpackInvClassName, "backpack-1", null!);

        // Wire up inventories with API and network util
        OffhandInventory.Api = Api;
        OffhandInventory.InvNetworkUtil = InvNetworkUtilMock.Object;
        HotbarInventory.Api = Api;
        HotbarInventory.InvNetworkUtil = InvNetworkUtilMock.Object;
        BackpackInventory.Api = Api;
        BackpackInventory.InvNetworkUtil = InvNetworkUtilMock.Object;

        // Setup player entity
        EntityMock = new Mock<EntityPlayer>();
        EntityMock.Setup(e => e.LeftHandItemSlot).Returns(OffhandInventory[0]);

        // Setup inventory manager
        InventoryManagerMock = new Mock<IPlayerInventoryManager>();
        InventoryManagerMock.Setup(i => i.ActiveHotbarSlot).Returns(HotbarInventory[0]);
        InventoryManagerMock.Setup(i => i.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(BackpackInventory);
        InventoryManagerMock.Setup(i => i.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(HotbarInventory);
        InventoryManagerMock.Setup(i => i.OpenInventory(It.IsAny<IInventory>())).Returns(new object());
        InventoryManagerMock.Setup(i => i.CloseInventoryAndSync(It.IsAny<IInventory>()));

        // Setup TryTransferTo to perform actual item transfer
        InventoryManagerMock
            .Setup(i => i.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(), ref It.Ref<ItemStackMoveOperation>.IsAny))
            .Returns((ItemSlot source, ItemSlot target, ref ItemStackMoveOperation op) =>
            {
                if (source.Empty || (target.Itemstack?.Collectible?.Code != source.Itemstack?.Collectible?.Code && !target.Empty))
                {
                    op.MovedQuantity = 0;
                    return null;
                }

                int toMove = Math.Min(op.RequestedQuantity, source.StackSize);
                if (target.Empty)
                {
                    target.Itemstack = source.TakeOut(toMove);
                    op.MovedQuantity = toMove;
                }
                else
                {
                    // Merge into existing stack
                    int canFit = target.Itemstack.Collectible.MaxStackSize - target.StackSize;
                    int actualMove = Math.Min(toMove, canFit);
                    target.Itemstack.StackSize += actualMove;
                    source.Itemstack.StackSize -= actualMove;
                    if (source.Itemstack.StackSize <= 0)
                    {
                        source.Itemstack = null;
                    }
                    op.MovedQuantity = actualMove;
                }
                return new object(); // Return dummy packet
            });

        // Wire up player
        PlayerMock.Setup(p => p.Entity).Returns(EntityMock.Object);
        PlayerMock.Setup(p => p.InventoryManager).Returns(InventoryManagerMock.Object);

        if (side == EnumAppSide.Client)
        {
            ClientPlayerMock!.Setup(p => p.Entity).Returns(EntityMock.Object);
            ClientPlayerMock.Setup(p => p.InventoryManager).Returns(InventoryManagerMock.Object);
        }
    }

    #endregion

    #region Item Factory Methods

    /// <summary>
    /// Creates a light source item with the API already configured.
    /// </summary>
    public MockItem CreateLightSource(int id, byte brightness = 20)
    {
        return MockItem.CreateLightSource(id, brightness, Api);
    }

    /// <summary>
    /// Creates a non-light source item with the API already configured.
    /// </summary>
    public MockItem CreateNonLightSource(int id)
    {
        return MockItem.CreateNonLightSource(id, Api);
    }

    #endregion

    #region Fluent Configuration

    /// <summary>
    /// Sets the offhand item.
    /// </summary>
    public VsTestFixture WithOffhandItem(ItemStack? item)
    {
        OffhandInventory[0].Itemstack = item;
        return this;
    }

    /// <summary>
    /// Sets the offhand item from a MockItem.
    /// </summary>
    public VsTestFixture WithOffhandItem(MockItem? item)
    {
        if (item is not null)
        {
            item.SetApi(Api);
            OffhandInventory[0].Itemstack = new ItemStack(item);
        }
        else
        {
            OffhandInventory[0].Itemstack = null;
        }
        return this;
    }

    /// <summary>
    /// Sets the active hotbar item (slot 0).
    /// </summary>
    public VsTestFixture WithActiveHotbarItem(ItemStack? item)
    {
        HotbarInventory[0].Itemstack = item;
        return this;
    }

    /// <summary>
    /// Sets the active hotbar item from a MockItem.
    /// </summary>
    public VsTestFixture WithActiveHotbarItem(MockItem? item)
    {
        if (item is not null)
        {
            item.SetApi(Api);
            HotbarInventory[0].Itemstack = new ItemStack(item);
        }
        else
        {
            HotbarInventory[0].Itemstack = null;
        }
        return this;
    }

    /// <summary>
    /// Populates the backpack inventory with items (auto-sets API on items).
    /// </summary>
    public VsTestFixture WithBackpackItems(params MockItem[] items)
    {
        for (int i = 0; i < items.Length && i < BackpackInventory.Count; i++)
        {
            items[i].SetApi(Api);
            BackpackInventory[i].Itemstack = new ItemStack(items[i]);
        }
        return this;
    }

    /// <summary>
    /// Populates the hotbar inventory with items starting at slot 1 (auto-sets API on items).
    /// Slot 0 is the active hotbar slot, use WithActiveHotbarItem for that.
    /// </summary>
    public VsTestFixture WithHotbarItems(params MockItem[] items)
    {
        for (int i = 0; i < items.Length && i + 1 < HotbarInventory.Count; i++)
        {
            items[i].SetApi(Api);
            HotbarInventory[i + 1].Itemstack = new ItemStack(items[i]);
        }
        return this;
    }

    /// <summary>
    /// Sets a specific backpack slot with an item and stack size.
    /// </summary>
    public VsTestFixture WithBackpackSlot(int slot, MockItem item, int stackSize = 1)
    {
        item.SetApi(Api);
        BackpackInventory[slot].Itemstack = new ItemStack(item, stackSize);
        return this;
    }

    /// <summary>
    /// Sets a specific hotbar slot with an item and stack size.
    /// </summary>
    public VsTestFixture WithHotbarSlot(int slot, MockItem item, int stackSize = 1)
    {
        item.SetApi(Api);
        HotbarInventory[slot].Itemstack = new ItemStack(item, stackSize);
        return this;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets total items in the backpack.
    /// </summary>
    public int GetBackpackTotalItems()
    {
        int total = 0;
        foreach (var slot in BackpackInventory)
        {
            if (!slot.Empty) total += slot.StackSize;
        }
        return total;
    }

    /// <summary>
    /// Gets total items in the hotbar.
    /// </summary>
    public int GetHotbarTotalItems()
    {
        int total = 0;
        foreach (var slot in HotbarInventory)
        {
            if (!slot.Empty) total += slot.StackSize;
        }
        return total;
    }

    /// <summary>
    /// Configures the fixture for use with a specific ModSystem by setting its private API field.
    /// </summary>
    public VsTestFixture ConfigureModSystem<T>(T modSystem, string apiFieldName = "api") where T : ModSystem
    {
        var apiField = typeof(T).GetField(apiFieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (apiField is null)
        {
            throw new InvalidOperationException($"Could not find field '{apiFieldName}' on type {typeof(T).Name}");
        }

        if (Side == EnumAppSide.Client && ClientApiMock is not null)
        {
            apiField.SetValue(modSystem, ClientApiMock.Object);
        }
        else if (Side == EnumAppSide.Server && ServerApiMock is not null)
        {
            apiField.SetValue(modSystem, ServerApiMock.Object);
        }
        else
        {
            apiField.SetValue(modSystem, ApiMock.Object);
        }

        return this;
    }

    #endregion
}
