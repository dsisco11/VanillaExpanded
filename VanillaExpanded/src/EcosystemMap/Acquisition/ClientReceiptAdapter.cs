using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>Coordinates packet insertion and unload with an initial loaded-only snapshot; private access stays in this checked adapter.</summary>
internal sealed class ClientReceiptAdapter : IDisposable
{
    private const string PatchId = "vanillaexpanded.ecosystem.clientreceipt";
    private static readonly FieldInfo GameField = RequiredField(typeof(ClientSystem), "game", typeof(ClientMain));
    private static readonly FieldInfo ChunksField = RequiredField(typeof(ClientWorldMap), "MapChunks", typeof(Dictionary<long, ClientMapChunk>));
    private static readonly FieldInfo RegionsField = AccessTools.Field(typeof(ClientWorldMap), "MapRegions")
        ?? throw new NotSupportedException("Client map-region inventory is unavailable.");
    private static ClientReceiptAdapter? active;
    private readonly object gate = new();
    private readonly ClientMain game;
    private readonly Harmony harmony = new(PatchId);
    private readonly Action<Exception> report;
    private EnvironmentalAcquisition? acquisition;
    private int chunkSize;
    private bool disposed;

    #region Public API
    /// <summary>Installs synchronization before normal world receipt; capture starts only when storage identity is ready.</summary>
    internal ClientReceiptAdapter(ClientMain game, Action<Exception> report)
    {
        this.game = game;
        this.report = report;
        if (!typeof(IDictionary).IsAssignableFrom(RegionsField.FieldType))
            throw new NotSupportedException("Client map-region inventory has changed.");
        if (Interlocked.CompareExchange(ref active, this, null) != null)
            throw new InvalidOperationException("An ecosystem receipt adapter is already installed.");
        try
        {
            var receipt = RequiredMethod(typeof(SystemNetworkProcess), "ProcessInBackground", typeof(Packet_Server));
            var unload = RequiredMethod(typeof(SystemUnloadChunks), "HandleChunkUnload", typeof(Packet_Server));
            harmony.Patch(receipt, prefix: new HarmonyMethod(typeof(ClientReceiptAdapter), nameof(BeginReceipt)),
                postfix: new HarmonyMethod(typeof(ClientReceiptAdapter), nameof(CaptureReceipt)),
                finalizer: new HarmonyMethod(typeof(ClientReceiptAdapter), nameof(EndReceipt)));
            harmony.Patch(unload, prefix: new HarmonyMethod(typeof(ClientReceiptAdapter), nameof(BeginUnload)),
                finalizer: new HarmonyMethod(typeof(ClientReceiptAdapter), nameof(EndReceipt)));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Binds capture and copies already-loaded inputs while ordinary packet insertion/removal is excluded.</summary>
    internal void Start(EnvironmentalAcquisition owner, int size)
    {
        List<CapturedEnvironment> snapshots = new();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            acquisition = owner;
            chunkSize = size;
            StorageEpoch ticket = owner.CurrentEpoch;
            foreach (DictionaryEntry entry in (IDictionary)RegionsField.GetValue(game.WorldMap)!)
            {
                long index = (long)entry.Key;
                // Vanilla adds signed X to shifted Z; remove X before decoding the regional row.
                snapshots.Add(new(ticket, EnvironmentalSnapshots.Region(new((int)index, unchecked((int)((index - (int)index) >> 32))), (IMapRegion)entry.Value!), Sequence: owner.NextSequence()));
            }
            foreach (var entry in (Dictionary<long, ClientMapChunk>)ChunksField.GetValue(game.WorldMap)!)
            {
                var pos = game.WorldMap.ChunkPosFromChunkIndex2D(entry.Key);
                var tile = EnvironmentalSnapshots.Tile(new(pos.X, pos.Z), entry.Value, size);
                if (tile != null) snapshots.Add(new(ticket, Tile: tile, Sequence: owner.NextSequence()));
            }
        }
        // Backpressure and storage joins must never run under the dictionary synchronization gate.
        foreach (var snapshot in snapshots) owner.Submit(snapshot);
    }

    /// <summary>Stops capture before the acquisition owner drains; an already-copied late job retains its original ticket.</summary>
    internal void Stop() { lock (gate) acquisition = null; }

    /// <summary>Removes only owned patches and waits for any source snapshot to leave the gate before releasing world references.</summary>
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; acquisition = null; }
        Interlocked.CompareExchange(ref active, null, this);
        harmony.UnpatchAll(PatchId);
    }
    #endregion

    #region Private
    #region Harmony receipt boundary
    /// <summary>Locks only map-region/map-chunk packets, before vanilla looks up or inserts their dictionary entries.</summary>
    private static void BeginReceipt(ClientSystem __instance, Packet_Server packet, out Receipt? __state)
    {
        __state = null;
        var adapter = Volatile.Read(ref active);
        if (adapter != null && (packet.Id == 17 || packet.Id == 42) && ReferenceEquals(GameField.GetValue(__instance), adapter.game))
            __state = adapter.Enter();
    }

    /// <summary>Copies the completed update while insertion is stable, tagging it with the epoch captured before receipt.</summary>
    private static void CaptureReceipt(Packet_Server packet, Receipt? __state)
    {
        if (__state?.Owner == null) return;
        var adapter = __state.Adapter;
        try
        {
            if (packet.Id == 17)
            {
                var key = new EcosystemTileKey(packet.MapChunk.ChunkX, packet.MapChunk.ChunkZ);
                var chunks = (Dictionary<long, ClientMapChunk>)ChunksField.GetValue(adapter.game.WorldMap)!;
                if (chunks.TryGetValue(adapter.game.WorldMap.MapChunkIndex2D(key.X, key.Z), out var chunk))
                {
                    var tile = EnvironmentalSnapshots.Tile(key, chunk, adapter.chunkSize);
                    if (tile != null) __state.Capture = new(__state.Epoch!, Tile: tile, Sequence: __state.Sequence);
                }
            }
            else
            {
                var key = new EcosystemRegionKey(packet.MapRegion.RegionX, packet.MapRegion.RegionZ);
                var regions = (IDictionary)RegionsField.GetValue(adapter.game.WorldMap)!;
                if (regions[adapter.game.WorldMap.MapRegionIndex2D(key.X, key.Z)] is IMapRegion region)
                    __state.Capture = new(__state.Epoch!, EnvironmentalSnapshots.Region(key, region), Sequence: __state.Sequence);
            }
        }
        catch (Exception error) { adapter.report(error); }
    }

    /// <summary>Uses the same gate for vanilla's map-chunk removal path, including exceptional exits.</summary>
    private static void BeginUnload(ClientSystem __instance, out Receipt? __state)
    {
        var adapter = Volatile.Read(ref active);
        __state = adapter != null && ReferenceEquals(GameField.GetValue(__instance), adapter.game) ? adapter.Enter() : null;
    }

    /// <summary>Always releases synchronization before submitting owned data; preserves vanilla's original exception.</summary>
    private static Exception? EndReceipt(Exception? __exception, Receipt? __state)
    {
        if (__state == null) return __exception;
        Monitor.Exit(__state.Adapter.gate);
        if (__exception == null && __state.Capture != null)
        {
            try { __state.Owner!.Submit(__state.Capture); }
            catch (Exception error) { __state.Adapter.report(error); }
        }
        return __exception;
    }
    #endregion

    #region Checked access and synchronization
    /// <summary>Captures the acquisition owner and epoch before any original update can race with purge.</summary>
    private Receipt Enter()
    {
        Monitor.Enter(gate);
        return new Receipt(this, acquisition, acquisition?.CurrentEpoch, acquisition?.NextSequence() ?? 0);
    }

    /// <summary>Validates the exact private field contract instead of allowing unchecked casts throughout acquisition code.</summary>
    private static FieldInfo RequiredField(Type type, string name, Type fieldType)
    {
        var field = AccessTools.Field(type, name);
        return field?.FieldType == fieldType ? field : throw new NotSupportedException("Incompatible ecosystem field: " + name);
    }

    /// <summary>Validates the exact method signature before installing a scoped patch.</summary>
    private static MethodInfo RequiredMethod(Type type, string name, params Type[] parameters) =>
        AccessTools.DeclaredMethod(type, name, parameters) ?? throw new NotSupportedException("Incompatible ecosystem method: " + name);

    /// <summary>Retains the owner, epoch and copied data across one original call and its exception-safe finalizer.</summary>
    private sealed class Receipt
    {
        internal ClientReceiptAdapter Adapter { get; }
        internal EnvironmentalAcquisition? Owner { get; }
        internal StorageEpoch? Epoch { get; }
        internal CapturedEnvironment? Capture { get; set; }
        internal long Sequence { get; }
        /// <summary>Stores receipt-time identity before the original packet mutation begins.</summary>
        internal Receipt(ClientReceiptAdapter adapter, EnvironmentalAcquisition? owner, StorageEpoch? epoch, long sequence)
            { Adapter = adapter; Owner = owner; Epoch = epoch; Sequence = sequence; }
    }
    #endregion
    #endregion
}
