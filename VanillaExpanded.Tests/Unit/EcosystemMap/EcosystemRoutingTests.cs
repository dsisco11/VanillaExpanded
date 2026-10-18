using System.Collections.Concurrent;
using HarmonyLib;
using VanillaExpanded.EcosystemMap;
using Moq;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Registration;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises the installed world-map envelope and actual ecosystem client session without opening a GUI or real world cache.</summary>
[Collection("Ecosystem receipt adapters")]
public sealed class EcosystemRoutingTests
{
    #region Public API
    /// <summary>Server routing reaches OnDataFromServer while inactive; purge clears persistent/derived state and later ordinary input repopulates it.</summary>
    [Fact]
    public async Task VanillaEnvelopeRoutesOriginalShrubsAndPurgeToTheClientSession()
    {
        using var fixture = new StorageFixture();
        var client = new ClientReceiptFixture();
        var errors = new ConcurrentQueue<Exception>();
        using var receipt = new ClientReceiptAdapter(client.Game, errors.Enqueue);
        var clientApi = new Mock<ICoreClientAPI>();
        var commands = new ChatCommandApi(Mock.Of<ICoreAPI>());
        var purge = commands.Create("map").BeginSubCommand("purgedb");
        var originalResult = TextCommandResult.Success();
        purge.HandleWith(_ => originalResult);
        clientApi.SetupGet(value => value.ChatCommands).Returns(commands);
        clientApi.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        using var session = new EcosystemClientSession(clientApi.Object, receipt, fixture.Open(), 32);
        var owner = new EcosystemMapSystem();
        AccessTools.Property(typeof(EcosystemMapSystem), "Client").SetValue(owner, session);
        var manager = new WorldMapManager();
        manager.RegisterMapLayer<EcosystemMapLayer>(EcosystemMapSystem.LayerCode, 1.1);
        var layer = new EcosystemMapLayer(clientApi.Object, manager);
        AccessTools.Field(typeof(EcosystemMapLayer), "owner").SetValue(layer, owner);
        manager.MapLayers.Add(layer);
        Assert.False(layer.Active);

        var serverApi = new Mock<ICoreAPI>();
        serverApi.SetupGet(value => value.Side).Returns(EnumAppSide.Server);
        var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IServerPlayer>();
        player.SetupGet(value => value.ConnectionState).Returns(EnumClientState.Playing);
        MapLayerUpdate? envelope = null;
        var channel = new Mock<IServerNetworkChannel>();
        channel.Setup(value => value.SendPacket(It.IsAny<MapLayerUpdate>(), It.IsAny<IServerPlayer[]>()))
            .Callback<MapLayerUpdate, IServerPlayer[]>((message, players) =>
            { Assert.Same(player.Object, Assert.Single(players)); envelope = message; });
        AccessTools.Field(typeof(WorldMapManager), "api").SetValue(manager, serverApi.Object);
        AccessTools.Field(typeof(WorldMapManager), "serverChannel").SetValue(manager, channel.Object);
        var payload = ShrubPayload.Encode(new(3, 4), StorageFixture.Map());
        manager.SendMapDataToClient(layer, player.Object, payload);
        Assert.Equal(EcosystemMapSystem.LayerCode, Assert.Single(envelope!.Maplayers).ForMapLayer);
        AccessTools.Method(typeof(WorldMapManager), "OnMapLayerDataReceivedClient").Invoke(manager, new object[] { envelope });
        var old = session.Storage.CurrentEpoch;
        int cleared = 0;
        session.Purged += () => cleared++;
        var handler = (OnCommandDelegate)AccessTools.Field(typeof(ChatCommandImpl), "handler").GetValue(purge)!;
        Assert.Same(originalResult, handler(new TextCommandCallingArgs()));
        Assert.True(old.DisposableWork.IsCancellationRequested);
        Assert.Equal(1, cleared);
        session.Receive(ShrubPayload.Encode(new(8, 9), StorageFixture.Map(1)));
        session.Dispose();
        Assert.Empty(errors);
        await using var reopened = fixture.Open();
        Assert.Null((await reopened.ReadRegionAsync(new(3, 4))).Value);
        Assert.Equal(StorageFixture.Map(1).Values, (await reopened.ReadRegionAsync(new(8, 9))).Value!.Shrubs!.Values);
    }


    /// <summary>Unchanged shrubs recover after same-connection client purge through authenticated reset and ordinary native delivery.</summary>
    [Fact]
    public async Task SameConnectionPurgeCanReceiveUnchangedShrubsAgain()
    {
        using var fixture = new StorageFixture();
        var client = new ClientReceiptFixture();
        var errors = new ConcurrentQueue<Exception>();
        using var receipt = new ClientReceiptAdapter(client.Game, errors.Enqueue);
        var commands = new ChatCommandApi(Mock.Of<ICoreAPI>());
        var purge = commands.Create("map").BeginSubCommand("purgedb").HandleWith(_ => TextCommandResult.Success());
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.ChatCommands).Returns(commands);
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        using var session = new EcosystemClientSession(api.Object, receipt, fixture.Open(), 32);
        var owner = new EcosystemMapSystem();
        AccessTools.Property(typeof(EcosystemMapSystem), "Client").SetValue(owner, session);
        var map = new WorldMapManager();
        map.RegisterMapLayer<EcosystemMapLayer>(EcosystemMapSystem.LayerCode, 1.1);
        var clientLayer = new EcosystemMapLayer(api.Object, map);
        AccessTools.Field(typeof(EcosystemMapLayer), "owner").SetValue(clientLayer, owner);
        map.MapLayers.Add(clientLayer);

