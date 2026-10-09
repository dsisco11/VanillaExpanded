using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.RadialProgress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Drives actual block gestures against real source/target inventories and client request wiring.</summary>
internal sealed class BlockGestureCase : IDisposable
{
    private readonly AutoStashTestScope scope = new();
    private readonly AutoStashSystem_Client client = new();
    public VsTestFixture Fixture { get; } = VsTestFixture.Client();
    public Mock<IClientPlayer> Player { get; }
    public Mock<IClientNetworkChannel> Channel { get; } = new();
    public List<Network.Packet_RequestAutoStash> Requests { get; } = [];
    public GestureBlockBehavior Behavior { get; }
    public Mock<IRadialProgressBar> Progress { get; } = new();
    public Mock<IProgressSystemProvider> ProgressProvider { get; } = new();
    public BlockSelection Selection { get; } = new() { Position = new BlockPos(12, 34, 56) };
    public MockItem Item { get; }
    private readonly InventoryGeneric targetInventory;
    private InventorySnapshot? beforeGesture;
    private int[] dirtyBeforeGesture = [];
    private readonly Mock<BlockEntityContainer>? containerTarget;
    private readonly Mock<BlockEntityCrate>? crateTarget;
    private readonly Mock<TestableBlockEntityBloomery>? bloomeryTarget;

    #region Public API
    #region Setup and entry points
    /// <summary>Builds eligible target contents and binds request/sound/animation observations without rendering.</summary>
    public BlockGestureCase(string kind)
    {
        VanillaExpandedModSystem.Config.EnableAutoStash = true;
        Player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        Player.SetupGet(player => player.InventoryManager).Returns(Fixture.Player);
        Player.SetupGet(player => player.Entity).Returns(Fixture.EntityMock.Object);
        Fixture.EntityMock.Object.World = Fixture.World;
        Fixture.EntityMock.Object.Api = Fixture.Api;
        Fixture.ClientWorldMock!.SetupGet(world => world.Player).Returns(Player.Object);
        Fixture.WorldMock.SetupGet(world => world.Api).Returns(Fixture.Api);
        Fixture.ClientNetworkMock!.Setup(network => network.GetChannel(Constants.ModId)).Returns(Channel.Object);
        Channel.Setup(channel => channel.SendPacket(It.IsAny<Network.Packet_RequestAutoStash>()))
            .Callback<Network.Packet_RequestAutoStash>(packet => Requests.Add(packet));
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.GetModSystem<AutoStashSystem_Client>(true)).Returns(client);
        Fixture.ApiMock.SetupGet(api => api.ModLoader).Returns(loader.Object);
        client.StartClientSide(Fixture.ClientApi);

        BlockEntity target;
        Block block;
        if (kind == "bloomery")
        {
            Item = MockItem.CreateBloomeryOre(1, 2, Fixture.Api);
            Item.Code = new AssetLocation("game:gesture-ore");
            bloomeryTarget = new Mock<TestableBlockEntityBloomery>(Selection.Position, Fixture.Api) { CallBase = true };
            bloomeryTarget.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
            var bloomery = bloomeryTarget.Object;
            bloomery.TestInventory[1].Itemstack = new ItemStack(Item, 1);
            targetInventory = bloomery.TestInventory;
            target = bloomery;
            block = new BlockBloomery();
        }
        else
        {
            Item = new MockItem(1, api: Fixture.Api) { Code = new AssetLocation("game:gesture-item"), MaxStackSize = 64 };
            var inventory = new InventoryGeneric(1, "gesture", "target", null!);
            inventory.Api = Fixture.Api;
            inventory[0].Itemstack = new ItemStack(Item, 1);
            targetInventory = inventory;
            if (kind == "crate")
            {
                crateTarget = new Mock<BlockEntityCrate> { CallBase = true };
                crateTarget.SetupGet(value => value.Inventory).Returns(inventory);
                crateTarget.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
                target = crateTarget.Object;
            }
            else
            {
                containerTarget = new Mock<BlockEntityContainer> { CallBase = true };
                containerTarget.SetupGet(value => value.Inventory).Returns(inventory);
                containerTarget.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
                target = containerTarget.Object;
            }
            block = kind == "crate" ? new BlockCrate() : new BlockGenericTypedContainer();
        }
        Fixture.BackpackInventory[0].Itemstack = new ItemStack(Item, 3);
        var accessor = new Mock<IBlockAccessor>();
        accessor.Setup(value => value.GetBlockEntity(Selection.Position)).Returns(target);
        Fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(accessor.Object);
        Progress.SetupAllProperties();
        ProgressProvider.Setup(provider => provider.CreateProgressBar()).Returns(Progress.Object);
        Behavior = new GestureBlockBehavior(block, ProgressProvider.Object);
        Behavior.OnLoaded(Fixture.Api);
        Player.Object.Entity.Controls.CtrlKey = true;
        Player.Object.Entity.Controls.ShiftKey = true;
    }

    /// <summary>Starts through the production interaction entry point and captures its handling result.</summary>
    public (bool Result, EnumHandling Handling) Start()
    {
        beforeGesture = new InventorySnapshot(Fixture.BackpackInventory, Fixture.HotbarInventory, targetInventory);
        dirtyBeforeGesture = targetInventory.DirtySlots.Order().ToArray();
        EnumHandling handling = EnumHandling.Handled;
        bool result = Behavior.OnBlockInteractStart(Fixture.World, Player.Object, Selection, ref handling);
        return (result, handling);
    }

    /// <summary>Steps an already-started production gesture at an exact elapsed time.</summary>
    public (bool Result, EnumHandling Handling) Step(float seconds)
    {
        EnumHandling handling = EnumHandling.PassThrough;
        bool result = Behavior.OnBlockInteractStep(seconds, Fixture.World, Player.Object, Selection, ref handling);
        return (result, handling);
    }

    /// <summary>Cancels or stops through the real entry point and returns its handling result.</summary>
    public EnumHandling End(bool cancel)
    {
        EnumHandling handling = EnumHandling.PassThrough;
        if (cancel) Behavior.OnBlockInteractCancel(.2f, Fixture.World, Player.Object, Selection, ref handling);
        else Behavior.OnBlockInteractStop(.2f, Fixture.World, Player.Object, Selection, ref handling);
        return handling;
    }
    #endregion
    #region Observations and cleanup
    /// <summary>Verifies exact request target plus sound and first-person animation counts.</summary>
    public void AssertSubmission(int count)
    {
        Assert.Equal(count, Requests.Count);
        Assert.All(Requests, packet => Assert.Equal(Selection.Position, packet.position));
        Fixture.WorldMock.Verify(world => world.PlaySoundAt(It.Is<AssetLocation>(path => path.ToString() == "game:sounds/player/poultice-applied"),
            Player.Object.Entity, null!, false, 16, 1), Times.Exactly(count));
        Player.Verify(player => player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract), Times.Exactly(count));
        Player.Verify(player => player.TriggerFpAnimation(It.IsAny<EnumHandInteract>()), Times.Exactly(count));
        beforeGesture!.AssertUnchangedExcept();
        beforeGesture.AssertConserved();
        Assert.Equal(dirtyBeforeGesture, targetInventory.DirtySlots.Order());
        containerTarget?.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        crateTarget?.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        bloomeryTarget?.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
        Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
    }

    /// <summary>Disposes request listeners and restores mutable feature settings after each scenario.</summary>
    public void Dispose()
    {
        try
        {
            End(false);
            client.Dispose();
        }
        finally { scope.Dispose(); }
    }
    #endregion
    #endregion
}
