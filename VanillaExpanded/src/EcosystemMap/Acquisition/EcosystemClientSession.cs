using System;
using VanillaExpanded.EcosystemMap.Storage;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>Owns one client's capture, purge and durability lifetime independently of map visibility.</summary>
internal sealed class EcosystemClientSession : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly ClientReceiptAdapter receipt;
    private readonly EnvironmentalAcquisition acquisition;
    private readonly EcosystemMapPurgeBinding? purge;
    private readonly OceanCaptureSource? oceans;
    private bool disposed;
    internal EnvironmentalStorage Storage { get; }
    internal GpuHabitatPreparation Preparation { get; }
    internal event Action? Purged;

    #region Public API
    /// <summary>Opens identity-scoped storage, attaches purge and captures only the currently loaded client inventory.</summary>
    internal static EcosystemClientSession Open(ICoreClientAPI api, ClientReceiptAdapter receipt)
    {
        var blocks = api.World.BlockAccessor;
        var identity = new EcosystemStorageIdentity(api.World.SavegameIdentifier, blocks.MapSizeX,
            blocks.MapSizeY, blocks.MapSizeZ, api.World.SeaLevel, blocks.RegionSize, GlobalConstants.ChunkSize);
        // One waiting record per stage bounds copied data without assuming an unmeasured throughput budget.
        var storage = new EnvironmentalStorage(GamePaths.DataPath, identity, api.Logger, queueCapacity: 1);
        return new EcosystemClientSession(api, receipt, storage, identity.ChunkSize);
    }

    /// <summary>Owns supplied world-scoped storage and binds actual receipt/purge boundaries after storage validation.</summary>
    internal EcosystemClientSession(ICoreClientAPI api, ClientReceiptAdapter receipt, EnvironmentalStorage storage, int chunkSize)
    {
        this.api = api;
        this.receipt = receipt;
        Storage = storage;
        acquisition = new EnvironmentalAcquisition(Storage, 1, Report);
        Preparation = new GpuHabitatPreparation(Storage, GpuHabitatLimits.Client);
        try
        {
            Storage.WaitUntilReadyAsync().GetAwaiter().GetResult();
            purge = EcosystemMapPurgeBinding.Attach(api, Storage, () => { Preparation.Reset(); oceans?.RefreshLoaded(); Purged?.Invoke(); });
            receipt.Start(acquisition, chunkSize);
            oceans = OceanCaptureSource.Attach(api, receipt, acquisition, chunkSize, Report);
        }
        catch
        {
            try { oceans?.Dispose(); }
            finally
            {
                receipt.Stop();
                purge?.Dispose();
                try { Preparation.Dispose(); }
                finally { acquisition.ShutdownAsync().GetAwaiter().GetResult(); }
            }
            throw;
        }
    }

    /// <summary>Validates a shrub message and enqueues its original data under this session's receipt-time ticket.</summary>
    internal void Receive(byte[] data)
    {
        if (disposed) return;
        var ticket = acquisition.CurrentEpoch;
        long sequence = acquisition.NextSequence();
        var region = ShrubPayload.Decode(data);
        if (region != null) acquisition.Submit(new(ticket, Region: region, Sequence: sequence));
    }

    /// <summary>Detaches source intake first, then joins capture and durable writes before clearing session-owned callbacks.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { oceans?.Dispose(); }
        finally
        {
            receipt.Stop();
            purge?.Dispose();
            try { Preparation.Dispose(); }
            finally
            {
                try { acquisition.ShutdownAsync().GetAwaiter().GetResult(); }
                finally { Purged = null; }
            }
        }
    }
    #endregion

    #region Private
    /// <summary>Reports persistence failures through the feature boundary without losing them as unobserved tasks.</summary>
    private void Report(Exception error) => api.Logger.Error("Ecosystem environmental capture failed: {0}", error);
    #endregion
}
