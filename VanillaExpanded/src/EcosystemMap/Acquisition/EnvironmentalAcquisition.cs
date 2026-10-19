using System;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>Separates copied receipt jobs from durable writes and joins both in order when acquisition stops.</summary>
internal sealed class EnvironmentalAcquisition
{
    private readonly EnvironmentalStorage storage;
    private readonly Channel<CapturedEnvironment> queue;
    private readonly Task worker;
    private readonly object lifetime = new();
    private readonly Action<Exception> report;
    private Task? shutdown;
    private int stopped;
    private long sequence;
    private readonly Dictionary<(EcosystemRegionKey, EnvironmentalCompleteness), long> regionSequences = new();
    private readonly Dictionary<(EcosystemTileKey, EnvironmentalCompleteness), long> tileSequences = new();

    #region Public API
    /// <summary>Creates a bounded copied-snapshot queue; capacity is an explicit workload policy supplied by registration.</summary>
    internal EnvironmentalAcquisition(EnvironmentalStorage storage, int capacity, Action<Exception> report)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.storage = storage;
        this.report = report;
        queue = Channel.CreateBounded<CapturedEnvironment>(new BoundedChannelOptions(capacity)
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        worker = Task.Run(ProcessAsync);
    }

    /// <summary>Reads a producer ticket before copying source data, so purge during a snapshot cannot revive old inputs.</summary>
    internal StorageEpoch CurrentEpoch => storage.CurrentEpoch;

    /// <summary>Orders source copies independently of submission timing after adapter locks are released.</summary>
    internal long NextSequence() => Interlocked.Increment(ref sequence);

    /// <summary>Admits a copied job with bounded backpressure, outside game-owned and adapter locks.</summary>
    internal bool Submit(CapturedEnvironment capture)
    {
        if (Volatile.Read(ref stopped) != 0 || !storage.IsCurrent(capture.Epoch)) return false;
        try { queue.Writer.WriteAsync(capture).AsTask().GetAwaiter().GetResult(); return true; }
        catch (ChannelClosedException) { return false; }
    }

    /// <summary>Stops admission and drains accepted captures before closing durable storage; repeated calls share one receipt.</summary>
    internal Task ShutdownAsync()
    {
        lock (lifetime)
        {
            if (shutdown != null) return shutdown;
            Interlocked.Exchange(ref stopped, 1);
            queue.Writer.TryComplete();
            return shutdown = Task.Run(DrainAsync);
        }
    }
    #endregion

    #region Private
    /// <summary>Serially submits immutable captures; old epochs are rejected by the storage owner after purge.</summary>
    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var capture in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (!storage.IsCurrent(capture.Epoch)) continue;
                    if (capture.Region is { } region)
                    {
                        var ordered = new RegionalEnvironment(region.Key,
                            Newer(region.Key, EnvironmentalCompleteness.Climate, region.Climate, capture.Sequence),
                            Newer(region.Key, EnvironmentalCompleteness.Forest, region.Forest, capture.Sequence),
                            Newer(region.Key, EnvironmentalCompleteness.Shrubs, region.Shrubs, capture.Sequence));
                        if (ordered.Completeness != EnvironmentalCompleteness.None)
                            await storage.WriteRegionAsync(ordered, capture.Epoch).ConfigureAwait(false);
                    }
                    if (capture.Tile is { } tile)
                    {
                        var ordered = new TileEnvironment(tile.Key, tile.Size,
                            NewerTile(tile.Key, EnvironmentalCompleteness.Elevation, tile.HasElevation ? tile.Elevation : null, capture.Sequence),
                            NewerTile(tile.Key, EnvironmentalCompleteness.Ocean, tile.Ocean, capture.Sequence));
                        if (ordered.Completeness != EnvironmentalCompleteness.None)
                            await storage.WriteTileAsync(ordered, capture.Epoch).ConfigureAwait(false);
                    }
                }
                catch (Exception error) { report(error); }
            }
        }
        catch (Exception error)
        {
            // A failed observer must release producers waiting for copied-job capacity before shutdown joins the worker.
            queue.Writer.TryComplete(error);
            throw;
        }
    }

    /// <summary>Orders height and ocean observations independently so either partial arrival preserves the other.</summary>
    private T? NewerTile<T>(EcosystemTileKey key, EnvironmentalCompleteness component, T? value, long ordinal) where T : class
    {
        if (value == null || (tileSequences.TryGetValue((key, component), out long previous) && ordinal < previous)) return null;
        tileSequences[(key, component)] = ordinal;
        return value;
    }

    /// <summary>Retains only the latest available component when source capture and bounded submission interleave.</summary>
    private EnvironmentalMap? Newer(EcosystemRegionKey key, EnvironmentalCompleteness component, EnvironmentalMap? map, long ordinal)
    {
        if (map == null || (regionSequences.TryGetValue((key, component), out long previous) && ordinal < previous)) return null;
        regionSequences[(key, component)] = ordinal;
        return map;
    }

    /// <summary>Waits for receipt jobs before closing the persistent queue, including when acquisition processing fails.</summary>
    private async Task DrainAsync()
    {
        try { await worker.ConfigureAwait(false); }
        finally { await storage.ShutdownAsync().ConfigureAwait(false); }
    }
    #endregion
}
