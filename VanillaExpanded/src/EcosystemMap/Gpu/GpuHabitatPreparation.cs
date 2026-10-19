using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns bounded disposable input preparation, source reuse and stale-result rejection without querying the game world.</summary>
internal sealed class GpuHabitatPreparation : IDisposable
{
    private readonly object gate = new();
    private readonly EnvironmentalStorage storage;
    private readonly GpuHabitatLimits limits;
    private readonly BoundedLru<EcosystemRegionKey, EnvironmentalInput<RegionalEnvironment>> regions;
    private readonly BoundedLru<EcosystemTileKey, EnvironmentalInput<TileEnvironment>> tiles;
    private readonly BoundedLru<EcosystemTileKey, GpuHabitatBundle> bundles;
    private readonly Channel<GpuHabitatJob> jobs;
    private readonly HashSet<EcosystemTileKey> pending = new();
    private readonly Task worker;
    private CancellationTokenSource scope = new();
    private long regionReads, tileReads, preparedBundles;
    internal (long Regions, long Tiles, long Bundles) Work
    { get { lock (gate) return (regionReads, tileReads, preparedBundles); } }
    private bool suspended;
    private bool disposed;
    internal (int Regions, int SourceTiles, int Bundles, int Pending) Counts
    { get { lock (gate) return (regions.Count, tiles.Count, bundles.Count, pending.Count); } }

    #region Public API
    #region Construction and view lifetime
    /// <summary>Starts one worker with bounded nonblocking admission and explicitly chosen workload limits.</summary>
    internal GpuHabitatPreparation(EnvironmentalStorage storage, GpuHabitatLimits limits)
    {
        this.storage = storage; this.limits = limits;
        regions = new(limits.Regions); tiles = new(limits.SourceTiles); bundles = new(limits.Bundles);
        jobs = Channel.CreateBounded<GpuHabitatJob>(new BoundedChannelOptions(limits.PendingJobs)
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        storage.Changed += OnInputsChanged;
        worker = Task.Run(ProcessAsync);
    }

    /// <summary>Retains the entire finite explored view, using configured counts only as off-view cache floors.</summary>
    internal GpuHabitatLimits SetView(IReadOnlyCollection<EcosystemTileKey> visible)
    {
        var regionKeys = new HashSet<EcosystemRegionKey>();
        foreach (var tile in visible) regionKeys.Add(storage.Identity.RegionFor(tile));
        var retained = new GpuHabitatLimits(Math.Max(limits.Regions, regionKeys.Count),
            Math.Max(limits.SourceTiles, visible.Count), Math.Max(limits.Bundles, visible.Count), limits.PendingJobs);
        lock (gate)
        {
            // Shrinking may revoke off-view snapshots. A stable view always fits, so ordinary admission cannot evict its inputs.
            foreach (var input in regions.Resize(retained.Regions)) input.Valid = false;
            foreach (var input in tiles.Resize(retained.SourceTiles)) input.Valid = false;
            bundles.Resize(retained.Bundles);
        }
        return retained;
    }
    /// <summary>Revokes old view/coverage receipts, optionally suspending admission on map close; cached source data remains reusable.</summary>
    internal void ChangeView(bool suspend = false)
    {
        CancellationTokenSource old;
        lock (gate)
        {
            if (disposed) return;
            suspended = suspend; old = ReplaceScope();
        }
        Cancel(old);
    }

    /// <summary>Intentionally discards all disposable/source caches on an explicit ecosystem purge.</summary>
    internal void Reset()
    {
        CancellationTokenSource old;
        lock (gate)
        {
            if (disposed) return;
            old = ReplaceScope(); bundles.Clear();
            foreach (var input in regions.Clear()) input.Valid = false;
            foreach (var input in tiles.Clear()) input.Valid = false;
        }
        Cancel(old);
    }
    #endregion

    #region Requests and lifetime
    /// <summary>Returns a current cache receipt or admits one bounded job; a full queue tells presentation to retry later.</summary>
    internal bool TryPrepare(EcosystemTileKey key, out Task<GpuHabitatReceipt?> result)
    {
        lock (gate)
        {
            result = Task.FromResult<GpuHabitatReceipt?>(null);
            if (disposed || suspended) return false;
            if (bundles.TryGet(key, out var entry) && InputsCurrent(entry!))
            {
                result = Task.FromResult<GpuHabitatReceipt?>(new(entry!, scope));
                return true;
            }
            if (pending.Contains(key)) return false;
            var completion = new TaskCompletionSource<GpuHabitatReceipt?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var job = new GpuHabitatJob(key, scope, completion);
            if (!jobs.Writer.TryWrite(job)) return false;
            pending.Add(key); result = completion.Task; return true;
        }
    }

    /// <summary>Checks view, world/purge and input leases immediately before a caller publishes an input bundle.</summary>
    internal bool IsCurrent(GpuHabitatReceipt result)
    {
        lock (gate) return !disposed && !suspended && ReferenceEquals(result.Scope, scope) && InputsCurrent(result.Bundle);
    }

    /// <summary>Cancels and joins only disposable jobs; persistent capture/storage lifetimes remain owned by the session.</summary>
    public void Dispose()
    {
        CancellationTokenSource old;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; storage.Changed -= OnInputsChanged;
            old = scope; jobs.Writer.TryComplete();
            foreach (var input in regions.Clear()) input.Valid = false;
            foreach (var input in tiles.Clear()) input.Valid = false;
            bundles.Clear();
        }
        Cancel(old);
        worker.GetAwaiter().GetResult();
    }
    #endregion
    #endregion

