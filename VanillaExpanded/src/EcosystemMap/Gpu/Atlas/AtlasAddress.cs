namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Pixel rectangle identity and generation within one immutable atlas layout.</summary>
internal readonly record struct AtlasAddress(int Index, long Generation, int X, int Y, int Size);
