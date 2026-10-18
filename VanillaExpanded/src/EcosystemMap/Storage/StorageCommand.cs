using System;
using System.Threading.Tasks;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>One bounded worker operation and its asynchronous receipt; failures cannot strand the caller's completion.</summary>
internal sealed class StorageCommand
{
    private readonly Action<EcosystemDatabase> execute;
    private readonly Action<Exception> reject;

    #region Public API
    /// <summary>Associates command execution and failure completion without exposing the database to producers.</summary>
    internal StorageCommand(Action<EcosystemDatabase> execute, Action<Exception> reject) { this.execute = execute; this.reject = reject; }
    /// <summary>Completes failed commands explicitly and leaves the serialized worker available for later operations.</summary>
    internal void Execute(EcosystemDatabase database)
    {
        try { execute(database); }
        catch (Exception error) { reject(error); }
    }
    /// <summary>Rejects queued work when opening the connection or the worker itself fails.</summary>
    internal void Reject(Exception error) => reject(error);
    #endregion
}
