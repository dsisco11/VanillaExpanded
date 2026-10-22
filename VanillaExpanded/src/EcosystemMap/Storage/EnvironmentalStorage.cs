using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Serializes independent cache access on a background worker, with bounded admission and distinct purge/shutdown durability contracts.</summary>
internal sealed class EnvironmentalStorage : IDisposable, IAsyncDisposable
{
    private readonly Channel<StorageCommand> queue;
    private readonly ILogger logger;
    internal EcosystemStorageIdentity Identity { get; }
    internal event Action<EnvironmentalChange>? Changed;
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly object shutdownLock = new();
    private readonly EcosystemDatabase database;
    private readonly Task worker;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StorageEpoch epoch = new(Guid.NewGuid(), 0);
    private bool accepting = true;
    private Task? shutdown;
    internal string FilePath => database.FilePath;
    internal StorageEpoch CurrentEpoch => Volatile.Read(ref epoch);

    #region Public API
    #region Opening and records
    #region Initialization
    /// <summary>Starts a bounded serialized worker; the caller supplies queue capacity for its acquisition workload.</summary>
    internal EnvironmentalStorage(string dataPath, EcosystemStorageIdentity identity, ILogger logger, int queueCapacity)
    {
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        Identity = identity;
        this.logger = logger;
        database = new EcosystemDatabase(dataPath, identity, logger);
        queue = Channel.CreateBounded<StorageCommand>(new BoundedChannelOptions(queueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        worker = Task.Run(ProcessAsync);
    }

    /// <summary>Waits for identity/schema validation; all database creation and I/O occur on the worker.</summary>
    internal Task WaitUntilReadyAsync() => ready.Task;
    /// <summary>Checks source and derived result validity, including normal shutdown.</summary>
    internal bool IsCurrent(StorageEpoch candidate) => Volatile.Read(ref accepting) && ReferenceEquals(CurrentEpoch, candidate);

    #endregion
    #region Original records
    /// <summary>Queues an immutable partial regional update, returning false if its producer belongs to a purged/closed session.</summary>
    internal Task<bool> WriteRegionAsync(RegionalEnvironment record, StorageEpoch producer) =>
        SubmitAsync(producer, db => { db.WriteRegion(record); Notify(new(Region: record.Key)); return true; }, false);

    /// <summary>Queues original elevation data under the producer's session ticket.</summary>
    internal Task<bool> WriteTileAsync(TileEnvironment tile, StorageEpoch producer) =>
        SubmitAsync(producer, db => { db.WriteTile(tile); Notify(new(Tile: tile.Key)); return true; }, false);

    /// <summary>Reads previously captured local environmental records without loading any game region.</summary>
    internal async Task<StorageRead<RegionalEnvironment>> ReadRegionAsync(EcosystemRegionKey key)
    {
        StorageEpoch ticket = CurrentEpoch;
        var value = await SubmitAsync(ticket, db => db.ReadRegion(key), null).ConfigureAwait(false);
        return new StorageRead<RegionalEnvironment>(ticket, value);
    }

    /// <summary>Reads saved tile elevations; callers retain the ticket when preparing disposable results.</summary>
    internal async Task<StorageRead<TileEnvironment>> ReadTileAsync(EcosystemTileKey key)
    {
        StorageEpoch ticket = CurrentEpoch;
        var value = await SubmitAsync(ticket, db => db.ReadTile(key), null).ConfigureAwait(false);
        return new StorageRead<TileEnvironment>(ticket, value);
    }
    #endregion
    #region Upload-ready records
    /// <summary>Queues upload-ready regional reads and any local cache rebuild on the serialized storage worker.</summary>
    internal async Task<StorageRead<PackedRegionalData>> ReadRegionForUploadAsync(EcosystemRegionKey key)
    {
        StorageEpoch ticket = CurrentEpoch;
        var value = await SubmitAsync(ticket, db => db.ReadRegionForUpload(key), null).ConfigureAwait(false);
        return new StorageRead<PackedRegionalData>(ticket, value);
    }

    /// <summary>Queues persisted height/flag reads and any required channel packing away from rendering callbacks.</summary>
    internal async Task<StorageRead<PackedTerrainData>> ReadTileForUploadAsync(EcosystemTileKey key)
    {
        StorageEpoch ticket = CurrentEpoch;
        var value = await SubmitAsync(ticket, db => db.ReadTileForUpload(key), null).ConfigureAwait(false);
        return new StorageRead<PackedTerrainData>(ticket, value);
    }
    /// <summary>Copies bounded distinct requests before queuing one worker operation; missing records remain individually nullable.</summary>
    internal async Task<StorageRead<PackedReadBatch>> ReadPackedBatchAsync(
        IReadOnlyCollection<EcosystemRegionKey> regions, IReadOnlyCollection<EcosystemTileKey> tiles)
    {
        if (regions.Count > PackedReadBatch.MaximumRecords || tiles.Count > PackedReadBatch.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(tiles));
        var regionKeys = new HashSet<EcosystemRegionKey>(regions);
        var tileKeys = new HashSet<EcosystemTileKey>(tiles);
        StorageEpoch ticket = CurrentEpoch;
        var value = await SubmitAsync(ticket, db => db.ReadPackedBatch(regionKeys, tileKeys), null).ConfigureAwait(false);
        return new StorageRead<PackedReadBatch>(ticket, value);
    }
    #endregion
    #endregion

    #region Purge and shutdown
    /// <summary>Advances producer/result validity and orders a cache clear before any writes admitted under the new ticket.</summary>
    internal async Task PurgeAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StorageEpoch previous;
        await admission.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!accepting) throw new ObjectDisposedException(nameof(EnvironmentalStorage));
            previous = CurrentEpoch;
            Volatile.Write(ref epoch, new StorageEpoch(previous.Session, checked(previous.Generation + 1)));
            await queue.Writer.WriteAsync(new StorageCommand(db => { db.Purge(); completion.SetResult(); },
                error => completion.TrySetException(error))).ConfigureAwait(false);
        }
        finally { admission.Release(); }
        // Never invoke cancellation callbacks while holding admission or a database transaction.
        try { previous.CancelDisposableWork(); }
        finally { await completion.Task.ConfigureAwait(false); }
    }

    /// <summary>Stops new acquisition, cancels disposable work, drains accepted persistent operations, and then closes storage.</summary>
    internal Task ShutdownAsync()
    {
        // Publish the shutdown task before cancellation observers can reenter the owner.
        lock (shutdownLock) return shutdown ??= Task.Run(StopAsync);
    }

    /// <summary>Synchronously joins the worker for vanilla's synchronous shutdown callbacks.</summary>
    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();
    /// <summary>Allows asynchronous owners to flush accepted persistent records before releasing their world state.</summary>
    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);
    #endregion
    #endregion

    #region Private
    #region Admission and worker
    /// <summary>Notifies disposable consumers only after commit; observer failure must not turn a durable write into a failed receipt.</summary>
    private void Notify(EnvironmentalChange change)
    {
        try { Changed?.Invoke(change); }
        catch (Exception error) { logger.Error("Ecosystem input invalidation observer failed: {0}", error); }
    }

    /// <summary>Uses bounded async backpressure and serial admission so purge barriers cannot be overtaken by new-generation writes.</summary>
    private async Task<T> SubmitAsync<T>(StorageEpoch producer, System.Func<EcosystemDatabase, T> operation, T unavailable)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await admission.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrent(producer)) return unavailable;
            await queue.Writer.WriteAsync(new StorageCommand(db =>
            {
                // Purge invalidates queued operations. Shutdown cancels only disposable work and must drain accepted writes.
                if (!ReferenceEquals(CurrentEpoch, producer)) { completion.SetResult(unavailable); return; }
                T result = operation(db);
                completion.SetResult(ReferenceEquals(CurrentEpoch, producer) ? result : unavailable);
            }, error => completion.TrySetException(error))).ConfigureAwait(false);
        }
        finally { admission.Release(); }
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>Owns connection open, every read/write/transaction and final close, independently of render or map visibility.</summary>
    private async Task ProcessAsync()
    {
        Exception? failure = null;
        try
        {
            database.Open();
            ready.SetResult();
            await foreach (StorageCommand command in queue.Reader.ReadAllAsync().ConfigureAwait(false))
                command.Execute(database);
        }
        catch (Exception error)
        {
            failure = error;
            ready.TrySetException(error);
            queue.Writer.TryComplete(error);
            while (queue.Reader.TryRead(out var command)) command.Reject(error);
            throw;
        }
        finally
        {
            database.Close();
            if (failure != null) Volatile.Write(ref accepting, false);
        }
    }
    #endregion

    #region Lifetime
    /// <summary>Closes admission without advancing the persistent generation, so accepted writes survive cancellation of disposable jobs.</summary>
    private async Task StopAsync()
    {
        await admission.WaitAsync().ConfigureAwait(false);
        try
        {
            Volatile.Write(ref accepting, false);
            queue.Writer.TryComplete();
        }
        finally { admission.Release(); }
        try { CurrentEpoch.CancelDisposableWork(); }
        finally { await worker.ConfigureAwait(false); }
    }
    #endregion
    #endregion
}
