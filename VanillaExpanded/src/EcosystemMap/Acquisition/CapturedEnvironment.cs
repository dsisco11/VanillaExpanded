using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>An owned capture and its receipt-time epoch, retained together across bounded queue waits.</summary>
internal sealed record CapturedEnvironment(StorageEpoch Epoch, RegionalEnvironment? Region = null, TileEnvironment? Tile = null, long Sequence = 0);