        var server = new ServerDeliveryFixture();
        var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IServerPlayer>();
        player.SetupGet(value => value.ConnectionState).Returns(EnumClientState.Playing);
        var connection = new ConnectedClient(7) { State = EnumClientState.Playing, Player = (ServerPlayer)player.Object, MapRegionSent = new() };
        server.World.Clients[7] = connection;
        var region = new Mock<IMapRegion>(MockBehavior.Strict);
        region.SetupGet(value => value.ShrubMap).Returns(new Vintagestory.API.Datastructures.IntDataMap2D
            { Size = 3, TopLeftPadding = 1, Data = StorageFixture.Map().Values.ToArray() });
        server.Maps.Setup(value => value.GetMapRegion(3, 4)).Returns(region.Object);
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.GetModSystem<WorldMapManager>(true)).Returns(map);
        server.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        AccessTools.Field(typeof(WorldMapManager), "api").SetValue(map, server.Api.Object);
        var dataChannel = new Mock<IServerNetworkChannel>();
        int sent = 0;
        dataChannel.Setup(value => value.SendPacket(It.IsAny<MapLayerUpdate>(), It.IsAny<IServerPlayer[]>()))
            .Callback<MapLayerUpdate, IServerPlayer[]>((message, _) =>
            {
                sent++;
                AccessTools.Method(typeof(WorldMapManager), "OnMapLayerDataReceivedClient").Invoke(map, new object[] { message });
            });
        AccessTools.Field(typeof(WorldMapManager), "serverChannel").SetValue(map, dataChannel.Object);
        using var source = new ServerShrubSource(server.Api.Object);
        source.Bind(new EcosystemMapLayer(server.Api.Object, map));
        var control = new Mock<IClientNetworkChannel>();
        control.SetupGet(value => value.Connected).Returns(true);
        control.Setup(value => value.SendPacket(It.IsAny<EcosystemCacheReset>()))
            .Callback<EcosystemCacheReset>(message => server.Reset(player.Object, message));
        AccessTools.Field(typeof(EcosystemMapSystem), "cacheResetChannel").SetValue(owner, control.Object);
        session.Purged += (Action)AccessTools.Method(typeof(EcosystemMapSystem), "NotifyServerCacheReset").CreateDelegate(typeof(Action), owner);

        long index = ((long)4 << 32) + 3;
        connection.SetMapRegionSent(index);
        server.Tick(0);
        connection.SetMapRegionSent(index);
        server.Tick(0);
        Assert.Equal(1, sent); // Unchanged data is suppressed until the client clears its cache.
        var handler = (OnCommandDelegate)AccessTools.Field(typeof(ChatCommandImpl), "handler").GetValue(purge)!;
        handler(new TextCommandCallingArgs());
        control.Verify(value => value.SendPacket(It.IsAny<EcosystemCacheReset>()), Times.Once);
        connection.SetMapRegionSent(index);
        server.Tick(0);
        Assert.Equal(2, sent);
        session.Dispose();
        Assert.Empty(errors);
        await using var reopened = fixture.Open();
        Assert.Equal(StorageFixture.Map().Values, (await reopened.ReadRegionAsync(new(3, 4))).Value!.Shrubs!.Values);
    }

    /// <summary>Only a successful purge on a supporting server emits a reset notification.</summary>
    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public void ResetNotificationRequiresServerSupportAndSuccessfulPurge(bool supported, bool successful, int expected)
    {
        using var fixture = new StorageFixture();
        var client = new ClientReceiptFixture();
        using var receipt = new ClientReceiptAdapter(client.Game, error => throw error);
        var commands = new ChatCommandApi(Mock.Of<ICoreAPI>());
        var purge = commands.Create("map").BeginSubCommand("purgedb")
            .HandleWith(_ => successful ? TextCommandResult.Success() : TextCommandResult.Error("failed"));
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.ChatCommands).Returns(commands);
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        using var session = new EcosystemClientSession(api.Object, receipt, fixture.Open(), 32);
        var owner = new EcosystemMapSystem();
        var channel = new Mock<IClientNetworkChannel>();
        channel.SetupGet(value => value.Connected).Returns(supported);
        AccessTools.Field(typeof(EcosystemMapSystem), "cacheResetChannel").SetValue(owner, channel.Object);
        session.Purged += (Action)AccessTools.Method(typeof(EcosystemMapSystem), "NotifyServerCacheReset").CreateDelegate(typeof(Action), owner);
        var handler = (OnCommandDelegate)AccessTools.Field(typeof(ChatCommandImpl), "handler").GetValue(purge)!;
        handler(new TextCommandCallingArgs());
        channel.Verify(value => value.SendPacket(It.IsAny<EcosystemCacheReset>()), Times.Exactly(expected));
    }

    /// <summary>Neither map opening nor old-image view changes sends a request for missing environmental inputs.</summary>
    [Fact]
    public void OldMapPanningAndMinimapCallbacksDoNotRequestBackfill()
    {
        var api = new Mock<ICoreAPI>(MockBehavior.Strict);
        var sink = new Mock<IWorldMapManager>(MockBehavior.Strict);
        var layer = new EcosystemMapLayer(api.Object, sink.Object);
        layer.OnMapOpenedClient();
        layer.OnViewChangedClient(new(), new());
        layer.OnMapClosedClient();
        layer.OnDataFromClient(new byte[] { 1, 2, 3 });
        api.VerifyNoOtherCalls();
        sink.VerifyNoOtherCalls();
    }
    #endregion
}
