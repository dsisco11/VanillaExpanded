using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>A cached local-read lease invalidated by an input update, eviction, purge or shutdown.</summary>
internal sealed class EnvironmentalInput<T> where T : class
{
    internal StorageEpoch Epoch { get; }
    internal bool Valid { get; set; } = true;
    internal bool Loaded { get; set; }
    internal T? Value { get; set; }

    #region Public API
    /// <summary>Captures world/purge provenance before the asynchronous local read begins.</summary>
    internal EnvironmentalInput(StorageEpoch epoch) { Epoch = epoch; }
    #endregion
}
