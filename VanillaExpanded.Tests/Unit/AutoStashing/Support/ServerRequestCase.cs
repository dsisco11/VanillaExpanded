using Moq;
using VanillaExpanded.Network;
using VanillaExpanded.src.ModSystems;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Delivers packets through the registered server handlers with real engine player reachability.</summary>
internal sealed class ServerRequestCase : IDisposable
{
    public VsTestFixture Fixture { get; }
    public Mock<IServerPlayer> Player { get; }
    public EntityPlayer PlayerEntity { get; }
    public Mock<IBlockAccessor> Accessor { get; } = new();
    public Mock<ILandClaimAPI> Claims { get; } = new();
    public BlockPos Position { get; } = new(0, 0, 0);
    public MockServerNetworkChannel Channel { get; } = new();
    private readonly AutoStashSystem_Server system = new();

    #region Public API
    /// <summary>Registers the actual handlers and binds only API, inventory, claims, and entity observations.</summary>
    public ServerRequestCase(VsTestFixture fixture)
    {
        Fixture = fixture;
        var context = new ServerPermissionContext(Accessor.Object);
        Player = new Mock<ServerPlayer>(context.Server, new ServerWorldPlayerData()).As<IServerPlayer>();
        PlayerEntity = new EntityPlayer { Api = fixture.Api, World = fixture.World };
        PlayerEntity.Pos.SetPos(.5, .5, .5);
        Player.SetupGet(value => value.Entity).Returns(PlayerEntity);
        Player.SetupGet(value => value.InventoryManager).Returns(fixture.Player);
        Player.SetupGet(value => value.PlayerName).Returns("request-owner");
        Player.SetupGet(value => value.PlayerUID).Returns("request-owner-id");
        var data = new Mock<IWorldPlayerData>();
        data.SetupGet(value => value.PickingRange).Returns(5);
        Player.SetupGet(value => value.WorldData).Returns(data.Object);
        fixture.WorldMock.SetupGet(value => value.BlockAccessor).Returns(Accessor.Object);
        fixture.WorldMock.SetupGet(value => value.Claims).Returns(Claims.Object);
        Claims.Setup(value => value.TryAccess(Player.Object, Position, EnumBlockAccessFlags.Use)).Returns(true);
        var network = new Mock<IServerNetworkAPI>();
        network.Setup(value => value.GetChannel(Constants.ModId)).Returns(Channel);
        fixture.ServerApiMock!.SetupGet(value => value.Network).Returns(network.Object);
        system.StartServerSide(fixture.ServerApi);
    }

    /// <summary>Submits a block packet at the otherwise reachable fixture position.</summary>
    public void BlockRequest() => Channel.SimulateReceivePacket(Player.Object, new Packet_RequestAutoStash { position = Position.Copy() });

    /// <summary>Submits an entity packet with a specific attachment index.</summary>
    public void EntityRequest(long id, int index) => Channel.SimulateReceivePacket(Player.Object,
        new Packet_RequestEntityAutoStash { EntityId = id, AttachmentSlotIndex = index });

    /// <summary>Checks that execution never delegates mutations to the mock inventory manager or sends response packets.</summary>
    public void AssertNoSyntheticTransfer()
    {
        Fixture.InventoryManagerMock.Verify(value => value.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
        Assert.Empty(Channel.SentPackets);
        Assert.Empty(Channel.BroadcastPackets);
    }

    /// <summary>Disposes the registered system without running server infrastructure.</summary>
    public void Dispose() => system.Dispose();
    #endregion
}
