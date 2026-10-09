using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaExpanded.Network;
using VanillaExpanded.SpawnDecal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.SpawnDecal;

/// <summary>Runs the installed engine's spawn-consumption method through the production Harmony boundary.</summary>
[Collection("SpawnDecalConfig")]
public sealed class SpawnDecalConsumptionTests
{
    #region Public API
    /// <summary>The last personal use still returns the respawn position while notifying its client that it expired.</summary>
    [Fact]
    public void LastPersonalUseSendsClearWithoutChangingRespawnResult()
    {
        using var fixture = new Fixture(1);
        var result = fixture.Player.GetSpawnPosition(true);
        Assert.Equal(0, result.UsesLeft);
        Assert.Equal(10.5, result.X);
        Assert.Equal(20, result.Y);
        Assert.Equal(30.5, result.Z);
        Assert.Null(fixture.Data.SpawnPosition);
        var sent = Assert.Single(fixture.Packets);
        Assert.False(sent.Packet.HasSpawn);
        Assert.Same(fixture.Player, Assert.Single(sent.Players));
    }

    /// <summary>Remaining and unlimited personal uses keep the decal without emitting a clear.</summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(-1, -1)]
    public void AvailablePersonalUsesDoNotSendClear(int uses, int expected)
    {
        using var fixture = new Fixture(uses);
        var result = fixture.Player.GetSpawnPosition(true);
        Assert.Equal(expected, result.UsesLeft);
        Assert.NotNull(fixture.Data.SpawnPosition);
        Assert.Empty(fixture.Packets);
    }

    /// <summary>A non-consuming lookup keeps the last personal use and emits no update.</summary>
    [Fact]
    public void NonConsumingLookupDoesNotClear()
    {
        using var fixture = new Fixture(1);
        Assert.Equal(1, fixture.Player.GetSpawnPosition(false).UsesLeft);
        Assert.NotNull(fixture.Data.SpawnPosition);
        Assert.Empty(fixture.Packets);
    }

    /// <summary>Exhausting a forced role spawn does not remove the still-owned personal spawn indicator.</summary>
    [Fact]
    public void ForcedRoleExhaustionDoesNotClearPersonalSpawn()
    {
        using var fixture = new Fixture(1);
        fixture.Role.ForcedSpawn = new PlayerSpawnPos { x = 40, y = 50, z = 60, yaw = 0, RemainingUses = 1 };
        Assert.Equal(0, fixture.Player.GetSpawnPosition(true).UsesLeft);
        Assert.Null(fixture.Role.ForcedSpawn);
        Assert.NotNull(fixture.Data.SpawnPosition);
        Assert.Empty(fixture.Packets);
    }

    /// <summary>A role fallback with no personal point does not produce a spurious personal clear.</summary>
    [Fact]
    public void DefaultRoleWithoutPersonalPointDoesNotSendClear()
    {
        using var fixture = new Fixture(1);
        fixture.Data.SpawnPosition = null;
        fixture.Role.DefaultSpawn = new PlayerSpawnPos { x = 40, y = 50, z = 60, yaw = 0, RemainingUses = 1 };
        Assert.Equal(0, fixture.Player.GetSpawnPosition(true).UsesLeft);
        Assert.Null(fixture.Role.DefaultSpawn);
        Assert.Empty(fixture.Packets);
    }
    #endregion

    #region Private
    /// <summary>Initializes only the engine fields used by the spawn lookup, avoiding server startup and world loading.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly Harmony harmony = new("VanillaExpanded.Tests.SpawnConsumption");
        private readonly SpawnDecalServerSystem system = new();
        private readonly SpawnDecalServerSystem? previousInstance = SpawnDecalServerSystem.Instance;
        public readonly ServerPlayer Player;
        public readonly ServerWorldPlayerData Data;
        public readonly PlayerRole Role = new() { Code = "test" };
        public readonly List<(Packet_TemporalSpawn Packet, IServerPlayer[] Players)> Packets = [];

        #region Public API
        /// <summary>Connects the real installed engine lookup to the production patch and a captured server channel.</summary>
        public Fixture(int uses)
        {
            var server = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
            Player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
            Data = (ServerWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ServerWorldPlayerData));
            AccessTools.Field(typeof(ServerWorldPlayerData), "PlayerUID").SetValue(Data, "spawn-test");
            Data.SpawnPosition = new PlayerSpawnPos { x = 10, y = 20, z = 30, yaw = 0, RemainingUses = uses };
            AccessTools.Field(typeof(ServerPlayer), "worlddata").SetValue(Player, Data);
            AccessTools.Field(typeof(ServerPlayer), "server").SetValue(Player, server);
            server.PlayersByUid = new Dictionary<string, ServerPlayer> { ["spawn-test"] = Player };
            server.PlayerDataManager = (PlayerDataManager)RuntimeHelpers.GetUninitializedObject(typeof(PlayerDataManager));
            server.PlayerDataManager.PlayerDataByUid = new Dictionary<string, ServerPlayerData>
            {
                ["spawn-test"] = new ServerPlayerData { PlayerUID = "spawn-test", RoleCode = Role.Code }
            };
            server.Config = (ServerConfig)RuntimeHelpers.GetUninitializedObject(typeof(ServerConfig));
            server.Config.RolesByCode = new Dictionary<string, PlayerRole> { [Role.Code] = Role };
            server.WorldMap = (ServerWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ServerWorldMap));
            AccessTools.Field(typeof(ServerWorldMap), "mapsize").SetValue(server.WorldMap, new Vec3i(100, 100, 100));

            var api = new Mock<ICoreServerAPI>();
            api.SetupGet(a => a.Logger).Returns(Mock.Of<ILogger>());
            var channel = new Mock<IServerNetworkChannel>();
            channel.Setup(c => c.SendPacket(It.IsAny<Packet_TemporalSpawn>(), It.IsAny<IServerPlayer[]>()))
                .Callback<Packet_TemporalSpawn, IServerPlayer[]>((packet, players) => Packets.Add((packet, players)));
            AccessTools.Field(typeof(SpawnDecalServerSystem), "sapi").SetValue(system, api.Object);
            AccessTools.Field(typeof(SpawnDecalServerSystem), "channel").SetValue(system, channel.Object);
            typeof(SpawnDecalServerSystem).GetProperty(nameof(SpawnDecalServerSystem.Instance))!.SetValue(null, system);
            harmony.CreateClassProcessor(typeof(ServerPlayerPatches)).Patch();
        }

        /// <summary>Removes the isolated patch and restores the previous singleton.</summary>
        public void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            typeof(SpawnDecalServerSystem).GetProperty(nameof(SpawnDecalServerSystem.Instance))!.SetValue(null, previousInstance);
        }
        #endregion
    }
    #endregion
}
