namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Pairs cached input with its validity ticket so a consumer can reject completed reads after purge or world exit.</summary>
internal sealed record StorageRead<T>(StorageEpoch Epoch, T? Value) where T : class;