    #region Private
    #region Worker and local snapshots
    /// <summary>Drains bounded jobs, completing cancelled/stale requests without allowing an obsolete upload bundle to replace current output.</summary>
    private async Task ProcessAsync()
    {
        await foreach (var job in jobs.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { job.Completion.TrySetResult(await PrepareAsync(job).ConfigureAwait(false)); }
            catch (Exception error) { job.Completion.TrySetException(error); }
            finally { lock (gate) pending.Remove(job.Key); }
        }
    }

    /// <summary>Reads only original local snapshots; species criteria and per-column comparisons stay on the GPU.</summary>
    private async Task<GpuHabitatReceipt?> PrepareAsync(GpuHabitatJob job)
    {
        if (!JobCurrent(job)) return null;
        var region = await RegionAsync(storage.Identity.RegionFor(job.Key)).ConfigureAwait(false);
        var tile = await TileAsync(job.Key).ConfigureAwait(false);
        lock (gate)
        {
            if (!JobCurrent(job) || !InputCurrent(region) || !InputCurrent(tile)) return null;
            var bundle = new GpuHabitatBundle(job.Key, storage.Identity, region, tile);
            bundles.Put(job.Key, bundle);
            preparedBundles++;
            return new(bundle, scope);
        }
    }
    /// <summary>Loads a shared regional snapshot while retaining a pending lease so receipt updates can invalidate an in-flight read.</summary>
    private async Task<EnvironmentalInput<RegionalEnvironment>> RegionAsync(EcosystemRegionKey key)
    {
        EnvironmentalInput<RegionalEnvironment> input;
        lock (gate)
        {
            if (regions.TryGet(key, out var cached) && InputCurrent(cached!) && cached!.Loaded) return cached;
            input = new(storage.CurrentEpoch);
            regionReads++;
            var evicted = regions.Put(key, input);
            if (evicted != null) evicted.Valid = false;
        }
        var read = await storage.ReadRegionAsync(key).ConfigureAwait(false);
        lock (gate)
        {
            if (!ReferenceEquals(read.Epoch, input.Epoch)) input.Valid = false;
            input.Value = read.Value; input.Loaded = true;
        }
        return input;
    }

    /// <summary>Loads a separate generated-height snapshot with the same targeted update and purge provenance guarantees.</summary>
    private async Task<EnvironmentalInput<TileEnvironment>> TileAsync(EcosystemTileKey key)
    {
        EnvironmentalInput<TileEnvironment> input;
        lock (gate)
        {
            if (tiles.TryGet(key, out var cached) && InputCurrent(cached!) && cached!.Loaded) return cached;
            input = new(storage.CurrentEpoch);
            tileReads++;
            var evicted = tiles.Put(key, input);
            if (evicted != null) evicted.Valid = false;
        }
        var read = await storage.ReadTileAsync(key).ConfigureAwait(false);
        lock (gate)
        {
            if (!ReferenceEquals(read.Epoch, input.Epoch)) input.Valid = false;
            input.Value = read.Value; input.Loaded = true;
        }
        return input;
    }
    #endregion

    #region Invalidation
    /// <summary>Revokes only the changed regional or elevation lease and corresponding input bundles after a committed write.</summary>
    private void OnInputsChanged(EnvironmentalChange change)
    {
        lock (gate)
        {
            if (change.Region is { } region)
            {
                var removed = regions.Remove(region);
                if (removed != null) removed.Valid = false;
                foreach (var key in bundles.Keys)
                    if (storage.Identity.RegionFor(key) == region) bundles.Remove(key);
            }
            if (change.Tile is { } tile)
            {
                var removed = tiles.Remove(tile);
                if (removed != null) removed.Valid = false;
                bundles.Remove(tile);
            }
        }
    }

    /// <summary>Checks an upload bundle's exact source leases under the owner lock.</summary>
    private bool InputsCurrent(GpuHabitatBundle entry) => InputCurrent(entry.Region) && InputCurrent(entry.Elevation);

    /// <summary>Checks invalidation and persistent session provenance under the owner lock.</summary>
    private bool InputCurrent<T>(EnvironmentalInput<T> input) where T : class =>
        input.Valid && storage.IsCurrent(input.Epoch);

    /// <summary>Serializes scope checks with view changes; no game-owned locks are involved.</summary>
    private bool JobCurrent(GpuHabitatJob job)
    {
        lock (gate) return !disposed && !suspended && ReferenceEquals(job.Scope, scope);
    }

    /// <summary>Replaces scope identity before callbacks execute, leaving outstanding receipts visibly stale.</summary>
    private CancellationTokenSource ReplaceScope()
    {
        var previous = scope; scope = new(); return previous;
    }

    /// <summary>Runs disposable cancellation observers after releasing the owner lock.</summary>
    private static void Cancel(CancellationTokenSource previous)
    {
        try { previous.Cancel(); }
        finally { previous.Dispose(); }
    }
    #endregion
    #endregion
}
