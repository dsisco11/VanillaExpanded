using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one lazy graphics-thread upload scope before drawing; cache-only frames never query GL upload state.</summary>
internal sealed class GpuHabitatUploads
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly IHabitatUploadApi api;
    private Batch? current;
    private GpuHabitatUploadState? state;

    #region Public API
    /// <summary>Injects the narrow state boundary; texture allocation/transfers retain their existing native API.</summary>
    internal GpuHabitatUploads(IHabitatUploadApi api) { this.api = api; }
    /// <summary>Opens a non-nested scope without querying or changing GL until its first allocation or upload.</summary>
    internal IDisposable Begin()
    {
        CheckThread();
        if (current != null) throw new InvalidOperationException("A habitat upload batch is already open.");
        current = new Batch(this);
        return current;
    }
    /// <summary>Requires the owning scope and establishes shared transfer state only on the first resource operation.</summary>
    internal void EnsureActive()
    {
        CheckThread();
        if (current == null) throw new InvalidOperationException("Habitat texture uploads require an upload batch.");
        state ??= new GpuHabitatUploadState(api);
    }
    #endregion

    #region Private
    /// <summary>Restores before drawing and forgets this batch even if state restoration fails.</summary>
    private void End(Batch batch)
    {
        CheckThread();
        if (!ReferenceEquals(current, batch)) return;
        try { state?.Dispose(); }
        finally { state = null; current = null; }
    }
    /// <summary>Prevents both batch setup and delayed disposal from crossing graphics contexts.</summary>
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Habitat upload batches require the graphics thread.");
    }
    /// <summary>Retains its exact owner identity so repeated disposal cannot close a later batch.</summary>
    private sealed class Batch : IDisposable
    {
        private readonly GpuHabitatUploads owner;
        /// <summary>Captures only scope ownership; no GL work occurs here.</summary>
        internal Batch(GpuHabitatUploads owner) { this.owner = owner; }
        /// <summary>Ends only this batch through its creating owner.</summary>
        public void Dispose() => owner.End(this);
    }
    #endregion
}
