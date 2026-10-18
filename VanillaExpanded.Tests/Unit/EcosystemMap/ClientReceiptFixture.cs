using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Creates only the installed map dictionaries and packet processor state required by headless receipt tests.</summary>
internal sealed class ClientReceiptFixture
{
    internal ClientMain Game { get; } = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
    internal SystemNetworkProcess Network { get; } = (SystemNetworkProcess)RuntimeHelpers.GetUninitializedObject(typeof(SystemNetworkProcess));
    internal SystemUnloadChunks Unload { get; } = (SystemUnloadChunks)RuntimeHelpers.GetUninitializedObject(typeof(SystemUnloadChunks));
    internal Dictionary<long, ClientMapChunk> Chunks { get; } = new();
    internal static MethodInfo Process { get; } = AccessTools.DeclaredMethod(typeof(SystemNetworkProcess), "ProcessInBackground");
    internal static MethodInfo UnloadMethod { get; } = AccessTools.DeclaredMethod(typeof(SystemUnloadChunks), "HandleChunkUnload");

    #region Public API
    /// <summary>Initializes just the fields consumed by installed map-chunk receipt and the adapter's loaded inventory.</summary>
    internal ClientReceiptFixture()
    {
        Game.WorldMap = (ClientWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldMap));
        AccessTools.Field(typeof(ClientWorldMap), "mapsize").SetValue(Game.WorldMap, new Vec3i(10000, 256, 12000));
        AccessTools.Field(typeof(ClientWorldMap), "MapChunks").SetValue(Game.WorldMap, Chunks);
        var regionsField = AccessTools.Field(typeof(ClientWorldMap), "MapRegions");
        regionsField.SetValue(Game.WorldMap, Activator.CreateInstance(regionsField.FieldType));
        AccessTools.Field(typeof(ClientSystem), "game").SetValue(Network, Game);
        AccessTools.Field(typeof(ClientSystem), "game").SetValue(Unload, Game);
    }

    /// <summary>Inserts a real installed regional object without running game startup or terrain generation.</summary>
    internal void AddRegion(int x, int z, IntDataMap2D? climate, IntDataMap2D? forest)
    {
        var field = AccessTools.Field(typeof(ClientWorldMap), "MapRegions");
        var region = (IMapRegion)Activator.CreateInstance(field.FieldType.GetGenericArguments()[1], nonPublic: true)!;
        region.ClimateMap = climate!;
        region.ForestMap = forest!;
        ((IDictionary)field.GetValue(Game.WorldMap)!)[Game.WorldMap.MapRegionIndex2D(x, z)] = region;
    }

    /// <summary>Runs the actual installed packet-17 update and insertion method through any installed Harmony adapter.</summary>
    internal void Receive(int x, int z, ushort value)
    {
        Process.Invoke(Network, new object[] { Packet(x, z, value) });
    }

    /// <summary>Builds a legitimate map-chunk packet with separate generated terrain and rain heights.</summary>
    internal static Packet_Server Packet(int x, int z, ushort value)
    {
        ushort[] heights = Enumerable.Repeat(value, 1024).ToArray();
        byte[] bytes = new byte[2048];
        Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
        return new Packet_Server
        {
            Id = 17,
            MapChunk = new Packet_ServerMapChunk
            {
                ChunkX = x, ChunkZ = z, TerrainHeightMap = bytes, RainHeightMap = new byte[2048], Ymax = value
            }
        };
    }
    #endregion
}

/// <summary>Serializes tests that install the single client world's scoped Harmony receipt adapter.</summary>
[CollectionDefinition("Ecosystem receipt adapters", DisableParallelization = true)]
public sealed class ClientReceiptCollection { }
