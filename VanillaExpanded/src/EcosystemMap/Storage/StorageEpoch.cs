using System;
using System.Threading;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>A session/generation ticket shared by producers and disposable work; tickets cannot survive purge or a new storage owner.</summary>
internal sealed class StorageEpoch
{
    private readonly CancellationTokenSource disposable = new();
    internal Guid Session { get; }
    internal long Generation { get; }
    internal CancellationToken DisposableWork { get; }

    #region Public API
    /// <summary>Creates a fresh cancellation scope for disposable work, independently of persistent-write lifetime.</summary>
    internal StorageEpoch(Guid session, long generation) { Session = session; Generation = generation; DisposableWork = disposable.Token; }
    /// <summary>Notifies disposable consumers after admission locks have been released.</summary>
    internal void CancelDisposableWork()
    {
        try { disposable.Cancel(); }
        finally { disposable.Dispose(); }
    }
    #endregion
}
