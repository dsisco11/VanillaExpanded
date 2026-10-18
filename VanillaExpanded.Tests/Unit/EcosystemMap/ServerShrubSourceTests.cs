using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.EcosystemMap;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks the actual normal-send hook, early obligations and owner-thread disconnect cleanup against installed server types.</summary>
[Collection("Ecosystem receipt adapters")]
public sealed class ServerShrubSourceTests
{
    #region Public API
    /// <summary>The normal vanilla send marker survives observation; delivery waits for the layer and reads only loaded observed regions.</summary>
    [Fact]
    public void OrdinarySendHookWaitsForLayerAndForgetsDisconnectedGenerations()
    {
        var server = new ServerDeliveryFixture();
        var connection = new ConnectedClient(7)
        {
            MapRegionSent = new(), State = EnumClientState.Playing,
            Player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer))
        };
        server.World.Clients[connection.Id] = connection;
        server.Maps.Setup(value => value.GetMapRegion(-3, 7)).Returns((IMapRegion)null!);
        using (var source = new ServerShrubSource(server.Api.Object))
        {
            long index = ((long)7 << 32) - 3;
            connection.SetMapRegionSent(index);
            Assert.Contains(index, connection.MapRegionSent);
            server.Tick(0);
            server.Maps.Verify(value => value.GetMapRegion(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            source.Bind(new EcosystemMapLayer(server.Api.Object, new WorldMapManager()));
            server.Tick(0);
            server.Maps.Verify(value => value.GetMapRegion(-3, 7), Times.Once);
            connection.SetMapRegionSent(index);
            server.Events.Raise(value => value.PlayerDisconnect += null, connection.Player);
            server.Reset(connection.Player, new EcosystemCacheReset());
            server.Tick(0); // Engine removal can follow its disconnect event; reset must not revive this generation.
            server.Maps.Verify(value => value.GetMapRegion(-3, 7), Times.Once);
            server.Shutdown();
        }
        server.Events.Verify(value => value.UnregisterGameTickListener(7), Times.Once);
        connection.SetMapRegionSent(0);
        Assert.Contains(0, connection.MapRegionSent);
    }

    /// <summary>Authenticated reset reseeding still reads only currently loaded already-sent regions and rejects unowned player identity.</summary>
    [Fact]
    public void ResetCannotRetrieveAnUnloadedOrUnsentRegion()
    {
        var server = new ServerDeliveryFixture();
        var player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
        var connection = new ConnectedClient(7) { MapRegionSent = new(), State = EnumClientState.Playing, Player = player };
        server.World.Clients[7] = connection;
        server.Maps.Setup(value => value.GetMapRegion(2, 3)).Returns((IMapRegion)null!);
        using var source = new ServerShrubSource(server.Api.Object);
        source.Bind(new EcosystemMapLayer(server.Api.Object, new WorldMapManager()));
        connection.SetMapRegionSent(((long)3 << 32) + 2);
        server.Tick(0);
        server.Reset((ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer)), new EcosystemCacheReset());
        server.Tick(0);
        server.Maps.Verify(value => value.GetMapRegion(2, 3), Times.Once);
        server.Reset(player, new EcosystemCacheReset { Version = 99 });
        server.Tick(0);
        server.Maps.Verify(value => value.GetMapRegion(2, 3), Times.Once);
        server.Reset(player, new EcosystemCacheReset());
        server.Tick(0);
        server.Maps.Verify(value => value.GetMapRegion(2, 3), Times.Exactly(2));
        server.Maps.VerifyNoOtherCalls();
    }
    #endregion
}
