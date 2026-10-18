using System.Collections.Concurrent;
using HarmonyLib;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises installed packet insertion, initial snapshots and the shared unload boundary under concurrent mutation.</summary>
[Collection("Ecosystem receipt adapters")]
public sealed class ClientReceiptAdapterTests
{
    #region Public API
    /// <summary>Actual packet updates race initial enumeration and synchronized removals without corrupting snapshots or blocking shutdown.</summary>
    [Fact]
    public async Task InitialInventoryReceiptAndUnloadShareAnExceptionSafeGate()
    {
        using var fixture = new StorageFixture();
        var client = new ClientReceiptFixture();
        var errors = new ConcurrentQueue<Exception>();
        using var adapter = new ClientReceiptAdapter(client.Game, errors.Enqueue);
        client.AddRegion(-3, 7, new IntDataMap2D { Size = 1, Data = new[] { int.MinValue } },
            new IntDataMap2D { Size = 1, Data = new[] { 255 } });
        client.Receive(0, 0, 12);
        var storage = fixture.Open();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        var unloadPatch = new Harmony("vanillaexpanded.tests.ecosystem.unload");
        try
        {
            // Substitute only voxel-unload side effects. The real method's adapter prefix/finalizer still coordinate dictionary removal.
            unloadPatch.Patch(ClientReceiptFixture.UnloadMethod, prefix:
                new HarmonyMethod(typeof(ClientReceiptAdapterTests), nameof(RemoveMapChunk)) { priority = Priority.Last });
            await Task.WhenAll(
                Task.Run(() => adapter.Start(acquisition, 32)),
                Task.Run(() => { for (int i = 0; i < 120; i++) client.Receive(i % 16, 0, (ushort)i); }),
                Task.Run(() =>
                {
                    for (int i = 0; i < 120; i++)
                        ClientReceiptFixture.UnloadMethod.Invoke(client.Unload, new object[] { ClientReceiptFixture.Packet(i % 16, 0, 0) });
                })).WaitAsync(TimeSpan.FromSeconds(30));
            for (int x = 0; x < 16; x++) client.Receive(x, 0, (ushort)(200 + x));
            adapter.Stop();
            await acquisition.ShutdownAsync();
            client.Receive(50, 0, 77); // A stopped owner cannot capture into this or a future world.
            Assert.Empty(errors);
            await using var reopened = fixture.Open();
            Assert.NotNull((await reopened.ReadRegionAsync(new(-3, 7))).Value!.Climate);
            for (int x = 0; x < 16; x++)
                Assert.All((await reopened.ReadTileAsync(new(x, 0))).Value!.Elevation, value => Assert.Equal((ushort)(200 + x), value));
            Assert.Null((await reopened.ReadTileAsync(new(50, 0))).Value);
        }
        finally
        {
            unloadPatch.UnpatchAll(unloadPatch.Id);
            adapter.Stop();
            await acquisition.ShutdownAsync();
        }
    }


    /// <summary>Copies packet-42 maps before vanilla's deferred region event, retaining exact layout independently of packet arrays.</summary>
    [Fact]
    public async Task OrdinaryRegionPacketCapturesBeforeTheDeferredEvent()
    {
        using var fixture = new StorageFixture();
        var client = new ClientReceiptFixture();
        var errors = new ConcurrentQueue<Exception>();
        using var adapter = new ClientReceiptAdapter(client.Game, errors.Enqueue);
        var storage = fixture.Open();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        try
        {
            adapter.Start(acquisition, 32);
            var taskField = AccessTools.Field(typeof(ClientMain), "MainThreadTasks");
            taskField.SetValue(client.Game, Activator.CreateInstance(taskField.FieldType));
            AccessTools.Field(typeof(ClientMain), "MainThreadTasksLock").SetValue(client.Game, new object());
            var climate = new Packet_IntMap { Size = 3, TopLeftPadding = 1, Data = StorageFixture.Map().Values.ToArray() };
            var other = new Packet_IntMap { Size = 1, Data = new[] { 123 } };
            var packet = new Packet_Server
            {
                Id = 42,
                MapRegion = new Packet_MapRegion
                {
                    RegionX = 3, RegionZ = 8, ClimateMap = climate, ForestMap = other, LandformMap = other,
                    GeologicProvinceMap = other, OceanMap = other,
                    Moddata = Vintagestory.API.Util.SerializerUtil.Serialize(new Dictionary<string, byte[]>())
                }
            };
            ClientReceiptFixture.Process.Invoke(client.Network, new object[] { packet });
            climate.Data[0] = 42;
        }
        finally { adapter.Stop(); await acquisition.ShutdownAsync(); }
        Assert.Empty(errors);
        await using var reopened = fixture.Open();
        var saved = (await reopened.ReadRegionAsync(new(3, 8))).Value!;
        Assert.Equal(int.MinValue, saved.Climate!.Values[0]);
        Assert.Equal(1, saved.Climate.TopLeftPadding);
        Assert.Equal(123, saved.Forest!.Values[0]);
        Assert.Null(saved.Shrubs);
    }

    /// <summary>An exception inside vanilla unload still releases the adapter gate before later inventory/packet operations.</summary>
    [Fact]
    public async Task ExceptionalUnloadReleasesSynchronization()
    {
        using var fixture = new StorageFixture();
        var client = new ClientReceiptFixture();
        var errors = new ConcurrentQueue<Exception>();
        using var adapter = new ClientReceiptAdapter(client.Game, errors.Enqueue);
        Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            ClientReceiptFixture.UnloadMethod.Invoke(client.Unload, new object[] { new Packet_Server() }));
        var storage = fixture.Open();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        try
        {
            await Task.Run(() => adapter.Start(acquisition, 32)).WaitAsync(TimeSpan.FromSeconds(10));
            client.Receive(2, 3, 88);
        }
        finally { adapter.Stop(); await acquisition.ShutdownAsync(); }
        Assert.Empty(errors);
        await using var reopened = fixture.Open();
        Assert.Equal((ushort)88, (await reopened.ReadTileAsync(new(2, 3))).Value!.Elevation[0]);
    }
    #endregion

    #region Private
    /// <summary>Performs the map-dictionary side effect at the actual patched unload boundary without requiring a renderer.</summary>
    private static bool RemoveMapChunk(SystemUnloadChunks __instance, Packet_Server packet)
    {
        var game = (ClientMain)AccessTools.Field(typeof(ClientSystem), "game").GetValue(__instance)!;
        var chunks = (Dictionary<long, Vintagestory.Client.ClientMapChunk>)AccessTools.Field(typeof(ClientWorldMap), "MapChunks").GetValue(game.WorldMap)!;
        chunks.Remove(game.WorldMap.MapChunkIndex2D(packet.MapChunk.ChunkX, packet.MapChunk.ChunkZ));
        return false;
    }
    #endregion
}
