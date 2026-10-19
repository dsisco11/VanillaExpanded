using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>Coalesces normal chunk notifications into background loaded-only semantic snapshots independently of map visibility.</summary>
internal sealed class OceanCaptureSource : IDisposable
{
    private const int PendingLimit = 128;
    private readonly object gate = new();
    private readonly Dictionary<EcosystemTileKey, Request> pending = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly ICoreClientAPI api;
    private readonly ClientReceiptAdapter receipt;
    private readonly EnvironmentalAcquisition acquisition;
    private readonly NativeOceanClassifier classifier;
    private readonly Action<Exception> report;
    private readonly Task worker;
    private Request? rescan;
    private bool stopped;

    #region Public API
    /// <summary>Binds the loaded native category table, starts one worker and observes existing/normal client chunks.</summary>
    internal OceanCaptureSource(ICoreClientAPI api, ClientReceiptAdapter receipt, EnvironmentalAcquisition acquisition,
        NativeOceanClassifier classifier, Action<Exception> report)
    {
        this.api = api; this.receipt = receipt; this.acquisition = acquisition; this.classifier = classifier; this.report = report;
        api.Event.ChunkDirty += OnDirty;
        receipt.TileReceived += OnTile;
        worker = Task.Run(ProcessAsync);
        RefreshLoaded();
    }

    /// <summary>Attaches when the native terrain palette is ready; incompatibility leaves ocean classification unavailable.</summary>
    internal static OceanCaptureSource? Attach(ICoreClientAPI api, ClientReceiptAdapter receipt, EnvironmentalAcquisition acquisition,
        int size, Action<Exception> report)
    {
        try
        {
            var terrain = api.ModLoader?.GetModSystem<WorldMapManager>()?.MapLayers.OfType<ChunkMapLayer>().FirstOrDefault();
            if (terrain == null)
            {
                api.Logger?.Warning("Ecosystem ocean classification unavailable: native terrain layer is absent.");
                return null;
            }
            return new(api, receipt, acquisition, new NativeOceanClassifier(terrain, api.World.BlockAccessor, api.World.Blocks, size), report);
        }
        catch (Exception error) { report(error); return null; }
    }

    /// <summary>Schedules a finite loaded inventory after startup or successful purge, retaining the producer epoch.</summary>
    internal void RefreshLoaded()
    {
        lock (gate)
        {
            if (stopped) return;
            rescan = new(acquisition.CurrentEpoch, acquisition.NextSequence());
            Signal();
        }
    }

    /// <summary>Detaches intake and joins accepted snapshots before acquisition/storage are drained.</summary>
    public void Dispose()
    {
        api.Event.ChunkDirty -= OnDirty;
        receipt.TileReceived -= OnTile;
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            Signal();
        }
        try { worker.GetAwaiter().GetResult(); }
        finally { wake.Dispose(); }
    }
    #endregion

    #region Private
    /// <summary>Only queues coordinates on the event thread; saturated notifications coalesce into one loaded-inventory scan.</summary>
    private void OnDirty(Vec3i pos, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        OnTile(new EcosystemTileKey(pos.X, pos.Z), acquisition.CurrentEpoch, acquisition.NextSequence());
    }

    /// <summary>Retries normal map-chunk arrivals if voxel notifications preceded their rain-height packet.</summary>
    private void OnTile(EcosystemTileKey key, StorageEpoch epoch, long sequence)
    {
        lock (gate)
        {
            // A delayed old packet must never replace a post-purge obligation or a newer observation.
            if (stopped || !ReferenceEquals(acquisition.CurrentEpoch, epoch)) return;
            if (pending.TryGetValue(key, out var previous) && ReferenceEquals(previous.Epoch, epoch)
                && previous.Sequence >= sequence) return;
            var request = new Request(epoch, sequence);
            if (pending.ContainsKey(key) || pending.Count < PendingLimit) pending[key] = request;
            else if (rescan == null || !ReferenceEquals(rescan.Epoch, epoch) || rescan.Sequence < sequence) rescan = request;
            Signal();
        }
    }
    /// <summary>Wakes a sleeping worker once without growing a queue of redundant wakeups; caller holds gate.</summary>
    private void Signal()
    {
        if (wake.CurrentCount == 0) wake.Release();
    }

    /// <summary>Copies only currently available surface categories, outside the event/render thread and before bounded write admission.</summary>
    private async Task ProcessAsync()
    {
        while (true)
        {
            await wake.WaitAsync().ConfigureAwait(false);
            KeyValuePair<EcosystemTileKey, Request>[] batch;
            Request? inventory;
            bool ending;
            lock (gate)
            {
                batch = pending.ToArray(); pending.Clear(); inventory = rescan; rescan = null; ending = stopped;
            }
            // Freeze/coalesce keys under our gate, then release it before native reads or bounded write admission.
            foreach (var item in batch) Capture(item.Key, item.Value);
            if (inventory != null)
                foreach (var key in receipt.LoadedTileKeys()) Capture(key, inventory);
            if (ending) return;
        }
    }

    /// <summary>Rejects stale epochs before world reads; releases adapter synchronization before any write backpressure.</summary>
    private void Capture(EcosystemTileKey key, Request request)
    {
        if (!ReferenceEquals(acquisition.CurrentEpoch, request.Epoch)) return;
        try
        {
            var tile = receipt.CaptureOcean(key, classifier);
            if (tile != null) acquisition.Submit(new(request.Epoch, Tile: tile, Sequence: request.Sequence));
        }
        catch (Exception error) { report(error); }
    }

    /// <summary>Retains notification-time provenance across asynchronous scheduling and inventory scans.</summary>
    private sealed record Request(StorageEpoch Epoch, long Sequence);
    #endregion
}
